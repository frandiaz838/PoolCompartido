using Microsoft.Maui.Controls.Shapes;

namespace PoolCompartido;

/// <summary>
/// HU26: campanita de la barra de arriba con la cantidad de avisos sin leer.
/// Se actualiza sola con el evento SIN_LEER_CAMBIO del despachador de notificaciones.
/// Tocarla abre el centro de notificaciones.
/// </summary>
public class Campana : ContentView
{
    private readonly Border _globito;
    private readonly Label _cantidad;
    private readonly Action<Notificacion?> _alCambiar;
    private Despachador<EventoNotificacion, Notificacion?>? _registradoEn;

    public Campana()
    {
        var icono = new Image
        {
            Source = "campana.png",
            WidthRequest = 24,
            HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _cantidad = new Label
        {
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        // Globito rojo arriba a la derecha (con el color de "peligro" de la paleta elegida, HU19)
        _globito = new Border
        {
            Content = _cantidad,
            HeightRequest = 18,
            MinimumWidthRequest = 18,
            Padding = new Thickness(4, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false
        };
        _globito.SetDynamicResource(VisualElement.BackgroundColorProperty, "ColorPeligro");
        // Blanco sobre el rojo del tema claro y oscuro sobre el rojo claro del tema oscuro (contraste ≥ 4,5:1)
        _cantidad.SetDynamicResource(Label.TextColorProperty, "ColorSobrePeligro");

        var zona = new Grid { WidthRequest = 44, HeightRequest = 44 };
        zona.Add(icono);
        zona.Add(_globito);
        zona.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await AbrirCentroAsync()) });

        Content = zona;
        VerticalOptions = LayoutOptions.Center;

        _alCambiar = _ => Actualizar();
        Loaded += (s, e) =>
        {
            _registradoEn?.Quitar(EventoNotificacion.SIN_LEER_CAMBIO, _alCambiar);
            _registradoEn = ServicioNotificaciones.Instancia.Eventos;
            _registradoEn.Registrar(EventoNotificacion.SIN_LEER_CAMBIO, _alCambiar);
            Actualizar();
        };
        Unloaded += (s, e) => _registradoEn?.Quitar(EventoNotificacion.SIN_LEER_CAMBIO, _alCambiar);
    }

    private void Actualizar()
    {
        int sinLeer = ServicioNotificaciones.Instancia.SinLeer;
        _cantidad.Text = sinLeer > 9 ? "9+" : sinLeer.ToString();
        _globito.IsVisible = sinLeer > 0;
        SemanticProperties.SetDescription(this, sinLeer == 0 ? "Notificaciones" : $"Notificaciones, {sinLeer} sin leer");
    }

    private async Task AbrirCentroAsync()
    {
        var navegacion = Shell.Current?.Navigation;
        if (navegacion is null || Shell.Current!.CurrentPage is NotificacionesPage) return;
        await navegacion.PushAsync(new NotificacionesPage());
    }
}
