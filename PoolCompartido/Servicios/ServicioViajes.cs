namespace PoolCompartido;

/// <summary>
/// Lógica del negocio: publicar viajes, anotarse, etc.
/// No sabe nada de pantallas: valida, lanza excepciones si algo está mal
/// y dispara eventos para que las pantallas reaccionen.
/// </summary>
public class ServicioViajes
{
    /// <summary>Única instancia compartida por toda la app.</summary>
    public static ServicioViajes Instancia { get; } = new();

    /// <summary>Usuario que está usando la app (hasta que haya login).</summary>
    public const string UsuarioActual = "Vos";

    public static readonly string[] Horarios = { "07:00", "08:00", "09:00", "13:00", "18:00", "19:00" };

    /// <summary>Bucle despachador con la tabla de eventos de la app.</summary>
    public Despachador<ViajeEvento, Viaje> Eventos { get; } = new();

    private readonly List<Viaje> _viajes = new();
    public IReadOnlyList<Viaje> Viajes => _viajes;

    private ServicioViajes()
    {
        // Datos de ejemplo para la demo
        _viajes.Add(new Viaje { Origen = "Palermo", Destino = "Facultad", Fecha = DateTime.Today.AddDays(1), Hora = "08:00", Conductor = "Male", AsientosDisponibles = 3 });
        _viajes.Add(new Viaje { Origen = "Caballito", Destino = "Centro", Fecha = DateTime.Today.AddDays(1), Hora = "09:00", Conductor = "Juan", AsientosDisponibles = 1 });
        _viajes.Add(new Viaje { Origen = "Belgrano", Destino = "Facultad", Fecha = DateTime.Today.AddDays(2), Hora = "18:00", Conductor = "Sofi", AsientosDisponibles = 0 });
    }

    /// <summary>HU1: el usuario actual publica un viaje como conductor.</summary>
    /// <exception cref="ArgumentException">Faltan datos obligatorios.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fecha pasada o asientos fuera de rango.</exception>
    public Viaje Publicar(string? origen, string? destino, DateTime fecha, string? hora, int asientos, string? comentario)
    {
        // Validación temprana de la entrada
        if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            throw new ArgumentException("Completá el origen y el destino.");

        if (string.IsNullOrWhiteSpace(hora))
            throw new ArgumentException("Elegí un horario de salida.");

        // innerException: null elige el constructor que deja el mensaje limpio
        if (fecha.Date < DateTime.Today)
            throw new ArgumentOutOfRangeException("La fecha del viaje no puede ser anterior a hoy.", innerException: null);

        if (asientos < 1 || asientos > 4)
            throw new ArgumentOutOfRangeException("Elegí entre 1 y 4 asientos disponibles.", innerException: null);

        var viaje = new Viaje
        {
            Origen = origen.Trim(),
            Destino = destino.Trim(),
            Fecha = fecha.Date,
            Hora = hora,
            Conductor = UsuarioActual,
            Comentario = comentario?.Trim() ?? "",
            AsientosDisponibles = asientos
        };

        _viajes.Add(viaje);
        Eventos.Disparar(ViajeEvento.VIAJE_PUBLICADO, viaje);
        return viaje;
    }

    /// <summary>HU3: el usuario actual se anota como pasajero.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ViajeException">El estado del viaje no permite anotarse.</exception>
    public void Anotarse(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);

        if (viaje.Conductor == UsuarioActual) throw new AutoReservaException();
        if (viaje.Pasajeros.Contains(UsuarioActual)) throw new YaAnotadoException();
        if (viaje.Fecha.Date < DateTime.Today) throw new ViajeVencidoException();
        if (viaje.AsientosDisponibles <= 0) throw new ViajeCompletoException();

        viaje.AgregarPasajero(UsuarioActual);
        viaje.AsientosDisponibles--;
        Eventos.Disparar(ViajeEvento.ASIENTO_RESERVADO, viaje);
    }

    /// <summary>HU8: viajes en los que participa el usuario actual (como conductor o pasajero), por fecha.</summary>
    public IEnumerable<Viaje> MisViajes() =>
        _viajes.Where(v => v.EsDelUsuarioActual || v.EstoyAnotado)
               .OrderBy(v => v.Fecha)
               .ThenBy(v => v.Hora);
}
