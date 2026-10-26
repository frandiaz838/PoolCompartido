namespace PoolCompartido;

/// <summary>
/// HU23: no se pudo obtener la ubicación del dispositivo o el barrio.
/// Envuelve las excepciones de MAUI (PermissionException, FeatureNotEnabledException,
/// FeatureNotSupportedException) con un mensaje claro para el usuario;
/// la excepción original queda en InnerException para el registro de errores.
/// </summary>
public class UbicacionNoDisponibleException : Exception
{
    public UbicacionNoDisponibleException(string mensaje, Exception? causa = null)
        : base(mensaje, causa) { }
}
