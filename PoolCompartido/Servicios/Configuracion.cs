namespace PoolCompartido;

/// <summary>Configuración general de la app.</summary>
public static class Configuracion
{
    /// <summary>
    /// Carpeta donde se guardan los archivos locales (sesión, caché de viajes y registro de errores).
    /// Al arrancar, App.xaml.cs la cambia por la carpeta de datos propia de cada dispositivo.
    /// </summary>
    public static string CarpetaDatos { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// Zona donde se usa la app (Córdoba capital, donde está la UBP). Cuando se busca un lugar sin saber
    /// dónde queda el viaje, se prefieren resultados cerca de acá: "Universidad Blas Pascal" o "UTN"
    /// tienen sedes en otras ciudades, y sin esto OpenStreetMap a veces elegía las de Buenos Aires.
    /// </summary>
    public static Coordenada ZonaPorDefecto { get; set; } = new(-31.4201, -64.1888);

    // Datos del proyecto de Firebase. Están en ClavesFirebase.cs, que NO se sube a GitHub.
    public static string FirebaseApiKey { get; set; } = ClavesFirebase.ApiKey;
    public static string FirebaseUrlBase { get; set; } = ClavesFirebase.UrlBase;

    /// <summary>
    /// HU16: cuánto espera el conductor, desde que inicia el viaje, a que cada pasajero haga check-in.
    /// Pasado ese tiempo, se lo saca del viaje y se libera su lugar. Para la demo: TimeSpan.FromMinutes(1).
    /// </summary>
    public static TimeSpan EsperaNoShow { get; set; } = TimeSpan.FromMinutes(10);
}
