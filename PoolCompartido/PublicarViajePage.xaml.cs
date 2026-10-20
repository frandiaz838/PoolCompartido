namespace PoolCompartido;

public partial class PublicarViajePage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;

    public PublicarViajePage()
    {
        InitializeComponent();

        dpFecha.MinimumDate = DateTime.Today;
        pickerHora.ItemsSource = ServicioViajes.Horarios;
        pickerAsientos.ItemsSource = new[] { "1", "2", "3", "4" };
    }

    private async void OnConfirmarClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;

        try
        {
            // Si no eligió nada queda en 0 y el servicio lo rechaza
            int asientos = pickerAsientos.SelectedItem is string texto
                ? int.Parse(texto)   // puede lanzar FormatException
                : 0;

            DateTime fecha = ((DateTime?)dpFecha.Date) ?? DateTime.Today;

            _servicio.Publicar(entryOrigen.Text, entryDestino.Text, fecha,
                               pickerHora.SelectedItem as string, asientos, editorComentario.Text);

            await DisplayAlertAsync("Listo", "¡Viaje publicado con éxito!", "OK");
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
            MostrarError(ex, "La cantidad de asientos no es un número válido.");
        }
        catch (PersistenciaException ex)
        {
            // El viaje se publicó igual; solo falló el guardado (ya registrado como ERROR)
            await DisplayAlertAsync("Atención", ex.Message, "OK");
            await Navigation.PopAsync();
        }
    }

    private void MostrarError(Exception ex, string mensaje)
    {
        RegistroErrores.RegistrarAviso(ex, "Publicar viaje");
        lblError.Text = mensaje;
        lblError.IsVisible = true;
    }
}
