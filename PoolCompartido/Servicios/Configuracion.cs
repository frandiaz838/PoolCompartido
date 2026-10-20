namespace PoolCompartido;

/// <summary>Configuración general de la app.</summary>
public static class Configuracion
{
    /// <summary>
    /// Carpeta donde se guardan los archivos (viajes y registro de errores).
    /// Al arrancar, App.xaml.cs la cambia por la carpeta de datos propia de cada dispositivo.
    /// </summary>
    public static string CarpetaDatos { get; set; } = AppContext.BaseDirectory;
}
