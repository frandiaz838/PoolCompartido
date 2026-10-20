namespace PoolCompartido;

/// <summary>
/// Registro de excepciones en un archivo de texto (buena práctica 7: registrar para analizar después).
/// AVISO = error del usuario (dato inválido, viaje completo...).
/// ERROR = falla del sistema (no se pudo guardar, error no controlado...).
/// </summary>
public static class RegistroErrores
{
    private static readonly object _candado = new();

    public static string RutaArchivo => Path.Combine(Configuracion.CarpetaDatos, "errores.log");

    public static void RegistrarAviso(Exception ex, string dondeOcurrio) => Escribir("AVISO", ex, dondeOcurrio);

    public static void RegistrarError(Exception ex, string dondeOcurrio) => Escribir("ERROR", ex, dondeOcurrio);

    private static void Escribir(string nivel, Exception ex, string dondeOcurrio)
    {
        string linea = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {nivel} en {dondeOcurrio}: {ex.GetType().Name} - {ex.Message}";
        if (ex.InnerException != null)
            linea += $" | Causa: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}";

        // lock: el controlador global puede llamar desde otro hilo
        lock (_candado)
        {
            try
            {
                // using: el archivo se cierra siempre, aunque falle la escritura
                using (var escritor = new StreamWriter(RutaArchivo, append: true))
                {
                    escritor.WriteLine(linea);
                }
            }
            catch (Exception errorDelLog) when (errorDelLog is IOException or UnauthorizedAccessException)
            {
                // No relanzamos: que falle el registro no puede romper la app.
                // Tampoco lo silenciamos: queda en la ventana de Salida de Visual Studio.
                System.Diagnostics.Debug.WriteLine($"No se pudo escribir el registro ({errorDelLog.Message}). Error original: {linea}");
            }
        }
    }

    /// <summary>Devuelve todo el registro.</summary>
    /// <exception cref="FileNotFoundException">Todavía no se registró nada.</exception>
    /// <exception cref="IOException">No se pudo leer el archivo.</exception>
    public static string LeerTodo()
    {
        lock (_candado)
        {
            using (var lector = new StreamReader(RutaArchivo))
            {
                return lector.ReadToEnd();
            }
        }
    }
}
