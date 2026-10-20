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
        pickerHorario.ItemsSource = new[] { "Todos" }.Concat(ServicioViajes.Horarios).ToList();
        pickerHorario.SelectedIndex = 0;

        // Tabla de eventos: qué hace esta pantalla cuando ocurre cada evento (callbacks)
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_PUBLICADO, viaje =>
        {
            Redibujar();
            MostrarAviso($"Se publicó un viaje nuevo: {viaje.Ruta}, el {viaje.Fecha:dd/MM} a las {viaje.Hora}.");
        });

        // HU3: el conductor recibe una notificación (por ahora se simula en este mismo dispositivo)
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, viaje =>
            MostrarAviso($"Aviso para {viaje.Conductor}: un pasajero se anotó a tu viaje a {viaje.Destino}. Quedan {viaje.AsientosDisponibles} asientos."));

        // HU5: se avisa a todos los pasajeros anotados (simulado en este dispositivo)
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_CANCELADO, viaje =>
        {
            Redibujar();
            MostrarAviso(viaje.Pasajeros.Count == 0
                ? $"Se canceló el viaje {viaje.Ruta} del {viaje.Fecha:dd/MM}. No había pasajeros anotados."
                : $"Aviso para {string.Join(", ", viaje.Pasajeros)}: {viaje.Conductor} canceló el viaje {viaje.Ruta} del {viaje.Fecha:dd/MM} a las {viaje.Hora}.");
        });

        Redibujar();

        // Si hubo un problema al leer el archivo de viajes, avisamos
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
        string? horario = pickerHorario.SelectedIndex > 0 ? pickerHorario.SelectedItem as string : null;

        var filtrados = _servicio.Viajes
            .Where(v => !v.Cancelado)
            .Where(v => destino == "" ||v.Destino.Contains(destino, StringComparison.OrdinalIgnoreCase))
            .Where(v => horario == null || v.Hora == horario)
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

    private async void OnPublicarClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new PublicarViajePage());
    }

    private async void OnAnotarmeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje }) return;

        try
        {
            _servicio.Anotarse(viaje);
            await DisplayAlertAsync("Reserva confirmada", $"¡Te anotaste! Quedan {viaje.AsientosDisponibles} asientos.", "OK");
        }
        // Excepciones específicas primero, la más general al final
        catch (AutoReservaException ex)
        {
            await MostrarProblemaAsync("Acción no permitida", ex);
        }
        catch (ViajeCompletoException ex)
        {
            await MostrarProblemaAsync("Sin lugar", ex);
        }
        catch (ViajeException ex)
        {
            await MostrarProblemaAsync("No disponible", ex);
        }
        catch (PersistenciaException ex)
        {
            // Ya quedó registrada como ERROR en el servicio
            await DisplayAlertAsync("Atención", ex.Message, "OK");
        }
    }

    /// <summary>Registra la excepción como aviso y se la muestra al usuario.</summary>
    private async Task MostrarProblemaAsync(string titulo, Exception ex)
    {
        RegistroErrores.RegistrarAviso(ex, "Anotarse a un viaje");
        await DisplayAlertAsync(titulo, ex.Message, "OK");
    }
}
