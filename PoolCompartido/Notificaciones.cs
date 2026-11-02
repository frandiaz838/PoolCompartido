namespace PoolCompartido;

/// <summary>
/// HU26: lo que agrega el centro de notificaciones a cada pantalla.
/// Toda la lógica está en ServicioNotificaciones; acá solo se arma la parte visual y se navega.
/// </summary>
internal static class Notificaciones
{
    /// <summary>
    /// Prepara una pantalla: le agrega el cartel de avisos arriba (encima del contenido)
    /// y, en la barra, el título con la campanita. Se llama en el constructor, después de InitializeComponent.
    /// </summary>
    public static void Preparar(ContentPage pagina, bool conCampana = true)
    {
        ArgumentNullException.ThrowIfNull(pagina);

        // El contenido original queda igual; el cartel va en la misma celda, por encima
        var capas = new Grid();
        if (pagina.Content is View contenido)
            capas.Add(contenido);
        var cartel = new CartelNotificacion();
        capas.Add(cartel);
        capas.SizeChanged += (s, e) => cartel.AjustarAncho(capas.Width);
        pagina.Content = capas;

        // En Windows la flecha de volver de Shell queda en la franja de arriba de la ventana, que sigue el tema
        // de Windows y no el de la app (en modo claro casi no se ve). Ahí se usa una flecha propia en la barra.
        bool flechaPropia = DeviceInfo.Platform == DevicePlatform.WinUI;
        if (flechaPropia)
            Shell.SetBackButtonBehavior(pagina, new BackButtonBehavior { IsVisible = false });

        if (conCampana || flechaPropia)
            Shell.SetTitleView(pagina, ArmarTitulo(pagina, conCampana, flechaPropia));
    }

    /// <summary>Barra de arriba: (flecha para volver), título de la pantalla y la campanita a la derecha.</summary>
    private static View ArmarTitulo(ContentPage pagina, bool conCampana, bool flechaPropia)
    {
        bool windows = DeviceInfo.Platform == DevicePlatform.WinUI;
        var titulo = new Label
        {
            FontSize = windows ? 22 : 20,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        titulo.SetBinding(Label.TextProperty, new Binding(nameof(Page.Title), source: pagina));
        ColoresDelTema.Aplicar(titulo, Label.TextColorProperty, "ColorPrimario");
        SemanticProperties.SetHeadingLevel(titulo, SemanticHeadingLevel.Level1);

        var barra = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            Padding = new Thickness(windows ? 8 : 0, 0, 4, 0)
        };

        if (flechaPropia)
        {
            var atras = new Button
            {
                Text = "←",
                FontSize = 20,
                Padding = 0,
                WidthRequest = 44,
                HeightRequest = 44,
                MinimumHeightRequest = 44,
                CornerRadius = 22,
                BackgroundColor = Colors.Transparent,
                VerticalOptions = LayoutOptions.Center,
                // Las pestañas no tienen a dónde volver; todas las demás pantallas se abren encima de otra
                IsVisible = pagina is not (MainPage or MisViajesPage)
            };
            ColoresDelTema.Aplicar(atras, Button.TextColorProperty, "ColorPrimario");
            SemanticProperties.SetDescription(atras, "Volver");
            ToolTipProperties.SetText(atras, "Volver");
            atras.Clicked += async (s, e) =>
            {
                var navegacion = Shell.Current?.Navigation ?? pagina.Navigation;
                if (navegacion.NavigationStack.Count > 1)
                    await navegacion.PopAsync();
            };
            barra.Add(atras, 0);
        }

        barra.Add(titulo, 1);
        if (conCampana)
            barra.Add(new Campana(), 2);
        return barra;
    }

    /// <summary>Viaje que hay que mostrar cuando se abra "Mis viajes" o "Viajes disponibles".</summary>
    public static Viaje? ViajePendiente { get; private set; }

    /// <summary>Pide que la próxima vez que aparezca la lista, se muestre ese viaje.</summary>
    public static void MarcarViajeParaMostrar(Viaje viaje) => ViajePendiente = viaje;

    /// <summary>Lo toma la pantalla que lo muestra (una sola vez).</summary>
    public static Viaje? TomarViajePendiente()
    {
        var viaje = ViajePendiente;
        ViajePendiente = null;
        return viaje;
    }

    /// <summary>
    /// Tocar un aviso (en el cartel o en el centro): se marca como leído y se va a lo que corresponde.
    /// Un mensaje del chat abre el chat; un viaje mío o donde estoy anotado, "Mis viajes";
    /// cualquier otro viaje, "Viajes disponibles".
    /// </summary>
    public static async Task AbrirAsync(Notificacion notificacion)
    {
        ArgumentNullException.ThrowIfNull(notificacion);
        if (!notificacion.Guardada) return;   // los avisos generales no llevan a ningún lado

        ServicioNotificaciones.Instancia.MarcarLeida(notificacion);

        var shell = Shell.Current;
        if (shell is null || notificacion.ViajeId is not string viajeId) return;

        var viaje = ServicioViajes.Instancia.Viajes.FirstOrDefault(v => v.Id == viajeId);
        if (viaje is null)
        {
            await shell.CurrentPage.DisplayAlertAsync("Aviso", "Ese viaje ya no está disponible.", "OK");
            return;
        }

        // HU25: un mensaje nuevo abre directamente el chat
        if (notificacion.Evento == ViajeEvento.MENSAJE_RECIBIDO && viaje.PuedeChatear)
        {
            if (shell.CurrentPage is ChatPage) await shell.Navigation.PopAsync(false);
            await shell.Navigation.PushAsync(new ChatPage(viaje));
            return;
        }

        // Se vuelve a la pestaña que corresponde (sin dejar pantallas abiertas encima)
        ViajePendiente = viaje;
        await shell.Navigation.PopToRootAsync(false);
        bool participo = viaje.EsDelUsuarioActual || viaje.EstoyAnotado;
        await shell.GoToAsync(participo ? "//MisViajesPage" : "//MainPage");
    }

    /// <summary>Mueve la lista hasta el viaje del aviso, si está en ella.</summary>
    public static void MostrarViajePendiente(CollectionView lista, IEnumerable<Viaje> visibles)
    {
        if (ViajePendiente is not Viaje viaje || !visibles.Contains(viaje)) return;

        TomarViajePendiente();
        lista.ScrollTo(viaje, position: ScrollToPosition.Start, animate: true);
    }
}
