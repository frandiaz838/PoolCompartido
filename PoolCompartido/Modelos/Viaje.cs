using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PoolCompartido;

/// <summary>
/// Un viaje publicado por un conductor.
/// Implementa INotifyPropertyChanged: cuando cambia un dato, la pantalla se redibuja sola.
/// </summary>
public class Viaje : INotifyPropertyChanged
{
    private static int _ultimoId;

    public int Id { get; } = Interlocked.Increment(ref _ultimoId);
    public string Origen { get; init; } = "";
    public string Destino { get; init; } = "";
    public DateTime Fecha { get; init; }
    public string Hora { get; init; } = "";
    public string Conductor { get; init; } = "";
    public string Comentario { get; init; } = "";

    /// <summary>Nombres de los pasajeros anotados (se usa para avisarles si se cancela el viaje).</summary>
    private readonly List<string> _pasajeros = new();
    public IReadOnlyList<string> Pasajeros => _pasajeros;

    // Relación del viaje con el usuario que está usando la app
    public bool EsDelUsuarioActual => Conductor == ServicioViajes.UsuarioActual;
    public bool EstoyAnotado => _pasajeros.Contains(ServicioViajes.UsuarioActual);
    public string RolUsuarioActual => EsDelUsuarioActual ? "Conductor" : EstoyAnotado ? "Pasajero" : "";

    public void AgregarPasajero(string nombre)
    {
        _pasajeros.Add(nombre);
        OnPropertyChanged(nameof(Pasajeros));
        OnPropertyChanged(nameof(EstoyAnotado));
        OnPropertyChanged(nameof(RolUsuarioActual));
    }

    private int _asientosDisponibles;
    public int AsientosDisponibles
    {
        get => _asientosDisponibles;
        set
        {
            if (_asientosDisponibles == value) return;
            _asientosDisponibles = value;
            OnPropertyChanged();
        }
    }

    // Textos listos para mostrar en la lista
    public string Ruta => $"{Origen} → {Destino}";
    public string Detalle => $"{Fecha:dd/MM} · {Hora} hs · Conductor: {Conductor}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}
