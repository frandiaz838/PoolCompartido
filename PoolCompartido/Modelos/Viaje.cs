using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PoolCompartido;

/// <summary>En qué momento está un viaje según la hora actual.</summary>
public enum EstadoViaje
{
    /// <summary>Todavía acepta pasajeros.</summary>
    Abierto,
    /// <summary>Faltan menos de 5 minutos para salir: ya no se anota nadie nuevo.</summary>
    PorSalir,
    /// <summary>Ya salió.</summary>
    EnCurso,
    /// <summary>El conductor lo canceló.</summary>
    Cancelado,
    /// <summary>Terminó: deja de mostrarse en la app (queda en Firebase como historial).</summary>
    Finalizado
}

/// <summary>
/// Un viaje publicado por un conductor.
/// Implementa INotifyPropertyChanged: cuando cambia un dato, la pantalla se redibuja sola.
/// </summary>
public class Viaje : INotifyPropertyChanged
{
    /// <summary>Identificador del viaje en Firebase.</summary>
    public string Id { get; init; } = "";
    public string Origen { get; init; } = "";
    public string Destino { get; init; } = "";
    public DateTime Fecha { get; init; }
    public string Hora { get; init; } = "";
    public string ConductorUid { get; init; } = "";
    /// <summary>Nombre del conductor (el que se muestra).</summary>
    public string Conductor { get; init; } = "";
    public string Comentario { get; init; } = "";
    /// <summary>HU14: costo total del viaje (nafta + peajes), o null si es sin costo. Se divide solo entre los pasajeros.</summary>
    public decimal? CostoTotal { get; init; }

    /// <summary>HU24: lugar público desde donde sale el conductor (puede estar vacío).</summary>
    public string PuntoPartida { get; init; } = "";
    public bool TienePuntoPartida => !string.IsNullOrWhiteSpace(PuntoPartida);
    public string PuntoPartidaTexto => $"Sale desde: {PuntoPartida}";

    // ───── HU15: check-in y ciclo de vida ─────

    /// <summary>HU15: el conductor tocó "Iniciar viaje".</summary>
    public bool Iniciado { get; private set; }

    /// <summary>HU15: el conductor tocó "Finalizar viaje".</summary>
    public bool Finalizado { get; private set; }

    // HU15: uid de los pasajeros que ya subieron (confirmados con su PIN)
    private readonly HashSet<string> _aBordo = new();
    public IReadOnlyCollection<string> ABordo => _aBordo;

    // HU15: uid → hash del PIN de cada pasajero (el PIN en sí solo lo ve el pasajero)
    private readonly Dictionary<string, string> _pinHash = new();
    internal IReadOnlyDictionary<string, string> PinHashPorUid => _pinHash;

    /// <summary>HU15: nombre y uid del último pasajero que subió (para el aviso de PASAJERO_A_BORDO).</summary>
    public string? UltimoPasajeroABordo { get; private set; }
    public string? UltimoPasajeroABordoUid { get; private set; }

    // ───── HU16: no-show ─────

    /// <summary>
    /// HU16: cuándo vio esta app que el conductor inició el viaje. No se guarda en Firebase:
    /// si se reabre la app, la espera arranca de nuevo (se espera de más, nunca de menos).
    /// </summary>
    public DateTime? IniciadoEn { get; private set; }

    // HU16: uid → nombre de los pasajeros que se sacaron por no subir a tiempo (los del último cambio)
    private readonly Dictionary<string, string> _noShows = new();
    public IReadOnlyDictionary<string, string> UltimosNoShow => _noShows;

    /// <summary>HU16: "Juan" o "Juan y Ana", para el aviso al conductor.</summary>
    public string NoShowTexto => _noShows.Count <= 1
        ? _noShows.Values.FirstOrDefault() ?? ""
        : string.Join(", ", _noShows.Values.SkipLast(1)) + " y " + _noShows.Values.Last();

    /// <summary>
    /// HU16: soy el conductor, el viaje está iniciado, alguien no subió y ya pasó el tiempo de espera.
    /// Recibe la hora para poder probarlo.
    /// </summary>
    internal bool HayNoShowEn(DateTime ahora) =>
        EsDelUsuarioActual && Iniciado && EstadoEn(ahora) == EstadoViaje.EnCurso && FaltanSubir > 0
        && IniciadoEn is DateTime inicio && ahora >= inicio + Configuracion.EsperaNoShow;

    // Pasajeros anotados: uid → nombre
    private readonly Dictionary<string, string> _pasajeros = new();
    public IReadOnlyDictionary<string, string> PasajerosPorUid => _pasajeros;
    public IReadOnlyCollection<string> Pasajeros => _pasajeros.Values;

    /// <summary>Nombre del último pasajero que se anotó (para la notificación al conductor).</summary>
    public string? UltimoPasajeroAnotado { get; private set; }

    /// <summary>HU4: nombre del último pasajero que se bajó del viaje (para el aviso al conductor de la HU7).</summary>
    public string? UltimoPasajeroQueSalio { get; private set; }

    // HU7: uid de esos mismos pasajeros, para saber si el que se anotó o se bajó soy yo
    // (los nombres se pueden repetir, el uid no)
    public string? UltimoPasajeroAnotadoUid { get; private set; }
    public string? UltimoPasajeroQueSalioUid { get; private set; }

    /// <summary>HU6: si este viaje se canceló, el viaje parecido que se encontró para el pasajero.</summary>
    public Viaje? Reemplazo { get; internal set; }

    private int _asientosDisponibles;
    public int AsientosDisponibles
    {
        get => _asientosDisponibles;
        private set
        {
            if (_asientosDisponibles == value) return;
            _asientosDisponibles = value;
            OnPropertyChanged();
        }
    }

    // HU5: un viaje cancelado deja de estar disponible, pero queda en el historial
    private bool _cancelado;
    public bool Cancelado
    {
        get => _cancelado;
        private set
        {
            if (_cancelado == value) return;
            _cancelado = value;
            OnPropertyChanged();
            AvisarCambioDeEstado();
        }
    }

    // ───── Estado según la hora ─────

    /// <summary>Las anotaciones se cierran 5 minutos antes de salir, para que el conductor se organice.</summary>
    public static readonly TimeSpan CierreAnotaciones = TimeSpan.FromMinutes(5);

    /// <summary>No sabemos cuánto dura cada viaje: se muestra "En curso" durante 2 horas después de salir.</summary>
    public static readonly TimeSpan DuracionEnCurso = TimeSpan.FromHours(2);

    /// <summary>Fecha y hora exacta de salida.</summary>
    public DateTime Salida => FranjasHorarias.TryLeerHora(Hora, out var hora) ? Fecha.Date + hora : Fecha.Date;

    public EstadoViaje Estado => EstadoEn(DateTime.Now);

    /// <summary>Estado del viaje en un momento dado (separado de DateTime.Now para poder probarlo).</summary>
    public EstadoViaje EstadoEn(DateTime ahora)
    {
        // HU15: lo que marcó el conductor manda sobre la hora. Si nunca toca los botones, sigue todo por la hora
        if (Finalizado) return EstadoViaje.Finalizado;
        if (ahora >= Salida + DuracionEnCurso) return EstadoViaje.Finalizado;
        if (Cancelado) return EstadoViaje.Cancelado;
        if (Iniciado || ahora >= Salida) return EstadoViaje.EnCurso;
        if (ahora >= Salida - CierreAnotaciones) return EstadoViaje.PorSalir;
        return EstadoViaje.Abierto;
    }

    /// <summary>Aparece en "Viajes disponibles" solo hasta 5 minutos antes de salir.</summary>
    public bool AceptaPasajeros => Estado == EstadoViaje.Abierto;

    /// <summary>Salirse o cancelar se puede hasta la hora de salida.</summary>
    public bool SePuedeModificar => Estado is EstadoViaje.Abierto or EstadoViaje.PorSalir;

    /// <summary>Ya terminó: se oculta de "Mis viajes".</summary>
    public bool Terminado => Estado == EstadoViaje.Finalizado;

    public string EstadoTexto => Estado switch
    {
        EstadoViaje.PorSalir => "Sale en menos de 5 minutos",
        EstadoViaje.EnCurso => "En curso",
        EstadoViaje.Cancelado => "Cancelado",
        EstadoViaje.Finalizado => "Finalizado",
        _ => ""
    };

    public bool MostrarEstado => EstadoTexto != "";

    // Último estado avisado a la pantalla (para no redibujar si no cambió nada)
    private EstadoViaje? _estadoAvisado;
    private bool? _puedeIniciarAvisado;

    /// <summary>
    /// Se llama con el timer: si el viaje cambió de estado porque pasó el tiempo
    /// (cerró las anotaciones, salió o terminó), avisa a la pantalla y devuelve true.
    /// </summary>
    internal bool RefrescarEstado()
    {
        // HU15: "Iniciar viaje" aparece 15 minutos antes de salir, aunque el estado todavía no cambie
        if (_puedeIniciarAvisado != PuedeIniciar)
        {
            _puedeIniciarAvisado = PuedeIniciar;
            OnPropertyChanged(nameof(PuedeIniciar));
        }

        if (_estadoAvisado == Estado) return false;

        AvisarCambioDeEstado();
        return true;
    }

    private void AvisarCambioDeEstado()
    {
        _estadoAvisado = Estado;
        OnPropertyChanged(nameof(Estado));
        OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(MostrarEstado));
        OnPropertyChanged(nameof(AceptaPasajeros));
        OnPropertyChanged(nameof(SePuedeModificar));
        OnPropertyChanged(nameof(Terminado));
        OnPropertyChanged(nameof(PuedeAnotarse));
        OnPropertyChanged(nameof(PuedeCancelar));
        OnPropertyChanged(nameof(PuedeSalirse));
        OnPropertyChanged(nameof(PuedeVerPuntosDeEncuentro));
        OnPropertyChanged(nameof(PuedeChatear));   // HU25
        AvisarCambioDeCheckIn();
    }

    /// <summary>HU15: botones y textos del check-in que dependen del estado o de quién subió.</summary>
    private void AvisarCambioDeCheckIn()
    {
        _puedeIniciarAvisado = PuedeIniciar;
        OnPropertyChanged(nameof(PuedeIniciar));
        OnPropertyChanged(nameof(PuedeFinalizar));
        OnPropertyChanged(nameof(PuedeConfirmarPin));
        OnPropertyChanged(nameof(PuedeVerPin));
        OnPropertyChanged(nameof(EstoyABordo));
        OnPropertyChanged(nameof(MostrarABordo));
        OnPropertyChanged(nameof(ABordoTexto));
        OnPropertyChanged(nameof(FaltanSubir));
    }

    // Relación del viaje con el usuario que inició sesión
    public bool EsDelUsuarioActual => ConductorUid == ServicioAutenticacion.UidActual;
    public bool EstoyAnotado => ServicioAutenticacion.UidActual is string uid && _pasajeros.ContainsKey(uid);
    public string RolUsuarioActual => EsDelUsuarioActual ? "Conductor" : EstoyAnotado ? "Pasajero" : "";

    /// <summary>El botón "Anotarme" solo aparece en viajes ajenos, abiertos y donde no estoy anotado.</summary>
    public bool PuedeAnotarse => !EsDelUsuarioActual && !EstoyAnotado && AceptaPasajeros;

    /// <summary>Solo el conductor puede cancelar, y solo antes de salir.</summary>
    public bool PuedeCancelar => EsDelUsuarioActual && SePuedeModificar;

    /// <summary>HU4: el botón "Salirme" solo aparece donde estoy anotado y el viaje todavía no salió.</summary>
    public bool PuedeSalirse => EstoyAnotado && SePuedeModificar;

    /// <summary>HU24: el conductor ve los puntos de encuentro mientras el viaje no terminó (también en curso).</summary>
    public bool PuedeVerPuntosDeEncuentro =>
        EsDelUsuarioActual && _pasajeros.Count > 0
        && Estado is EstadoViaje.Abierto or EstadoViaje.PorSalir or EstadoViaje.EnCurso;

    // ───── HU15: qué puede hacer cada uno ─────

    /// <summary>HU15: el conductor puede iniciar el viaje desde 15 minutos antes de la salida.</summary>
    public static readonly TimeSpan AnticipacionInicio = TimeSpan.FromMinutes(15);
    public DateTime InicioPermitido => Salida - AnticipacionInicio;

    /// <summary>HU15: "Iniciar viaje" (conductor, desde 15 minutos antes, si no está cancelado ni terminado).</summary>
    public bool PuedeIniciar =>
        EsDelUsuarioActual && !Iniciado && DateTime.Now >= InicioPermitido
        && (Estado is EstadoViaje.Abierto or EstadoViaje.PorSalir or EstadoViaje.EnCurso);

    /// <summary>
    /// HU15: "Finalizar viaje" (conductor, con el viaje en curso). Aunque no haya tocado "Iniciar viaje",
    /// después de la hora de salida lo puede marcar como completado sin esperar las 2 horas.
    /// </summary>
    public bool PuedeFinalizar => EsDelUsuarioActual && Estado == EstadoViaje.EnCurso;

    /// <summary>HU15: pasajeros que todavía no confirmaron su PIN.</summary>
    public int FaltanSubir => _pasajeros.Keys.Count(uid => !_aBordo.Contains(uid));

    /// <summary>HU15: "Confirmar PIN" (conductor, con el viaje iniciado y alguien que falte subir).</summary>
    public bool PuedeConfirmarPin => PuedeFinalizar && Iniciado && FaltanSubir > 0;

    /// <summary>HU15: el conductor ve cuántos subieron desde que inicia el viaje.</summary>
    public bool MostrarABordo => EsDelUsuarioActual && Iniciado && _pasajeros.Count > 0;
    public string ABordoTexto => $"A bordo: {_pasajeros.Count - FaltanSubir} de {_pasajeros.Count}";

    /// <summary>HU15: soy pasajero y el conductor ya confirmó mi PIN.</summary>
    public bool EstoyABordo => ServicioAutenticacion.UidActual is string uid && _aBordo.Contains(uid);

    /// <summary>HU15: el pasajero ve su PIN hasta que sube, mientras el viaje no terminó ni se canceló.</summary>
    public bool PuedeVerPin =>
        EstoyAnotado && !EstoyABordo
        && (Estado is EstadoViaje.Abierto or EstadoViaje.PorSalir or EstadoViaje.EnCurso);

    // Textos listos para mostrar en la lista
    public string Ruta => $"{Origen} → {Destino}";
    public string Detalle => $"{Fecha:dd/MM} · {Hora} hs · Conductor: {Conductor}";
    public string PasajerosTexto => _pasajeros.Count == 0
        ? "Sin pasajeros anotados"
        : $"Pasajeros: {string.Join(", ", _pasajeros.Values)}";

    // ───── HU14: reparto del costo ─────

    /// <summary>El conductor cargó un costo: solo en esos viajes se muestra y se recalcula.</summary>
    public bool TieneCosto => CostoTotal is > 0;

    /// <summary>Cuánto paga cada pasajero con los anotados de ahora (el conductor no paga su parte).</summary>
    public decimal CostoPorPasajero => CostoPara(_pasajeros.Count);

    /// <summary>
    /// Divide el total entre esa cantidad de pasajeros. Se redondea a pesos enteros hacia arriba,
    /// así entre todos nunca juntan menos que el total. Con 0 pasajeros, el que se anote paga todo.
    /// </summary>
    public decimal CostoPara(int pasajeros) =>
        TieneCosto ? Math.Ceiling(CostoTotal!.Value / Math.Max(pasajeros, 1)) : 0;

    /// <summary>Texto de la lista: cuánto paga cada uno y, si todavía me puedo anotar, cuánto pagaría yo.</summary>
    public string CostoTexto
    {
        get
        {
            if (!TieneCosto) return "";

            if (_pasajeros.Count == 0)
                return EsDelUsuarioActual
                    ? $"{Pesos(CostoTotal!.Value)} en total · todavía sin pasajeros"
                    : $"{Pesos(CostoTotal!.Value)} en total · Si te anotás: {Pesos(CostoPara(1))}";

            string porPasajero = $"{Pesos(CostoPorPasajero)} por pasajero";
            return EsDelUsuarioActual || EstoyAnotado
                ? porPasajero
                : $"{porPasajero} · Si te anotás: {Pesos(CostoPara(_pasajeros.Count + 1))}";
        }
    }

    // Formato argentino fijo ($6.000): no depende del idioma del dispositivo
    private static readonly NumberFormatInfo FormatoPesos = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    /// <summary>Muestra un importe como "$6.000".</summary>
    public static string Pesos(decimal monto) => "$" + monto.ToString("#,##0", FormatoPesos);

    /// <summary>Actualiza el viaje con los datos que llegaron del servidor.</summary>
    internal void ActualizarDesde(ViajeDatos datos)
    {
        var nuevos = datos.Pasajeros ?? new Dictionary<string, string>();
        bool cambiaronPasajeros = nuevos.Count != _pasajeros.Count || nuevos.Keys.Any(uid => !_pasajeros.ContainsKey(uid));

        if (cambiaronPasajeros)
        {
            var agregado = nuevos.FirstOrDefault(p => !_pasajeros.ContainsKey(p.Key));
            if (agregado.Key != null)
            {
                UltimoPasajeroAnotado = agregado.Value;
                UltimoPasajeroAnotadoUid = agregado.Key;
            }

            var salio = _pasajeros.FirstOrDefault(p => !nuevos.ContainsKey(p.Key));
            if (salio.Key != null)
            {
                UltimoPasajeroQueSalio = salio.Value;
                UltimoPasajeroQueSalioUid = salio.Key;
            }

            // HU16: con el viaje iniciado nadie se puede bajar, así que si falta alguien es porque
            // el conductor lo sacó por no subir a tiempo
            if (Iniciado && salio.Key != null)
            {
                _noShows.Clear();
                foreach (var (uid, nombre) in _pasajeros.Where(p => !nuevos.ContainsKey(p.Key)))
                    _noShows[uid] = nombre;
            }

            _pasajeros.Clear();
            foreach (var (uid, nombre) in nuevos)
                _pasajeros[uid] = nombre;

            OnPropertyChanged(nameof(Pasajeros));
            OnPropertyChanged(nameof(PasajerosTexto));
            OnPropertyChanged(nameof(EstoyAnotado));
            OnPropertyChanged(nameof(RolUsuarioActual));
            OnPropertyChanged(nameof(PuedeAnotarse));
            OnPropertyChanged(nameof(PuedeSalirse));
            OnPropertyChanged(nameof(PuedeVerPuntosDeEncuentro));
            OnPropertyChanged(nameof(PuedeChatear));       // HU25
            OnPropertyChanged(nameof(CostoPorPasajero));   // HU14: cambió entre cuántos se divide
            OnPropertyChanged(nameof(CostoTexto));
            AvisarCambioDeCheckIn();                       // HU15: cambia cuántos faltan subir
        }

        AsientosDisponibles = datos.AsientosDisponibles;
        Cancelado = datos.Cancelado;
        ActualizarCheckInDesde(datos);
    }

    /// <summary>HU15: hashes de los PIN, quién subió y si el conductor inició o finalizó.</summary>
    private void ActualizarCheckInDesde(ViajeDatos datos)
    {
        _pinHash.Clear();
        foreach (var (uid, hash) in datos.PinHash ?? new Dictionary<string, string>())
            _pinHash[uid] = hash;

        var aBordo = (datos.ABordo ?? new Dictionary<string, bool>())
            .Where(p => p.Value)
            .Select(p => p.Key)
            .ToHashSet();
        bool cambioABordo = !aBordo.SetEquals(_aBordo);
        if (cambioABordo)
        {
            string? subio = aBordo.FirstOrDefault(uid => !_aBordo.Contains(uid));
            if (subio != null)
            {
                UltimoPasajeroABordoUid = subio;
                UltimoPasajeroABordo = _pasajeros.GetValueOrDefault(subio, "Un pasajero");
            }
            _aBordo.Clear();
            _aBordo.UnionWith(aBordo);
        }

        bool iniciado = datos.Iniciado == true;
        bool finalizado = datos.Finalizado == true;
        bool cambioEstado = iniciado != Iniciado || finalizado != Finalizado;
        Iniciado = iniciado;
        Finalizado = finalizado;

        // HU16: desde acá se cuenta la espera para el check-in
        if (iniciado && IniciadoEn is null)
            IniciadoEn = DateTime.Now;

        if (cambioEstado)
            AvisarCambioDeEstado();   // también avisa lo del check-in
        else if (cambioABordo)
            AvisarCambioDeCheckIn();
    }

    // ───── HU25: chat grupal del viaje ─────

    private int _mensajesSinLeer;

    /// <summary>Mensajes de otros que llegaron con el chat cerrado.</summary>
    public int MensajesSinLeer
    {
        get => _mensajesSinLeer;
        internal set
        {
            if (_mensajesSinLeer == value) return;
            _mensajesSinLeer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextoBotonChat));
        }
    }

    /// <summary>El último mensaje que llegó (para el aviso de MENSAJE_RECIBIDO).</summary>
    public MensajeChat? UltimoMensaje { get; internal set; }

    public string TextoBotonChat => MensajesSinLeer > 0 ? $"Chat ({MensajesSinLeer})" : "Chat";

    /// <summary>El chat existe para el conductor y los pasajeros, hasta que el viaje termina.</summary>
    public bool PuedeChatear => (EsDelUsuarioActual || EstoyAnotado) && !Terminado;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
