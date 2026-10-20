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

        // Tabla de eventos: qué hace esta pantalla cuando ocurre cada evento (callbacks).
        // Los eventos llegan tanto por acciones en este dispositivo como por la sincronización con Firebase.
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_PUBLICADO, viaje =>
        {
            Redibujar();
            MostrarAviso(viaje.EsDelUsuarioActual
                ? $"Publicaste tu viaje {viaje.Ruta} del {viaje.Fecha:dd/MM}."
                : $"Nuevo viaje de {viaje.Conductor}: {viaje.Ruta}, el {viaje.Fecha:dd/MM} a las {viaje.Hora}.");
        });

        // HU3: el conductor recibe una notificación cuando alguien se anota
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, viaje =>
        {
            if (viaje.EsDelUsuarioActual)
                MostrarAviso($"{viaje.UltimoPasajeroAnotado} se anotó a tu viaje a {viaje.Destino}. Quedan {viaje.AsientosDisponibles} asientos.");
        });

        // HU5: los pasajeros anotados reciben el aviso de la cancelación
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_CANCELADO, viaje =>
        {
            Redibujar();
            if (viaje.EstoyAnotado)
                MostrarAviso($"{viaje.Conductor} canceló el viaje {viaje.Ruta} del {viaje.Fecha:dd/MM} a las {viaje.Hora}.");
            else if (viaje.EsDelUsuarioActual)
                MostrarAviso($"Cancelaste tu viaje {viaje.Ruta}. Se avisó a {viaje.Pasajeros.Count} pasajero(s).");
        });

        // Evento estándar de .NET: se cortó o volvió internet
        _servicio.ConexionCambiada += (sender, hayConexion) =>
            MostrarAviso(hayConexion
                ? "Volvió la conexión: los viajes están actualizados."
                : "Sin conexión: se muestran los últimos viajes guardados.");

        Redibujar();

        if (_servicio.AvisoAlIniciar is string aviso)
            MostrarAviso(aviso);
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
            .Where(v => !v.Cancelado)
            .Where(v => destino == "" || v.Destino.Contains(destino, StringComparison.OrdinalIgnoreCase))
            .Where(v => FranjasHorarias.Incluye(franja, v.Hora))
            .OrderBy(v => v.Fecha)
            .ThenBy(v => v.Hora);

        _viajesVisibles.Clear();
        foreach (var viaje in filtrados)
            _viajesVisibles.Add(viaje);
    }

    private void MostrarAviso(string mensaje)
    {
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
            await _servicio.AnotarseAsync(viaje);
            await DisplayAlertAsync("Reserva confirmada",
                $"¡Te anotaste al viaje de {viaje.Conductor}! Quedan {viaje.AsientosDisponibles} asientos.", "OK");
        }
        // Excepciones específicas primero, la más general al final
        catch (ViajeCompletoException ex)
        {
            await MostrarProblemaAsync("Sin lugar", ex);
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
            await DisplayAlertAsync("No se pudo anotar", ex.Message, "OK");
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>HU22: abre la ruta del viaje en Google Maps.</summary>
    private async void OnVerRutaClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje }) return;

        try
        {
            await ServicioMapas.AbrirRutaAsync(viaje);
        }
        catch (ArgumentException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Ver ruta");
            await DisplayAlertAsync("No se puede mostrar la ruta", ex.Message, "OK");
        }
        catch (MapaNoDisponibleException ex)
        {
            RegistroErrores.RegistrarError(ex, "Ver ruta");
            await DisplayAlertAsync("Mapa no disponible", ex.Message, "OK");
        }
    }

    /// <summary>Registra la excepción como aviso y se la muestra al usuario.</summary>
    private async Task MostrarProblemaAsync(string titulo, Exception ex)
    {
        RegistroErrores.RegistrarAviso(ex, "Anotarse a un viaje");
        await DisplayAlertAsync(titulo, ex.Message, "OK");
    }
}
