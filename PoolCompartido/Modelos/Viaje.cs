using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PoolCompartido;

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
            OnPropertyChanged(nameof(PuedeCancelar));
            OnPropertyChanged(nameof(PuedeAnotarse));
            OnPropertyChanged(nameof(PuedeSalirse));
        }
    }

    // Relación del viaje con el usuario que inició sesión
    public bool EsDelUsuarioActual => ConductorUid == ServicioAutenticacion.UidActual;
    public bool EstoyAnotado => ServicioAutenticacion.UidActual is string uid && _pasajeros.ContainsKey(uid);
    public string RolUsuarioActual => EsDelUsuarioActual ? "Conductor" : EstoyAnotado ? "Pasajero" : "";

    /// <summary>El botón "Anotarme" solo aparece en viajes ajenos, activos y donde no estoy anotado.</summary>
    public bool PuedeAnotarse => !EsDelUsuarioActual && !EstoyAnotado && !Cancelado;

    /// <summary>Solo el conductor puede cancelar, y solo si todavía no lo canceló.</summary>
    public bool PuedeCancelar => EsDelUsuarioActual && !Cancelado;

    /// <summary>HU4: el botón "Salirme" solo aparece en viajes activos donde estoy anotado como pasajero.</summary>
    public bool PuedeSalirse => EstoyAnotado && !Cancelado;

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
        }

        AsientosDisponibles = datos.AsientosDisponibles;
        Cancelado = datos.Cancelado;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
