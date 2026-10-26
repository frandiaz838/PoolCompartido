namespace PoolCompartido;

public partial class PublicarViajePage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;

    public PublicarViajePage()
    {
        InitializeComponent();

        dpFecha.MinimumDate = DateTime.Today;
        // Por defecto, la próxima hora en punto
        int proximaHora = Math.Min(DateTime.Now.Hour + 1, 23);
        tpHora.Time = new TimeSpan(proximaHora, 0, 0);
        pickerAsientos.ItemsSource = new[] { "1", "2", "3", "4" };
    }

    private async void OnConfirmarClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;
        var boton = (Button)sender!;
        boton.IsEnabled = false;   // evita publicar dos veces mientras espera al servidor

        try
        {
            // Si no eligió nada queda en 0 y el servicio lo rechaza
            int asientos = pickerAsientos.SelectedItem is string texto
                ? int.Parse(texto)   // puede lanzar FormatException
                : 0;

            DateTime fecha = ((DateTime?)dpFecha.Date) ?? DateTime.Today;
            TimeSpan hora = ((TimeSpan?)tpHora.Time) ?? TimeSpan.Zero;

            // HU14: vacío = sin costo. Puede lanzar FormatException o ArgumentOutOfRangeException
            decimal? costo = ServicioViajes.LeerCosto(entryCosto.Text);

            await _servicio.PublicarAsync(entryOrigen.Text, entryDestino.Text, fecha,
                                          FranjasHorarias.HoraATexto(hora), asientos, editorComentario.Text, costo);

            await DisplayAlertAsync("Listo", "¡Viaje publicado! Ya lo pueden ver los demás.", "OK");
            await Navigation.PopAsync();
        }
        // ArgumentOutOfRangeException hereda de ArgumentException: va primero la más específica
        catch (ArgumentOutOfRangeException ex)
        {
            MostrarError(ex, ex.Message);
        }
        catch (ArgumentException ex)
        {
            MostrarError(ex, ex.Message);
        }
        catch (FormatException ex)
        {
            // Viene del costo (HU14), que trae su propio mensaje; los asientos salen de una lista fija
            MostrarError(ex, ex.Message);
        }
        catch (HorarioOcupadoException ex)
        {
            // Ya maneja o viaja en otro viaje a esa hora
            MostrarError(ex, ex.Message);
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Publicar viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Publicar viaje");
            lblError.Text = ex.Message;
            lblError.IsVisible = true;
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    private void MostrarError(Exception ex, string mensaje)
    {
        RegistroErrores.RegistrarAviso(ex, "Publicar viaje");
        lblError.Text = mensaje;
        lblError.IsVisible = true;
    }
}
