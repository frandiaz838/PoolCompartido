using System.ComponentModel;
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

    /// <summary>HU24: lugar público desde donde sale el conductor (puede estar vacío).</summary>
    public string PuntoPartida { get; init; } = "";
    public bool TienePuntoPartida => !string.IsNullOrWhiteSpace(PuntoPartida);
    public string PuntoPartidaTexto => $"Sale desde: {PuntoPartida}";

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
        if (ahora >= Salida + DuracionEnCurso) return EstadoViaje.Finalizado;
        if (Cancelado) return EstadoViaje.Cancelado;
        if (ahora >= Salida) return EstadoViaje.EnCurso;
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

    /// <summary>
    /// Se llama con el timer: si el viaje cambió de estado porque pasó el tiempo
    /// (cerró las anotaciones, salió o terminó), avisa a la pantalla y devuelve true.
    /// </summary>
    internal bool RefrescarEstado()
    {
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

    // Textos listos para mostrar en la lista
    public string Ruta => $"{Origen} → {Destino}";
    public string Detalle => $"{Fecha:dd/MM} · {Hora} hs · Conductor: {Conductor}";
    public string PasajerosTexto => _pasajeros.Count == 0
        ? "Sin pasajeros anotados"
        : $"Pasajeros: {string.Join(", ", _pasajeros.Values)}";

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
        }

        AsientosDisponibles = datos.AsientosDisponibles;
        Cancelado = datos.Cancelado;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
