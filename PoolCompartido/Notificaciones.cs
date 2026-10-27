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

        if (conCampana)
            Shell.SetTitleView(pagina, ArmarTitulo(pagina));
    }

    /// <summary>Título de la pantalla con la campanita a la derecha (en la barra de arriba).</summary>
    private static View ArmarTitulo(ContentPage pagina)
    {
        var titulo = new Label
        {
            FontSize = DeviceInfo.Platform == DevicePlatform.WinUI ? 22 : 20,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        titulo.SetBinding(Label.TextProperty, new Binding(nameof(Page.Title), source: pagina));
        titulo.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#512BD4"), Color.FromArgb("#AC99EA"));

        var barra = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            Padding = new Thickness(DeviceInfo.Platform == DevicePlatform.WinUI ? 12 : 0, 0, 4, 0)
        };
        barra.Add(titulo, 0);
        barra.Add(new Campana(), 1);
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
