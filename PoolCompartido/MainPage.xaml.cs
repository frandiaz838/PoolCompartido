using System.Collections.ObjectModel;

namespace PoolCompartido;

public partial class MainPage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;

    /// <summary>Viajes que se ven en pantalla (ya filtrados).</summary>
    private readonly ObservableCollection<Viaje> _viajesVisibles = new();

    public MainPage()
    {
        InitializeComponent();

        // HU26: cartel de avisos y campanita
        Notificaciones.Preparar(this);

        listaViajes.ItemsSource = _viajesVisibles;
        pickerHorario.ItemsSource = FranjasHorarias.Nombres;
        pickerHorario.SelectedIndex = 0;
        CargarDias();

        // Tabla de eventos: esta pantalla registra la misma callback para todos los eventos de viajes.
        // Los eventos llegan tanto por acciones en este dispositivo como por la sincronización con Firebase.
        // HU26: el texto del aviso lo muestra ServicioNotificaciones (en cualquier pantalla); acá solo se redibuja.
        ViajeEvento[] eventos =
        {
            ViajeEvento.VIAJE_PUBLICADO, ViajeEvento.ASIENTO_RESERVADO, ViajeEvento.ASIENTO_LIBERADO,
            ViajeEvento.VIAJE_CANCELADO, ViajeEvento.REEMPLAZO_ENCONTRADO, ViajeEvento.SIN_REEMPLAZO,
            ViajeEvento.COSTO_RECALCULADO, ViajeEvento.VIAJE_INICIADO, ViajeEvento.PASAJERO_A_BORDO,
            ViajeEvento.VIAJE_FINALIZADO, ViajeEvento.MENSAJE_RECIBIDO, ViajeEvento.NO_SHOW_DETECTADO
        };
        foreach (var evento in eventos)
            _servicio.Eventos.Registrar(evento, viaje => AlOcurrirEvento(evento, viaje));

        // Evento estándar de .NET: se cortó o volvió internet (aviso en el cartel, no se guarda)
        _servicio.ConexionCambiada += (sender, hayConexion) =>
            ServicioNotificaciones.Instancia.Avisar(hayConexion
                ? "Volvió la conexión: los viajes están actualizados."
                : "Sin conexión: se muestran los últimos viajes guardados.");

        // Pasó el tiempo y algún viaje cerró las anotaciones: desaparece de la lista
        _servicio.EstadosCambiados += (sender, e) => Redibujar();

        // HU19: cambió el tema o la paleta: las tarjetas se vuelven a dibujar con los colores nuevos
        ServicioApariencia.Eventos.Registrar(EventoApariencia.APARIENCIA_CAMBIADA, _ => Redibujar());

        Redibujar();

        if (_servicio.AvisoAlIniciar is string aviso)
            ServicioNotificaciones.Instancia.Avisar(aviso);
    }

    /// <summary>
    /// Callback de los eventos de viajes: redibuja si cambió la lista (evento Paint).
    /// El mensaje según mi rol (HU7) lo muestra ServicioNotificaciones en la pantalla que esté abierta (HU26).
    /// </summary>
    private void AlOcurrirEvento(ViajeEvento evento, Viaje viaje)
    {
        // HU15: un viaje iniciado o finalizado ya no acepta pasajeros, así que sale de la lista
        if (evento is ViajeEvento.VIAJE_PUBLICADO or ViajeEvento.VIAJE_CANCELADO or ViajeEvento.ASIENTO_LIBERADO
                   or ViajeEvento.VIAJE_INICIADO or ViajeEvento.VIAJE_FINALIZADO)
            Redibujar();
        else
            ActualizarProximo();   // HU29: por ejemplo, me anoté y ahora ese es mi próximo viaje

        // HU6: se encontró un viaje parecido, se ofrece anotarse con un toque
        if (evento == ViajeEvento.REEMPLAZO_ENCONTRADO && viaje.Reemplazo is Viaje otro)
            OfrecerReemplazo(viaje, otro);
    }

    /// <summary>La pestaña que está abierta: los carteles se muestran ahí aunque sea "Mis viajes".</summary>
    private Page PaginaVisible => Shell.Current?.CurrentPage ?? this;

    private async void OfrecerReemplazo(Viaje cancelado, Viaje otro)
    {
        bool anotarme = await PaginaVisible.DisplayAlertAsync("Viaje cancelado",
            $"{cancelado.Conductor} canceló el viaje {cancelado.Ruta}.\n\n" +
            $"Encontramos otro parecido: {otro.Ruta}, sale a las {otro.Hora} con {otro.Conductor} " +
            $"({otro.AsientosDisponibles} lugares). ¿Querés anotarte?",
            "Sí, anotarme", "No");

        if (anotarme)
            await AnotarAsync(otro);
    }

    // Cada vez que se vuelve a esta pantalla, se redibuja (por si cambiaron los datos)
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Redibujar();
        // HU26: se llegó tocando un aviso: se muestra ese viaje
        Notificaciones.MostrarViajePendiente(listaViajes, _viajesVisibles);
    }

    /// <summary>
    /// Equivalente al evento Paint: vuelve a armar la lista visible según los filtros.
    /// </summary>
    private void Redibujar()
    {
        string origen = Geocodificador.Normalizar(entryOrigen.Text?.Trim() ?? "");
        string destino = Geocodificador.Normalizar(entryDestino.Text?.Trim() ?? "");
        var franja = (FranjaHoraria)Math.Max(pickerHorario.SelectedIndex, 0);
        DateTime? dia = pickerDia.SelectedIndex > 0 && pickerDia.SelectedIndex < _dias.Count ? _dias[pickerDia.SelectedIndex] : null;

        // HU30: se marca cada viaje que coincide con mis horarios habituales
        ServicioHorarios.Instancia.ActualizarCoincidencias(_servicio.Viajes);

        // Sin tildes ni mayúsculas: "cordoba" encuentra "Córdoba"
        var filtrados = _servicio.Viajes
            .Where(v => v.AceptaPasajeros)   // ni cancelados ni a menos de 5 minutos de salir
            .Where(v => origen == "" || Geocodificador.Normalizar(v.Origen).Contains(origen)
                                     || Geocodificador.Normalizar(v.PuntoPartida).Contains(origen))
            .Where(v => destino == "" || Geocodificador.Normalizar(v.Destino).Contains(destino))
            .Where(v => dia is null || v.Fecha.Date == dia.Value)
            .Where(v => FranjasHorarias.Incluye(franja, v.Hora))
            // HU30: primero los que coinciden con mis horarios, después por hora de salida
            .OrderBy(v => v.CoincideConMiHorario ? 0 : 1)
            .ThenBy(v => v.Salida);

        _viajesVisibles.Clear();
        foreach (var viaje in filtrados)
            _viajesVisibles.Add(viaje);

        ActualizarProximo();
    }

    // ───── HU29: tarjeta "Tu próximo viaje" ─────

    /// <summary>El viaje más próximo donde soy conductor o pasajero (el que está en curso, primero).</summary>
    private Viaje? _proximo;

    private void ActualizarProximo()
    {
        var proximo = _servicio.Viajes
            .Where(v => (v.EsDelUsuarioActual || v.EstoyAnotado) && v.Estado is not (EstadoViaje.Cancelado or EstadoViaje.Finalizado))
            .OrderBy(v => v.Estado == EstadoViaje.EnCurso ? 0 : 1)
            .ThenBy(v => v.Salida)
            .FirstOrDefault();

        if (!ReferenceEquals(proximo, _proximo))
        {
            _proximo = proximo;
            tarjetaProximo.BindingContext = proximo;
        }
        tarjetaProximo.IsVisible = proximo is not null;
    }

    private async void OnProximoRutaClicked(object? sender, EventArgs e)
    {
        if (_proximo is Viaje viaje)
            await PuntoDeEncuentro.AbrirRutaAsync(this, viaje, sender as Button);
    }

    private async void OnProximoChatClicked(object? sender, EventArgs e)
    {
        if (_proximo is Viaje viaje)
            await Navigation.PushAsync(new ChatPage(viaje));
    }

    /// <summary>Lleva a "Mis viajes", con la lista en ese viaje (lo mismo que tocar un aviso, HU26).</summary>
    private async void OnProximoVerClicked(object? sender, EventArgs e)
    {
        if (_proximo is not Viaje viaje) return;
        Notificaciones.MarcarViajeParaMostrar(viaje);
        await Shell.Current.GoToAsync("//MisViajesPage");
    }

    // ───── Filtros ─────

    /// <summary>Fechas del filtro "Día" (la primera, null, es "Cualquier día").</summary>
    private readonly List<DateTime?> _dias = new();

    /// <summary>"Cualquier día", "Hoy", "Mañana" y los 5 días siguientes ("Sáb 01/11").</summary>
    private void CargarDias()
    {
        _dias.Clear();
        _dias.Add(null);
        var nombres = new List<string> { "Cualquier día" };
        for (int i = 0; i < 7; i++)
        {
            var fecha = DateTime.Today.AddDays(i);
            _dias.Add(fecha);
            nombres.Add(i switch
            {
                0 => "Hoy",
                1 => "Mañana",
                _ => $"{HorarioHabitual.DiaCorto(fecha.DayOfWeek)} {fecha:dd/MM}"
            });
        }
        pickerDia.ItemsSource = nombres;
        pickerDia.SelectedIndex = 0;
    }

    private void OnFiltroTextoCambiado(object? sender, TextChangedEventArgs e) => Redibujar();

    private void OnHorarioCambiado(object? sender, EventArgs e) => Redibujar();

    /// <summary>⇄: invierte desde y hacia, para buscar la vuelta.</summary>
    private void OnInvertirClicked(object? sender, EventArgs e)
    {
        (entryOrigen.Text, entryDestino.Text) = (entryDestino.Text, entryOrigen.Text);
    }

    private void OnLimpiarClicked(object? sender, EventArgs e)
    {
        entryOrigen.Text = "";
        entryDestino.Text = "";
        pickerDia.SelectedIndex = 0;
        pickerHorario.SelectedIndex = 0;
    }

    private async void OnPerfilClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new PerfilPage());
    }

    private async void OnPublicarClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new PublicarViajePage());
    }

    private async void OnAnotarmeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        boton.IsEnabled = false;   // evita que se toque dos veces mientras espera al servidor
        try
        {
            await AnotarAsync(viaje);
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>Anota al usuario y le muestra el resultado (lo usan el botón y la oferta de reemplazo).</summary>
    private async Task AnotarAsync(Viaje viaje)
    {
        try
        {
            await _servicio.AnotarseAsync(viaje);
            await PaginaVisible.DisplayAlertAsync("Reserva confirmada",
                $"¡Te anotaste al viaje de {viaje.Conductor}! Quedan {viaje.AsientosDisponibles} asientos.", "OK");

            // HU25: aviso automático en el chat del viaje
            await ServicioChat.Instancia.AvisarAlGrupoAsync(viaje, "se sumó al viaje");

            // HU32: si cargué mi teléfono, lo pueden ver los de este viaje
            await ServicioContactos.Instancia.CompartirEnViajeSinFallarAsync(viaje);

            // HU24: enseguida se pregunta dónde lo pasan a buscar (se puede dejar para después)
            await PuntoDeEncuentro.PedirYGuardarAsync(PaginaVisible, viaje);
        }
        // Excepciones específicas primero, la más general al final
        catch (ViajeCompletoException ex)
        {
            await MostrarProblemaAsync("Sin lugar", ex);
        }
        catch (HorarioOcupadoException ex)
        {
            await MostrarProblemaAsync("Horario ocupado", ex);
        }
        catch (ViajeException ex)
        {
            await MostrarProblemaAsync("No disponible", ex);
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Anotarse a un viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Anotarse a un viaje");
            await PaginaVisible.DisplayAlertAsync("No se pudo anotar", ex.Message, "OK");
        }
    }

    /// <summary>HU22: abre la ruta del viaje en Google Maps.</summary>
    private async void OnVerRutaClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        // HU24: si participás del viaje, la ruta pasa por los puntos de encuentro, en el orden más corto
        await PuntoDeEncuentro.AbrirRutaAsync(this, viaje, boton);
    }

    /// <summary>Registra la excepción como aviso y se la muestra al usuario.</summary>
    private async Task MostrarProblemaAsync(string titulo, Exception ex)
    {
        RegistroErrores.RegistrarAviso(ex, "Anotarse a un viaje");
        await PaginaVisible.DisplayAlertAsync(titulo, ex.Message, "OK");
    }
}
