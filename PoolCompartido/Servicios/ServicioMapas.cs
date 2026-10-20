namespace PoolCompartido;

/// <summary>
/// HU22: abre el recorrido de un viaje en Google Maps.
/// No usa clave de API: arma una dirección web de Google Maps y la abre con el sistema
/// (en Android abre la app de Maps si está instalada; en Windows, el navegador).
/// </summary>
public static class ServicioMapas
{
    /// <summary>Se agrega al origen y al destino para que "Palermo" no termine en Italia.</summary>
    private const string Pais = "Argentina";

    /// <summary>Arma la dirección web de Google Maps con la ruta en auto de origen a destino.</summary>
    /// <exception cref="ArgumentException">Falta el origen o el destino.</exception>
    public static Uri ArmarUrlRuta(string? origen, string? destino)
    {
        // Validación temprana
        if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            throw new ArgumentException("El viaje no tiene origen o destino, no se puede mostrar la ruta.");

        // EscapeDataString convierte espacios, tildes y comas para que la dirección sea válida
        string desde = Uri.EscapeDataString($"{origen.Trim()}, {Pais}");
        string hasta = Uri.EscapeDataString($"{destino.Trim()}, {Pais}");

        return new Uri($"https://www.google.com/maps/dir/?api=1&origin={desde}&destination={hasta}&travelmode=driving");
    }

    /// <summary>Abre Google Maps con la ruta del viaje.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ArgumentException">Falta el origen o el destino.</exception>
    /// <exception cref="MapaNoDisponibleException">El dispositivo no pudo abrir el mapa.</exception>
    public static async Task AbrirRutaAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        Uri url = ArmarUrlRuta(viaje.Origen, viaje.Destino);

        bool seAbrio;
        try
        {
            seAbrio = await Launcher.Default.OpenAsync(url);
        }
        catch (FeatureNotSupportedException ex)
        {
            throw new MapaNoDisponibleException(ex);
        }

        if (!seAbrio)
            throw new MapaNoDisponibleException(null);
    }
}
