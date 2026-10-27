using System.Globalization;
using System.Text.Json;

namespace PoolCompartido;

/// <summary>
/// Herramientas genéricas (Programación Genérica) para elegir el mejor orden de una lista.
/// No saben nada de viajes: sirven para cualquier tipo T.
/// </summary>
public static class Ordenes
{
    /// <summary>
    /// Todas las formas posibles de ordenar los elementos (permutaciones).
    /// Con n elementos hay n! órdenes: con 4 paradas son 24, se recorren al instante.
    /// </summary>
    public static IEnumerable<IReadOnlyList<T>> Permutaciones<T>(IReadOnlyList<T> elementos)
    {
        ArgumentNullException.ThrowIfNull(elementos);
        if (elementos.Count <= 1)
        {
            yield return elementos.ToList();
            yield break;
        }

        for (int i = 0; i < elementos.Count; i++)
        {
            // Se fija el elemento i adelante y se permutan los demás
            var resto = elementos.Where((_, j) => j != i).ToList();
            foreach (var cola in Permutaciones(resto))
            {
                var orden = new List<T>(elementos.Count) { elementos[i] };
                orden.AddRange(cola);
                yield return orden;
            }
        }
    }

    /// <summary>El orden de menor costo total, o la lista tal cual si tiene menos de dos elementos.</summary>
    /// <param name="costo">Cuánto "cuesta" un orden completo (por ejemplo, minutos de manejo).</param>
    public static IReadOnlyList<T> Mejor<T>(IReadOnlyList<T> elementos, Func<IReadOnlyList<T>, double> costo)
    {
        ArgumentNullException.ThrowIfNull(elementos);
        ArgumentNullException.ThrowIfNull(costo);
        if (elementos.Count < 2)
            return elementos;

        IReadOnlyList<T> mejor = elementos;
        double mejorCosto = double.MaxValue;
        foreach (var orden in Permutaciones(elementos))
        {
            double c = costo(orden);
            if (c < mejorCosto)
            {
                mejor = orden;
                mejorCosto = c;
            }
        }
        return mejor;
    }
}

/// <summary>
/// HU24 (ruta óptima): ordena las paradas de los pasajeros para que el conductor haga el
/// camino más corto, sin ir y volver.
/// 1) Pide a OSRM (servicio gratuito de rutas de OpenStreetMap) los tiempos reales de manejo entre todos los puntos.
/// 2) Prueba todos los órdenes posibles y se queda con el de menor tiempo total.
/// Si OSRM no responde, usa la distancia en línea recta, que en una ciudad da casi siempre lo mismo.
/// </summary>
public static class OptimizadorDeRuta
{
    /// <summary>Tiempos de manejo (segundos) entre todos los puntos, en el orden dado.</summary>
    public static Uri ArmarUrlTabla(IReadOnlyList<Coordenada> puntos)
    {
        // OSRM usa "longitud,latitud" separados por ";"
        string lista = string.Join(";", puntos.Select(p =>
            string.Create(CultureInfo.InvariantCulture, $"{p.Longitud:0.######},{p.Latitud:0.######}")));
        return new Uri($"https://router.project-osrm.org/table/v1/driving/{lista}?annotations=duration");
    }

    /// <summary>Lee la matriz de tiempos de la respuesta de OSRM, o null si no la trae completa.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static double[,]? LeerTabla(string json, int cantidad)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;
        if (!raiz.TryGetProperty("code", out var codigo) || codigo.GetString() != "Ok"
            || !raiz.TryGetProperty("durations", out var filas) || filas.GetArrayLength() != cantidad)
            return null;

        var tabla = new double[cantidad, cantidad];
        for (int i = 0; i < cantidad; i++)
        {
            var fila = filas[i];
            if (fila.GetArrayLength() != cantidad) return null;
            for (int j = 0; j < cantidad; j++)
                // null = no hay camino conocido entre esos dos puntos: se lo trata como muy lejos
                tabla[i, j] = fila[j].ValueKind == JsonValueKind.Number ? fila[j].GetDouble() : 1e9;
        }
        return tabla;
    }

    /// <summary>Distancia en línea recta en metros (fórmula de haversine).</summary>
    public static double DistanciaRecta(Coordenada a, Coordenada b)
    {
        const double RadioTierra = 6_371_000;
        double rad(double g) => g * Math.PI / 180;
        double dLat = rad(b.Latitud - a.Latitud), dLon = rad(b.Longitud - a.Longitud);
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(rad(a.Latitud)) * Math.Cos(rad(b.Latitud)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * RadioTierra * Math.Asin(Math.Sqrt(h));
    }

    /// <summary>
    /// Ordena las paradas (las que tienen coordenadas) para hacer el camino más corto
    /// desde el inicio hasta el fin. Cualquiera de los dos extremos puede faltar.
    /// </summary>
    /// <returns>Los índices de <paramref name="paradas"/> en el mejor orden.</returns>
    public static async Task<IReadOnlyList<int>> OrdenarAsync(Coordenada? inicio, Coordenada? fin, IReadOnlyList<Coordenada> paradas)
    {
        ArgumentNullException.ThrowIfNull(paradas);
        var indices = Enumerable.Range(0, paradas.Count).ToList();
        if (paradas.Count < 2)
            return indices;

        // Todos los puntos en una lista: [inicio?] + paradas + [fin?]
        var puntos = new List<Coordenada>();
        int? posInicio = null, posFin = null;
        if (inicio is Coordenada i) { posInicio = puntos.Count; puntos.Add(i); }
        int primeraParada = puntos.Count;
        puntos.AddRange(paradas);
        if (fin is Coordenada f) { posFin = puntos.Count; puntos.Add(f); }

        Func<int, int, double> tiempo = await TiemposAsync(puntos);

        // Costo de un orden: inicio → paradas en ese orden → fin
        double Costo(IReadOnlyList<int> orden)
        {
            double total = 0;
            int? anterior = posInicio;
            foreach (int p in orden)
            {
                int actual = primeraParada + p;
                if (anterior is int a) total += tiempo(a, actual);
                anterior = actual;
            }
            if (posFin is int fn && anterior is int ult) total += tiempo(ult, fn);
            return total;
        }

        return Ordenes.Mejor(indices, Costo);
    }

    /// <summary>Tiempos reales de OSRM, o distancia en línea recta si OSRM no responde.</summary>
    private static async Task<Func<int, int, double>> TiemposAsync(IReadOnlyList<Coordenada> puntos)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, ArmarUrlTabla(puntos));
            pedido.Headers.UserAgent.ParseAdd(BuscadorDeBarrio.UserAgent);
            using var respuesta = await ClienteHttp.EnviarAsync(pedido);

            if (respuesta.IsSuccessStatusCode
                && LeerTabla(await respuesta.Content.ReadAsStringAsync(), puntos.Count) is double[,] tabla)
                return (a, b) => tabla[a, b];

            RegistroErrores.RegistrarAviso(new ServidorException($"OSRM respondió con código {(int)respuesta.StatusCode}"),
                                           "Ruta óptima: se usa la distancia en línea recta");
        }
        catch (Exception ex) when (ex is SinConexionException or JsonException)
        {
            RegistroErrores.RegistrarAviso(ex, "Ruta óptima: se usa la distancia en línea recta");
        }

        return (a, b) => DistanciaRecta(puntos[a], puntos[b]);
    }
}
