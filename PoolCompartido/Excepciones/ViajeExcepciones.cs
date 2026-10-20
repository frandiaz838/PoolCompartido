namespace PoolCompartido;

/// <summary>
/// Excepción base de la app: el estado del viaje no permite la operación pedida.
/// Hereda de InvalidOperationException (librería estándar de .NET).
/// </summary>
public class ViajeException : InvalidOperationException
{
    public ViajeException(string mensaje) : base(mensaje) { }
}

/// <summary>El conductor intenta anotarse como pasajero en su propio viaje.</summary>
public class AutoReservaException : ViajeException
{
    public AutoReservaException()
        : base("No podés anotarte como pasajero en un viaje que publicaste vos.") { }
}

/// <summary>El pasajero ya estaba anotado en ese viaje.</summary>
public class YaAnotadoException : ViajeException
{
    public YaAnotadoException()
        : base("Ya estás anotado en este viaje.") { }
}

/// <summary>El viaje ya pasó.</summary>
public class ViajeVencidoException : ViajeException
{
    public ViajeVencidoException()
        : base("Este viaje ya pasó su fecha de salida.") { }
}

/// <summary>
/// El cambio se hizo, pero no se pudo guardar en el archivo.
/// Envuelve la excepción original (IOException, etc.) en InnerException.
/// </summary>
public class PersistenciaException : Exception
{
    public PersistenciaException(string mensaje, Exception causa) : base(mensaje, causa) { }
}

/// <summary>El viaje fue cancelado por el conductor.</summary>
public class ViajeCanceladoException : ViajeException
{
    public ViajeCanceladoException()
        : base("Este viaje fue cancelado por el conductor.") { }
}

/// <summary>Alguien que no es el conductor intenta cancelar el viaje.</summary>
public class NoEsConductorException : ViajeException
{
    public NoEsConductorException()
        : base("Solo el conductor puede cancelar el viaje.") { }
}

/// <summary>No quedan asientos libres.</summary>
public class ViajeCompletoException : ViajeException
{
    public ViajeCompletoException()
        : base("Este viaje ya no tiene asientos disponibles.") { }
}
