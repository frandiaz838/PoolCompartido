using System.Collections.ObjectModel;

namespace PoolCompartido;

/// <summary>
/// HU26: centro de notificaciones. Lista los avisos guardados (los últimos 50), los no leídos resaltados.
/// Se actualiza sola con el despachador de notificaciones mientras está abierta.
/// </summary>
public partial class NotificacionesPage : ContentPage
{
    private readonly ServicioNotificaciones _servicio = ServicioNotificaciones.Instancia;
    private readonly ObservableCollection<Notificacion> _visibles = new();
    private readonly Action<Notificacion?> _alCambiar;

    public NotificacionesPage()
    {
        InitializeComponent();
        listaNotificaciones.ItemsSource = _visibles;

        // Cartel de avisos sí, campanita no (ya estás en el centro)
        Notificaciones.Preparar(this, conCampana: false);

        _alCambiar = _ => Redibujar();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _servicio.Eventos.Registrar(EventoNotificacion.SIN_LEER_CAMBIO, _alCambiar);
        Redibujar();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _servicio.Eventos.Quitar(EventoNotificacion.SIN_LEER_CAMBIO, _alCambiar);
    }

    /// <summary>Equivalente al evento Paint: vuelve a armar la lista.</summary>
    private void Redibujar()
    {
        _visibles.Clear();
        foreach (var notificacion in _servicio.Lista)
        {
            notificacion.RefrescarHora();   // "Hace 5 min" con la hora de ahora
            _visibles.Add(notificacion);
        }

        int sinLeer = _servicio.SinLeer;
        lblResumen.Text = _visibles.Count == 0
            ? ""
            : sinLeer == 0
                ? "Estás al día. Tocá un aviso para ir a ese viaje."
                : $"Tenés {sinLeer} sin leer. Tocá un aviso para ir a ese viaje.";
        lblResumen.IsVisible = _visibles.Count > 0;
    }

    private void OnMarcarTodoClicked(object? sender, EventArgs e) => _servicio.MarcarTodasLeidas();

    private async void OnNotificacionTocada(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: Notificacion notificacion }) return;
        await Notificaciones.AbrirAsync(notificacion);
    }
}
