using System.Collections.ObjectModel;

namespace PoolCompartido;

public partial class MisViajesPage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;
    private readonly ObservableCollection<Viaje> _misViajes = new();

    public MisViajesPage()
    {
        InitializeComponent();

        listaMisViajes.ItemsSource = _misViajes;

        // Esta pantalla también se suscribe a la tabla de eventos:
        // cada vez que publicás o te anotás, la lista se redibuja sola.
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_PUBLICADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, _ => Redibujar());

        Redibujar();
    }

    /// <summary>Equivalente al evento Paint: vuelve a armar la lista.</summary>
    private void Redibujar()
    {
        _misViajes.Clear();
        foreach (var viaje in _servicio.MisViajes())
            _misViajes.Add(viaje);
    }
}
