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

    /// <summary>Para otras acciones que solo puede hacer el conductor.</summary>
    public NoEsConductorException(string mensaje)
        : base(mensaje) { }
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

/// <summary>
/// El usuario ya está en otro viaje (como conductor o pasajero) cerca de ese horario:
/// no puede estar en dos viajes al mismo tiempo.
/// </summary>
public class HorarioOcupadoException : ViajeException
{
    /// <summary>El viaje que ya tiene a esa hora.</summary>
    public Viaje Otro { get; }

    public HorarioOcupadoException(Viaje otro)
        : base($"Ya tenés otro viaje a esa hora: {otro.Ruta} el {otro.Fecha:dd/MM} a las {otro.Hora}. No podés estar en dos viajes al mismo tiempo.")
    {
        Otro = otro;
    }
}

/// <summary>HU15: el PIN que escribió el conductor no es el de ningún pasajero que falte subir.</summary>
public class PinInvalidoException : ViajeException
{
    public PinInvalidoException()
        : base("Ese PIN no es el de ningún pasajero que falte subir. Pedile que lo mire de nuevo en Mis viajes.") { }

    /// <summary>Para otros problemas con el PIN (formato, o un pasajero que no tiene).</summary>
    public PinInvalidoException(string mensaje)
        : base(mensaje) { }
}

/// <summary>HU15: el conductor quiere iniciar el viaje más de 15 minutos antes de la salida.</summary>
public class InicioAnticipadoException : ViajeException
{
    public InicioAnticipadoException(DateTime desde)
        : base($"Todavía es temprano: podés iniciar el viaje desde las {desde:HH:mm}.") { }
}

/// <summary>HU15: la acción necesita que el conductor haya tocado "Iniciar viaje".</summary>
public class ViajeNoIniciadoException : ViajeException
{
    public ViajeNoIniciadoException()
        : base("Primero tocá \"Iniciar viaje\".") { }
}

/// <summary>HU31: ya calificó a esa persona en ese viaje (se califica una sola vez).</summary>
public class YaCalificasteException : ViajeException
{
    public YaCalificasteException(string nombre)
        : base($"Ya calificaste a {nombre} en este viaje.") { }
}

/// <summary>HU31: se quiere calificar un viaje que el conductor todavía no finalizó.</summary>
public class ViajeNoFinalizadoException : ViajeException
{
    public ViajeNoFinalizadoException()
        : base("Podés calificar cuando el conductor finalice el viaje.") { }
}

/// <summary>HU31: solo califican (y son calificados) el conductor y los pasajeros que estuvieron a bordo.</summary>
public class NoViajasteException : ViajeException
{
    public NoViajasteException()
        : base("Solo pueden calificar el conductor y los pasajeros que viajaron (los que hicieron check-in).") { }

    /// <summary>Para cuando la que no viajó es la otra persona.</summary>
    public NoViajasteException(string mensaje)
        : base(mensaje) { }
}
