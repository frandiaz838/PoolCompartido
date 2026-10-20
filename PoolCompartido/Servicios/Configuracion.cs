namespace PoolCompartido;

/// <summary>Configuración general de la app.</summary>
public static class Configuracion
{
    /// <summary>
    /// Carpeta donde se guardan los archivos locales (sesión, caché de viajes y registro de errores).
    /// Al arrancar, App.xaml.cs la cambia por la carpeta de datos propia de cada dispositivo.
    /// </summary>
    public static string CarpetaDatos { get; set; } = AppContext.BaseDirectory;

    // Datos del proyecto de Firebase. Están en ClavesFirebase.cs, que NO se sube a GitHub.
    public static string FirebaseApiKey { get; set; } = ClavesFirebase.ApiKey;
    public static string FirebaseUrlBase { get; set; } = ClavesFirebase.UrlBase;
}
