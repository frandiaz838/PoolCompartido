using System.Collections.ObjectModel;

namespace PoolCompartido;

public partial class MisViajesPage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;
    private readonly ObservableCollection<Viaje> _misViajes = new();

    public MisViajesPage()
    {
        InitializeComponent();

        listaMisViajes.ItemsSource = _misViajes;

        // Esta pantalla también se suscribe a la tabla de eventos:
        // cada vez que publicás, te anotás o cancelás, la lista se redibuja sola.
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_PUBLICADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_CANCELADO, _ => Redibujar());

        Redibujar();
    }

    // Cada vez que se vuelve a esta pantalla, se redibuja (por si cambiaron los datos)
    protected override void OnAppearing()
    {
        base.OnAppearing();
        Redibujar();
    }

    private async void OnRegistroClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegistroErroresPage());
    }

    /// <summary>HU5: el conductor cancela su viaje.</summary>
    private async void OnCancelarViajeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje }) return;

        string detalle = viaje.Pasajeros.Count == 0
            ? "No hay pasajeros anotados."
            : $"Se va a avisar a {viaje.Pasajeros.Count} pasajero(s): {string.Join(", ", viaje.Pasajeros)}.";

        bool confirmar = await DisplayAlertAsync("Cancelar viaje",
            $"¿Seguro que querés cancelar el viaje {viaje.Ruta} del {viaje.Fecha:dd/MM}? {detalle}",
            "Sí, cancelar", "No");
        if (!confirmar) return;

        try
        {
            _servicio.CancelarViaje(viaje);
            await DisplayAlertAsync("Viaje cancelado", "Listo, el viaje quedó cancelado y se avisó a los pasajeros.", "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Cancelar viaje");
            await DisplayAlertAsync("No se pudo cancelar", ex.Message, "OK");
        }
        catch (PersistenciaException ex)
        {
            // Ya quedó registrada como ERROR en el servicio
            await DisplayAlertAsync("Atención", ex.Message, "OK");
        }
    }

    /// <summary>Equivalente al evento Paint: vuelve a armar la lista.</summary>
    private void Redibujar()
    {
        _misViajes.Clear();
        foreach (var viaje in _servicio.MisViajes())
            _misViajes.Add(viaje);
    }
}
