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

        // HU24: si el conductor indicó un punto de partida, la ruta arranca ahí
        string origen = viaje.TienePuntoPartida ? $"{viaje.PuntoPartida}, {viaje.Origen}" : viaje.Origen;
        await AbrirAsync(ArmarUrlRuta(origen, viaje.Destino));
    }

    /// <summary>HU24: arma la dirección web de Google Maps que muestra un lugar.</summary>
    /// <param name="lugar">El punto de encuentro, por ejemplo "Av. Colón y General Paz".</param>
    /// <param name="zona">La localidad, para ubicarlo mejor (por ejemplo, el origen del viaje).</param>
    /// <exception cref="ArgumentException">No se indicó el lugar.</exception>
    public static Uri ArmarUrlLugar(string? lugar, string? zona)
    {
        if (string.IsNullOrWhiteSpace(lugar))
            throw new ArgumentException("No hay un lugar para mostrar en el mapa.");

        string texto = string.IsNullOrWhiteSpace(zona) ? lugar.Trim() : $"{lugar.Trim()}, {zona.Trim()}";
        return new Uri($"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString($"{texto}, {Pais}")}");
    }

    /// <summary>HU24: abre Google Maps en el punto de encuentro de un pasajero.</summary>
    /// <exception cref="ArgumentException">No se indicó el lugar.</exception>
    /// <exception cref="MapaNoDisponibleException">El dispositivo no pudo abrir el mapa.</exception>
    public static Task AbrirLugarAsync(string? lugar, string? zona) => AbrirAsync(ArmarUrlLugar(lugar, zona));

    /// <summary>HU23: dirección web de Google Maps que muestra un punto exacto.</summary>
    public static Uri ArmarUrlCoordenadas(double latitud, double longitud)
    {
        string lat = latitud.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        string lon = longitud.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return new Uri($"https://www.google.com/maps/search/?api=1&query={lat},{lon}");
    }

    /// <summary>HU23: abre Google Maps en la ubicación exacta que compartió un pasajero.</summary>
    /// <exception cref="MapaNoDisponibleException">El dispositivo no pudo abrir el mapa.</exception>
    public static Task AbrirCoordenadasAsync(double latitud, double longitud) =>
        AbrirAsync(ArmarUrlCoordenadas(latitud, longitud));

    private static async Task AbrirAsync(Uri url)
    {
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
