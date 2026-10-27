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

        listaViajes.ItemsSource = _viajesVisibles;
        pickerHorario.ItemsSource = FranjasHorarias.Nombres;
        pickerHorario.SelectedIndex = 0;

        // Tabla de eventos: esta pantalla registra la misma callback para todos los eventos de viajes.
        // Los eventos llegan tanto por acciones en este dispositivo como por la sincronización con Firebase.
        ViajeEvento[] eventos =
        {
            ViajeEvento.VIAJE_PUBLICADO, ViajeEvento.ASIENTO_RESERVADO, ViajeEvento.ASIENTO_LIBERADO,
            ViajeEvento.VIAJE_CANCELADO, ViajeEvento.REEMPLAZO_ENCONTRADO, ViajeEvento.SIN_REEMPLAZO,
            ViajeEvento.COSTO_RECALCULADO, ViajeEvento.VIAJE_INICIADO, ViajeEvento.PASAJERO_A_BORDO,
            ViajeEvento.VIAJE_FINALIZADO
        };
        foreach (var evento in eventos)
            _servicio.Eventos.Registrar(evento, viaje => AlOcurrirEvento(evento, viaje));

        // Evento estándar de .NET: se cortó o volvió internet
        _servicio.ConexionCambiada += (sender, hayConexion) =>
            MostrarAviso(hayConexion
                ? "Volvió la conexión: los viajes están actualizados."
                : "Sin conexión: se muestran los últimos viajes guardados.");

        // Pasó el tiempo y algún viaje cerró las anotaciones: desaparece de la lista
        _servicio.EstadosCambiados += (sender, e) => Redibujar();

        Redibujar();

        if (_servicio.AvisoAlIniciar is string aviso)
            MostrarAviso(aviso);
    }

    /// <summary>
    /// Callback de los eventos de viajes: redibuja si cambió la lista (evento Paint) y
    /// muestra el mensaje que corresponde según mi rol (HU7, conversión de mensajes).
    /// </summary>
    private void AlOcurrirEvento(ViajeEvento evento, Viaje viaje)
    {
        // HU15: un viaje iniciado o finalizado ya no acepta pasajeros, así que sale de la lista
        if (evento is ViajeEvento.VIAJE_PUBLICADO or ViajeEvento.VIAJE_CANCELADO or ViajeEvento.ASIENTO_LIBERADO
                   or ViajeEvento.VIAJE_INICIADO or ViajeEvento.VIAJE_FINALIZADO)
            Redibujar();

        string? mensaje = ConversorMensajes.Convertir(evento, viaje);

        // HU14: el nuevo costo llega encadenado justo después de "X se anotó" o "X se bajó":
        // se agrega abajo de ese aviso en lugar de taparlo
        if (evento == ViajeEvento.COSTO_RECALCULADO && mensaje is not null && _viajeDelAviso == viaje)
            lblAviso.Text += "\n" + mensaje;
        else if (mensaje is not null)
            MostrarAviso(mensaje);

        _viajeDelAviso = mensaje is null ? null : viaje;

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
    }

    /// <summary>
    /// Equivalente al evento Paint: vuelve a armar la lista visible según los filtros.
    /// </summary>
    private void Redibujar()
    {
        string destino = entryDestino.Text?.Trim() ?? "";
        var franja = (FranjaHoraria)Math.Max(pickerHorario.SelectedIndex, 0);

        var filtrados = _servicio.Viajes
            .Where(v => v.AceptaPasajeros)   // ni cancelados ni a menos de 5 minutos de salir
            .Where(v => destino == "" || v.Destino.Contains(destino, StringComparison.OrdinalIgnoreCase))
            .Where(v => FranjasHorarias.Incluye(franja, v.Hora))
            .OrderBy(v => v.Salida);

        _viajesVisibles.Clear();
        foreach (var viaje in filtrados)
            _viajesVisibles.Add(viaje);
    }

    /// <summary>Viaje del último aviso de un evento (para sumarle el del costo, HU14).</summary>
    private Viaje? _viajeDelAviso;

    private void MostrarAviso(string mensaje)
    {
        _viajeDelAviso = null;
        lblAviso.Text = mensaje;
        panelAviso.IsVisible = true;
    }

    private void OnDestinoCambiado(object? sender, TextChangedEventArgs e) => Redibujar();

    private void OnHorarioCambiado(object? sender, EventArgs e) => Redibujar();

    private void OnLimpiarClicked(object? sender, EventArgs e)
    {
        entryDestino.Text = "";
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
        if (sender is not Button { BindingContext: Viaje viaje }) return;

        // HU24: si participás del viaje, la ruta pasa por los puntos de encuentro de los pasajeros
        await PuntoDeEncuentro.AbrirRutaAsync(this, viaje);
    }

    /// <summary>Registra la excepción como aviso y se la muestra al usuario.</summary>
    private async Task MostrarProblemaAsync(string titulo, Exception ex)
    {
        RegistroErrores.RegistrarAviso(ex, "Anotarse a un viaje");
        await PaginaVisible.DisplayAlertAsync(titulo, ex.Message, "OK");
    }
}
