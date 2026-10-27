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

/// <summary>Un lugar encontrado en el mapa, con la localidad y la provincia donde queda.</summary>
/// <param name="Localidad">La ciudad o el pueblo (por ejemplo "Villa Allende"); null si no vino.</param>
/// <param name="Provincia">Por ejemplo "Córdoba"; null si no vino.</param>
public record LugarEncontrado(Coordenada Punto, string? Localidad, string? Provincia)
{
    /// <summary>
    /// "Villa Allende, Córdoba": lo que se le agrega a una dirección escrita para que Google Maps
    /// la busque en el lugar correcto. Nunca es un barrio: Google Maps no conoce todos los barrios.
    /// </summary>
    public string? Zona
    {
        get
        {
            var partes = new[] { Localidad, Provincia }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return partes.Count == 0 ? null : string.Join(", ", partes);
        }
    }
}

/// <summary>
/// HU24 (ruta óptima): convierte una dirección escrita ("Av. Colón 1200") en coordenadas,
/// con el servicio gratuito de OpenStreetMap (Nominatim), el mismo de la HU23.
/// Nominatim pide no hacer más de un pedido por segundo: la clase espera sola entre pedidos
/// y recuerda las direcciones que ya buscó para no repetirlas.
/// </summary>
public static class Geocodificador
{
    private static readonly TimeSpan EntrePedidos = TimeSpan.FromSeconds(1.1);
    private static readonly SemaphoreSlim _turno = new(1, 1);
    private static DateTime _ultimoPedido = DateTime.MinValue;

    /// <summary>Medio lado de la "caja" de búsqueda alrededor del viaje, en grados (unos 50 km).</summary>
    private const double RadioCaja = 0.5;

    // Pedidos ya hechos (también los que no encontraron nada, como null)
    private static readonly Dictionary<string, LugarEncontrado?> _memoria = new(StringComparer.OrdinalIgnoreCase);

    // Campos de la respuesta que sirven como localidad, del más común al menos común
    private static readonly string[] CamposLocalidad = { "city", "town", "village", "municipality" };

    /// <summary>
    /// Dirección del pedido a Nominatim. Si se conoce un punto de la zona, se buscan resultados
    /// alrededor; con <paramref name="acotado"/> = true, solo dentro de esa zona.
    /// </summary>
    public static Uri ArmarUrl(string texto, Coordenada? cerca, bool acotado = false)
    {
        string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=ar&accept-language=es&addressdetails=1"
                     + "&q=" + Uri.EscapeDataString(texto);

        if (cerca is Coordenada c)
        {
            var inv = CultureInfo.InvariantCulture;
            url += string.Create(inv, $"&viewbox={c.Longitud - RadioCaja:0.####},{c.Latitud + RadioCaja:0.####},{c.Longitud + RadioCaja:0.####},{c.Latitud - RadioCaja:0.####}");
            url += acotado ? "&bounded=1" : "&bounded=0";
        }
        return new Uri(url);
    }

    /// <summary>Saca el lugar de la respuesta de Nominatim, o null si no encontró nada.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static LugarEncontrado? LeerLugar(string json)
    {
        using var documento = JsonDocument.Parse(json);
        if (documento.RootElement.ValueKind != JsonValueKind.Array || documento.RootElement.GetArrayLength() == 0)
            return null;

        var primero = documento.RootElement[0];
        if (!(primero.TryGetProperty("lat", out var lat) && primero.TryGetProperty("lon", out var lon)
              && double.TryParse(lat.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud)
              && double.TryParse(lon.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud)))
            return null;

        string? localidad = null, provincia = null;
        if (primero.TryGetProperty("address", out var direccion) && direccion.ValueKind == JsonValueKind.Object)
        {
            localidad = CamposLocalidad.Select(campo => Texto(direccion, campo)).FirstOrDefault(v => v is not null);
            provincia = Texto(direccion, "state");
        }
        return new LugarEncontrado(new Coordenada(latitud, longitud), localidad, provincia);
    }

    /// <summary>Saca la coordenada de la respuesta de Nominatim, o null si no encontró nada.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static Coordenada? LeerRespuesta(string json) => LeerLugar(json)?.Punto;

    private static string? Texto(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(valor.GetString())
            ? valor.GetString()!.Trim()
            : null;

    /// <summary>
    /// Busca una dirección escrita. Primero tal cual la escribió la persona (dentro de la zona del viaje,
    /// si se conoce): así "Av. Goycoechea 250" se encuentra aunque quede en otra localidad que el origen.
    /// Si no aparece, se prueba agregándole la zona de respaldo (por ejemplo, la provincia).
    /// Devuelve null si no la encuentra.
    /// </summary>
    /// <param name="cerca">Un punto de la zona del viaje: se busca solo a unos 50 km de ahí.</param>
    /// <param name="zonaRespaldo">Se agrega al texto si no se encontró solo (por ejemplo "Córdoba").</param>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<LugarEncontrado?> BuscarLugarAsync(string? texto, Coordenada? cerca = null, string? zonaRespaldo = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        string solo = texto.Trim();
        string? conZona = string.IsNullOrWhiteSpace(zonaRespaldo) || solo.Contains(zonaRespaldo.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{solo}, {zonaRespaldo.Trim()}";
        bool acotado = cerca is not null;

        // Sin un punto de referencia, el texto solo podría aparecer en cualquier lugar del país:
        // en ese caso conviene probar primero con la zona
        var intentos = acotado ? new[] { solo, conZona } : new[] { conZona, solo };
        foreach (string? consulta in intentos)
        {
            if (consulta is not null && await PedirAsync(consulta, cerca, acotado) is LugarEncontrado lugar)
                return lugar;
        }
        return null;
    }

    /// <summary>Como <see cref="BuscarLugarAsync"/>, pero devuelve solo las coordenadas.</summary>
    /// <param name="zona">La localidad, para no confundir calles de otras ciudades.</param>
    /// <param name="cerca">Un punto conocido de la zona, para preferir resultados cercanos.</param>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<Coordenada?> BuscarAsync(string? texto, string? zona = null, Coordenada? cerca = null) =>
        (await BuscarLugarAsync(texto, cerca, zona))?.Punto;

    /// <summary>Un pedido a Nominatim (o la respuesta que ya se tenía guardada).</summary>
    private static async Task<LugarEncontrado?> PedirAsync(string consulta, Coordenada? cerca, bool acotado)
    {
        Uri url = ArmarUrl(consulta, cerca, acotado);
        string clave = url.Query;

        lock (_memoria)
        {
            if (_memoria.TryGetValue(clave, out var guardado))
                return guardado;
        }

        await _turno.WaitAsync();
        try
        {
            // Respetar el límite de un pedido por segundo
            var espera = _ultimoPedido + EntrePedidos - DateTime.UtcNow;
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera);

            using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
            pedido.Headers.UserAgent.ParseAdd(BuscadorDeBarrio.UserAgent);

            using var respuesta = await ClienteHttp.EnviarAsync(pedido);
            _ultimoPedido = DateTime.UtcNow;
            if (!respuesta.IsSuccessStatusCode)
                throw new ServidorException($"OpenStreetMap respondió con código {(int)respuesta.StatusCode}");

            LugarEncontrado? resultado;
            try
            {
                resultado = LeerLugar(await respuesta.Content.ReadAsStringAsync());
            }
            catch (JsonException ex)
            {
                RegistroErrores.RegistrarError(ex, "Leer respuesta de OpenStreetMap (buscar dirección)");
                throw new ServidorException("OpenStreetMap devolvió una respuesta inválida");
            }

            lock (_memoria)
                _memoria[clave] = resultado;
            return resultado;
        }
        finally
        {
            _turno.Release();
        }
    }
}
