using System.Globalization;
using System.Text.Json;

namespace PoolCompartido;

/// <summary>Un punto del mapa.</summary>
public readonly record struct Coordenada(double Latitud, double Longitud)
{
    /// <summary>Formato "lat,lon" con punto decimal, el que entienden Google Maps y OSRM.</summary>
    public string ATexto() =>
        string.Create(CultureInfo.InvariantCulture, $"{Latitud:0.######},{Longitud:0.######}");
}

/// <summary>
/// HU24 (ruta óptima): convierte una dirección escrita ("Av. Colón 1200, Saldán") en coordenadas,
/// con el servicio gratuito de OpenStreetMap (Nominatim), el mismo de la HU23.
/// Nominatim pide no hacer más de un pedido por segundo: la clase espera sola entre pedidos
/// y recuerda las direcciones que ya buscó para no repetirlas.
/// </summary>
public static class Geocodificador
{
    private static readonly TimeSpan EntrePedidos = TimeSpan.FromSeconds(1.1);
    private static readonly SemaphoreSlim _turno = new(1, 1);
    private static DateTime _ultimoPedido = DateTime.MinValue;

    // Direcciones ya buscadas (también las que no se encontraron, como null)
    private static readonly Dictionary<string, Coordenada?> _memoria = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Dirección del pedido a Nominatim. Si se conoce un punto cercano, se prefieren resultados alrededor.</summary>
    public static Uri ArmarUrl(string texto, Coordenada? cerca)
    {
        string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=ar&accept-language=es"
                     + "&q=" + Uri.EscapeDataString(texto);

        if (cerca is Coordenada c)
        {
            // Una "caja" de unos 50 km alrededor: ayuda con nombres cortos como "UBP" o "la facu"
            var inv = CultureInfo.InvariantCulture;
            url += string.Create(inv, $"&viewbox={c.Longitud - 0.5:0.####},{c.Latitud + 0.5:0.####},{c.Longitud + 0.5:0.####},{c.Latitud - 0.5:0.####}&bounded=0");
        }
        return new Uri(url);
    }

    /// <summary>Saca la coordenada de la respuesta de Nominatim, o null si no encontró nada.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static Coordenada? LeerRespuesta(string json)
    {
        using var documento = JsonDocument.Parse(json);
        if (documento.RootElement.ValueKind != JsonValueKind.Array || documento.RootElement.GetArrayLength() == 0)
            return null;

        var primero = documento.RootElement[0];
        if (primero.TryGetProperty("lat", out var lat) && primero.TryGetProperty("lon", out var lon)
            && double.TryParse(lat.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud)
            && double.TryParse(lon.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud))
            return new Coordenada(latitud, longitud);

        return null;
    }

    /// <summary>Busca las coordenadas de una dirección. Devuelve null si no la encuentra.</summary>
    /// <param name="texto">La dirección o el lugar.</param>
    /// <param name="zona">La localidad (por ejemplo, el origen del viaje), para no confundir calles de otras ciudades.</param>
    /// <param name="cerca">Un punto conocido de la zona, para preferir resultados cercanos.</param>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<Coordenada?> BuscarAsync(string? texto, string? zona = null, Coordenada? cerca = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        string consulta = string.IsNullOrWhiteSpace(zona) || texto.Contains(zona.Trim(), StringComparison.OrdinalIgnoreCase)
            ? texto.Trim()
            : $"{texto.Trim()}, {zona.Trim()}";

        lock (_memoria)
        {
            if (_memoria.TryGetValue(consulta, out var guardada))
                return guardada;
        }

        await _turno.WaitAsync();
        try
        {
            // Respetar el límite de un pedido por segundo
            var espera = _ultimoPedido + EntrePedidos - DateTime.UtcNow;
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera);

            using var pedido = new HttpRequestMessage(HttpMethod.Get, ArmarUrl(consulta, cerca));
            pedido.Headers.UserAgent.ParseAdd(BuscadorDeBarrio.UserAgent);

            using var respuesta = await ClienteHttp.EnviarAsync(pedido);
            _ultimoPedido = DateTime.UtcNow;
            if (!respuesta.IsSuccessStatusCode)
                throw new ServidorException($"OpenStreetMap respondió con código {(int)respuesta.StatusCode}");

            Coordenada? resultado;
            try
            {
                resultado = LeerRespuesta(await respuesta.Content.ReadAsStringAsync());
            }
            catch (JsonException ex)
            {
                RegistroErrores.RegistrarError(ex, "Leer respuesta de OpenStreetMap (buscar dirección)");
                throw new ServidorException("OpenStreetMap devolvió una respuesta inválida");
            }

            lock (_memoria)
                _memoria[consulta] = resultado;
            return resultado;
        }
        finally
        {
            _turno.Release();
        }
    }
}
