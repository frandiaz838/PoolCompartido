using System.Globalization;
using System.Text.Json;

namespace PoolCompartido;

/// <summary>HU23: posición actual del dispositivo y su barrio.</summary>
public record UbicacionActual(double Latitud, double Longitud, string Barrio);

/// <summary>
/// HU23: convierte una posición (latitud y longitud) en un nombre para mostrar,
/// usando el servicio gratuito de OpenStreetMap (Nominatim), que no necesita clave.
/// Si la posición es un lugar público conocido (una facultad, un shopping, una plaza),
/// se muestra ese nombre; si no, solo el barrio o la localidad.
/// Nunca se muestra una dirección exacta: así una casa no queda expuesta.
/// </summary>
public static class BuscadorDeBarrio
{
    /// <summary>Nominatim exige que cada app se identifique con un User-Agent propio.</summary>
    internal const string UserAgent = "PoolCompartido/1.0 (proyecto universitario)";

    /// <summary>
    /// Campos de la dirección que sirven como "barrio", del más chico al más grande.
    /// Se usa el primero que venga en la respuesta.
    /// </summary>
    private static readonly string[] Campos =
        { "suburb", "neighbourhood", "quarter", "city_district", "town", "village", "city" };

    /// <summary>
    /// Lugares públicos (categoría:tipo de OpenStreetMap) cuyo nombre se puede mostrar.
    /// Es una lista cerrada a propósito: una casa, un edificio de departamentos o un comercio
    /// chico nunca entran, así el nombre no delata dónde vive alguien.
    /// </summary>
    private static readonly HashSet<string> LugaresPublicos = new(StringComparer.OrdinalIgnoreCase)
    {
        "amenity:university", "amenity:college", "amenity:school", "amenity:hospital",
        "amenity:library", "amenity:bus_station", "amenity:townhall",
        "building:university", "building:college", "building:school", "building:hospital",
        "building:train_station",
        "shop:mall", "leisure:park", "leisure:stadium",
        "railway:station", "aeroway:aerodrome", "tourism:museum"
    };

    /// <summary>
    /// Dirección del pedido a Nominatim. Zoom 18 = el lugar más cercano (por ejemplo, la facultad);
    /// la respuesta trae igual el barrio dentro de "address".
    /// </summary>
    public static Uri ArmarUrl(double latitud, double longitud)
    {
        // CultureInfo.InvariantCulture: punto decimal siempre, aunque la compu esté en español
        string lat = latitud.ToString("0.######", CultureInfo.InvariantCulture);
        string lon = longitud.ToString("0.######", CultureInfo.InvariantCulture);
        return new Uri($"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=18&accept-language=es");
    }

    /// <summary>
    /// Saca de la respuesta de Nominatim el nombre para mostrar: el del lugar público si la
    /// posición es uno, o si no el del barrio. Devuelve null si no viene ninguno.
    /// </summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static string? ElegirNombre(string json)
    {
        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;

        // ¿Es un lugar público conocido? (por ejemplo, "amenity:university")
        if (Texto(raiz, "category") is string categoria
            && Texto(raiz, "type") is string tipo
            && LugaresPublicos.Contains($"{categoria}:{tipo}")
            && Texto(raiz, "name") is string lugar)
            return lugar;

        if (!raiz.TryGetProperty("address", out var direccion))
            return null;

        foreach (string campo in Campos)
        {
            if (direccion.TryGetProperty(campo, out var valor)
                && valor.GetString() is string nombre
                && !string.IsNullOrWhiteSpace(nombre))
                return nombre.Trim();
        }
        return null;
    }

    /// <summary>Valor de texto de una propiedad, o null si no existe o está vacío.</summary>
    private static string? Texto(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(valor.GetString())
            ? valor.GetString()!.Trim()
            : null;

    /// <summary>
    /// Busca el lugar público o el barrio de esa posición. Primero en Nominatim; si no se puede llegar a Nominatim
    /// o responde con error, se prueba con Photon (el mismo respaldo que usa el Geocodificador para las direcciones).
    /// </summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    /// <exception cref="UbicacionNoDisponibleException">No se encontró un barrio para esa posición.</exception>
    public static async Task<string> BuscarAsync(double latitud, double longitud)
    {
        try
        {
            return await BuscarEnNominatimAsync(latitud, longitud);
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            RegistroErrores.RegistrarAviso(ex, "Buscar barrio en Nominatim (se prueba con Photon)");
            // Si Photon tampoco responde, se informa el error original
            return await BuscarEnPhotonAsync(latitud, longitud) ?? throw ex;
        }
    }

    /// <summary>Dirección del pedido a Photon para una posición (búsqueda inversa).</summary>
    public static Uri ArmarUrlPhoton(double latitud, double longitud)
    {
        string lat = latitud.ToString("0.######", CultureInfo.InvariantCulture);
        string lon = longitud.ToString("0.######", CultureInfo.InvariantCulture);
        return new Uri($"https://photon.komoot.io/reverse?lat={lat}&lon={lon}&limit=1");
    }

    /// <summary>
    /// Saca de la respuesta de Photon el nombre para mostrar, con el mismo criterio que con Nominatim:
    /// el lugar público si lo es, o si no el barrio o la localidad. Devuelve null si no viene ninguno.
    /// </summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static string? ElegirNombrePhoton(string json)
    {
        using var documento = JsonDocument.Parse(json);
        if (!documento.RootElement.TryGetProperty("features", out var lugares)
            || lugares.ValueKind != JsonValueKind.Array || lugares.GetArrayLength() == 0
            || !lugares[0].TryGetProperty("properties", out var datos))
            return null;

        if (Texto(datos, "osm_key") is string categoria
            && Texto(datos, "osm_value") is string tipo
            && LugaresPublicos.Contains($"{categoria}:{tipo}")
            && Texto(datos, "name") is string lugar)
            return lugar;

        // Barrio, después la localidad (nunca la calle ni el número)
        return Texto(datos, "district") ?? Texto(datos, "locality") ?? Texto(datos, "city");
    }

    /// <summary>Respaldo con Photon: devuelve null si tampoco se pudo (sin internet, error o sin barrio).</summary>
    private static async Task<string?> BuscarEnPhotonAsync(double latitud, double longitud)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, ArmarUrlPhoton(latitud, longitud));
            pedido.Headers.UserAgent.ParseAdd(UserAgent);
            using var respuesta = await ClienteHttp.EnviarAsync(pedido);
            if (!respuesta.IsSuccessStatusCode) return null;
            return ElegirNombrePhoton(await respuesta.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is SinConexionException or JsonException)
        {
            RegistroErrores.RegistrarAviso(ex, "Buscar barrio en Photon");
            return null;
        }
    }

    private static async Task<string> BuscarEnNominatimAsync(double latitud, double longitud)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, ArmarUrl(latitud, longitud));
        pedido.Headers.UserAgent.ParseAdd(UserAgent);

        using var respuesta = await ClienteHttp.EnviarAsync(pedido);
        if (!respuesta.IsSuccessStatusCode)
            throw new ServidorException($"OpenStreetMap respondió con código {(int)respuesta.StatusCode}");

        string json = await respuesta.Content.ReadAsStringAsync();
        try
        {
            return ElegirNombre(json)
                   ?? throw new UbicacionNoDisponibleException("No encontramos el barrio de tu ubicación. Escribilo a mano.");
        }
        catch (JsonException ex)
        {
            RegistroErrores.RegistrarError(ex, "Leer respuesta de OpenStreetMap");
            throw new ServidorException("OpenStreetMap devolvió una respuesta inválida");
        }
    }
}
