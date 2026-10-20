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
        // cada vez que se publica, alguien se anota o se cancela, la lista se redibuja sola.
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_PUBLICADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_LIBERADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_CANCELADO, _ => Redibujar());

        Redibujar();
    }

    // Cada vez que se vuelve a esta pantalla, se redibuja (por si cambiaron los datos)
    protected override void OnAppearing()
    {
        base.OnAppearing();
        var usuario = ServicioAutenticacion.Instancia.UsuarioActual;
        lblSaludo.Text = usuario is null ? "Mis viajes" : $"Hola, {usuario.Nombre}";
        Redibujar();
    }

    private async void OnPerfilClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new PerfilPage());
    }

    /// <summary>HU5: el conductor cancela su viaje.</summary>
    private async void OnCancelarViajeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        string detalle = viaje.Pasajeros.Count == 0
            ? "No hay pasajeros anotados."
            : $"Se va a avisar a {viaje.Pasajeros.Count} pasajero(s): {string.Join(", ", viaje.Pasajeros)}.";

        bool confirmar = await DisplayAlertAsync("Cancelar viaje",
            $"¿Seguro que querés cancelar el viaje {viaje.Ruta} del {viaje.Fecha:dd/MM}? {detalle}",
            "Sí, cancelar", "No");
        if (!confirmar) return;

        boton.IsEnabled = false;
        try
        {
            await _servicio.CancelarViajeAsync(viaje);
            await DisplayAlertAsync("Viaje cancelado", "Listo, el viaje quedó cancelado y los pasajeros van a recibir el aviso.", "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Cancelar viaje");
            await DisplayAlertAsync("No se pudo cancelar", ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Cancelar viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Cancelar viaje");
            await DisplayAlertAsync("No se pudo cancelar", ex.Message, "OK");
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>HU4: el pasajero se sale del viaje y libera su asiento.</summary>
    private async void OnSalirmeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        bool confirmar = await DisplayAlertAsync("Salirme del viaje",
            $"¿Seguro que querés salirte del viaje de {viaje.Conductor} {viaje.Ruta} del {viaje.Fecha:dd/MM}? Tu lugar va a quedar libre.",
            "Sí, salirme", "No");
        if (!confirmar) return;

        boton.IsEnabled = false;   // evita que se toque dos veces mientras espera al servidor
        try
        {
            await _servicio.SalirseAsync(viaje);
            await DisplayAlertAsync("Listo", $"Ya no estás anotado en el viaje de {viaje.Conductor}.", "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Salirse de un viaje");
            await DisplayAlertAsync("No se pudo salir del viaje", ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Salirse de un viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Salirse de un viaje");
            await DisplayAlertAsync("No se pudo salir del viaje", ex.Message, "OK");
        }
        finally
        {
            boton.IsEnabled = true;
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
