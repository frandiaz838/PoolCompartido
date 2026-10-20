namespace PoolCompartido;

/// <summary>
/// No se pudo hablar con el servidor (sin internet o tardó demasiado).
/// Envuelve la excepción original (HttpRequestException o TaskCanceledException) en InnerException.
/// </summary>
public class SinConexionException : Exception
{
    public SinConexionException(Exception causa)
        : base("No hay conexión con el servidor. Revisá tu internet y probá de nuevo.", causa) { }
}

/// <summary>
/// El servidor rechazó la operación por falta de permisos
/// (por ejemplo, si las reglas de seguridad de Firebase no lo permiten).
/// </summary>
public class PermisoDenegadoException : Exception
{
    public PermisoDenegadoException()
        : base("El servidor rechazó la operación por falta de permisos.") { }
}

/// <summary>El servidor respondió con un error inesperado.</summary>
public class ServidorException : Exception
{
    public ServidorException(string detalle)
        : base($"El servidor respondió con un error ({detalle}). Probá de nuevo más tarde.") { }
}
