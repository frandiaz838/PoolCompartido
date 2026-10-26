using System.Globalization;
using System.Text.Json;

namespace PoolCompartido;

/// <summary>HU23: posición actual del dispositivo y su barrio.</summary>
public record UbicacionActual(double Latitud, double Longitud, string Barrio);

/// <summary>
/// HU23: convierte una posición (latitud y longitud) en el nombre del barrio o la localidad,
/// usando el servicio gratuito de OpenStreetMap (Nominatim), que no necesita clave.
/// Así en la parte pública se muestra "Villa Allende" y nunca la dirección exacta.
/// </summary>
public static class BuscadorDeBarrio
{
    /// <summary>Nominatim exige que cada app se identifique con un User-Agent propio.</summary>
    private const string UserAgent = "PoolCompartido/1.0 (proyecto universitario)";

    /// <summary>
    /// Campos de la dirección que sirven como "barrio", del más chico al más grande.
    /// Se usa el primero que venga en la respuesta.
    /// </summary>
    private static readonly string[] Campos =
        { "suburb", "neighbourhood", "quarter", "city_district", "town", "village", "city" };

    /// <summary>Dirección del pedido a Nominatim (zoom 14 = nivel barrio).</summary>
    public static Uri ArmarUrl(double latitud, double longitud)
    {
        // CultureInfo.InvariantCulture: punto decimal siempre, aunque la compu esté en español
        string lat = latitud.ToString("0.######", CultureInfo.InvariantCulture);
        string lon = longitud.ToString("0.######", CultureInfo.InvariantCulture);
        return new Uri($"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=14&accept-language=es");
    }

    /// <summary>Saca el nombre del barrio de la respuesta de Nominatim, o null si no viene.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static string? ElegirNombre(string json)
    {
        using var documento = JsonDocument.Parse(json);
        if (!documento.RootElement.TryGetProperty("address", out var direccion))
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

    /// <summary>Busca el barrio de esa posición.</summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    /// <exception cref="UbicacionNoDisponibleException">No se encontró un barrio para esa posición.</exception>
    public static async Task<string> BuscarAsync(double latitud, double longitud)
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
