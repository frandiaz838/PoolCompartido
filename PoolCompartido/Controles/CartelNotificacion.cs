using Microsoft.Maui.Controls.Shapes;

namespace PoolCompartido;

/// <summary>
/// HU26: cartel que baja desde arriba con un aviso nuevo y se va solo a los pocos segundos.
/// Cada pantalla tiene el suyo (lo agrega Notificaciones.Preparar) y se suscribe al despachador
/// de notificaciones mientras está en pantalla. Solo se muestra en la pantalla que se está viendo.
/// Tocarlo lleva al viaje del aviso; la ✕ lo cierra.
/// </summary>
public class CartelNotificacion : ContentView
{
    /// <summary>Cuánto tiempo queda a la vista.</summary>
    public static readonly TimeSpan Duracion = TimeSpan.FromSeconds(5);

    /// <summary>Ancho máximo del cartel (en la compu no ocupa toda la ventana).</summary>
    public const double AnchoMaximo = 560;

    private readonly Label _icono;
    private readonly Label _texto;
    private readonly Action<Notificacion?> _alLlegar;
    private Despachador<EventoNotificacion, Notificacion?>? _registradoEn;
    private Notificacion? _actual;
    private int _version;   // cada aviso nuevo invalida el "ocultar" del anterior

    public CartelNotificacion()
    {
        // Colores con los tokens del tema: cambian solos cuando cambia el tema (como AppThemeBinding)
        _icono = new Label { FontSize = 18, VerticalOptions = LayoutOptions.Center };
        AutomationProperties.SetIsInAccessibleTree(_icono, false);   // el emoji es decorativo
        _texto = new Label { FontSize = 14, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 4, VerticalOptions = LayoutOptions.Center };
        ColoresDelTema.Aplicar(_texto, Label.TextColorProperty, "ColorTexto");

        // La ✕ con zona de toque de 44 × 44 (aunque el dibujo sea chico)
        var cerrar = new Label
        {
            Text = "✕",
            FontSize = 16,
            WidthRequest = 44,
            HeightRequest = 44,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };
        ColoresDelTema.Aplicar(cerrar, Label.TextColorProperty, "ColorTextoSecundario");
        SemanticProperties.SetDescription(cerrar, "Cerrar aviso");
        cerrar.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _ = OcultarAsync(_version)) });

        var fila = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        fila.Add(_icono, 0);
        fila.Add(_texto, 1);
        fila.Add(cerrar, 2);

        var caja = new Border
        {
            Content = fila,
            Padding = new Thickness(14, 4, 4, 4),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Shadow = new Shadow { Brush = new SolidColorBrush(ColorDelTema("ColorSombra")), Opacity = 0.25f, Radius = 12, Offset = new Point(0, 4) }
        };
        ColoresDelTema.Aplicar(caja, VisualElement.BackgroundColorProperty, "ColorSuperficie");
        ColoresDelTema.AplicarPincel(caja, Border.StrokeProperty, "ColorBordeControl");
        caja.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await AbrirAsync()) });

        Content = caja;
        VerticalOptions = LayoutOptions.Start;
        HorizontalOptions = LayoutOptions.Center;
        Margin = new Thickness(12, 10);
        IsVisible = false;
        ZIndex = 100;

        _alLlegar = notificacion =>
        {
            if (notificacion is not null && EsLaPantallaVisible())
                _ = MostrarAsync(notificacion);
        };

        // Se suscribe mientras está en pantalla y se borra de la tabla al salir (Despachador.Quitar)
        Loaded += (s, e) => AlCargar();
        Unloaded += (s, e) => _registradoEn?.Quitar(EventoNotificacion.NOTIFICACION_NUEVA, _alLlegar);
    }

    /// <summary>Un color fijo de Colors.xaml (la sombra es negra en los dos temas).</summary>
    private static Color ColorDelTema(string token) =>
        Application.Current?.Resources.TryGetValue(token, out var valor) == true && valor is Color color ? color : Colors.Transparent;

    /// <summary>
    /// Fija el ancho según la pantalla. Con un ancho fijo el texto se acomoda en varias líneas y el cartel
    /// crece hacia abajo; con un ancho "máximo" Windows lo medía en una sola línea y lo recortaba.
    /// </summary>
    public void AjustarAncho(double anchoPantalla)
    {
        if (anchoPantalla <= 0) return;
        double ancho = Math.Min(AnchoMaximo, anchoPantalla - Margin.HorizontalThickness);
        if (Math.Abs(WidthRequest - ancho) > 0.5)
            WidthRequest = ancho;
    }

    private void AlCargar()
    {
        _registradoEn?.Quitar(EventoNotificacion.NOTIFICACION_NUEVA, _alLlegar);
        _registradoEn = ServicioNotificaciones.Instancia.Eventos;
        _registradoEn.Registrar(EventoNotificacion.NOTIFICACION_NUEVA, _alLlegar);

        // Un aviso que llegó justo antes de que esta pantalla estuviera lista (por ejemplo, "Sin conexión" al entrar)
        if (ServicioNotificaciones.Instancia.UltimoCartel is Notificacion ultimo
            && DateTime.Now - ultimo.Hora < TimeSpan.FromSeconds(3))
            _alLlegar(ultimo);
    }

    /// <summary>Las pestañas y pantallas de atrás también tienen su cartel: solo se muestra en la de adelante.</summary>
    private bool EsLaPantallaVisible()
    {
        Element? actual = this;
        while (actual is not null and not Page)
            actual = actual.Parent;

        var adelante = Shell.Current?.CurrentPage;
        return actual is null || adelante is null || ReferenceEquals(actual, adelante);
    }

    private async Task MostrarAsync(Notificacion notificacion)
    {
        _actual = notificacion;
        _icono.Text = notificacion.Icono;
        _texto.Text = notificacion.Texto;
        SemanticProperties.SetDescription(this, "Aviso: " + notificacion.Texto);
        int version = ++_version;

        if (!IsVisible)
        {
            IsVisible = true;
            Opacity = 0;
            TranslationY = -30;
            await Task.WhenAll(this.FadeTo(1, 200), this.TranslateTo(0, 0, 200, Easing.CubicOut));
        }

        await Task.Delay(Duracion);
        await OcultarAsync(version);
    }

    private async Task OcultarAsync(int version)
    {
        // Si mientras tanto llegó otro aviso, este "ocultar" ya no corresponde
        if (version != _version || !IsVisible) return;

        await Task.WhenAll(this.FadeTo(0, 180), this.TranslateTo(0, -30, 180, Easing.CubicIn));
        if (version == _version)
            IsVisible = false;
    }

    private async Task AbrirAsync()
    {
        if (_actual is not Notificacion notificacion) return;

        await OcultarAsync(_version);
        await Notificaciones.AbrirAsync(notificacion);
    }
}
