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
            .Where(v => destino == "" || v.Destino.Contains(destino, StringComparison.OrdinalIgnoreCase))
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
            await DisplayAlertAsync("Acción no permitida", ex.Message, "OK");
        }
        catch (ViajeCompletoException ex)
        {
            await DisplayAlertAsync("Sin lugar", ex.Message, "OK");
        }
        catch (ViajeException ex)
        {
            await DisplayAlertAsync("No disponible", ex.Message, "OK");
        }
    }
}
