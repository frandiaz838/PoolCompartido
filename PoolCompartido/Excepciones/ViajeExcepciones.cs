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

/// <summary>HU4: el pasajero intenta salirse de un viaje al que no está anotado.</summary>
public class NoAnotadoException : ViajeException
{
    public NoAnotadoException()
        : base("No estás anotado en este viaje.") { }
}

/// <summary>El viaje ya salió: no se puede modificar.</summary>
public class ViajeVencidoException : ViajeException
{
    public ViajeVencidoException()
        : base("Este viaje ya salió.") { }
}

/// <summary>Faltan menos de 5 minutos para salir: ya no se aceptan pasajeros nuevos.</summary>
public class AnotacionesCerradasException : ViajeException
{
    public AnotacionesCerradasException()
        : base("Este viaje sale en menos de 5 minutos y ya no acepta pasajeros.") { }
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

/// <summary>
/// Otra persona modificó el viaje al mismo tiempo (por ejemplo, dos se anotan al último lugar)
/// y no se pudo guardar después de varios intentos.
/// </summary>
public class ConflictoException : ViajeException
{
    public ConflictoException()
        : base("El viaje cambió mientras hacías la operación. Probá de nuevo.") { }
}
