namespace PoolCompartido;

public partial class RegistroErroresPage : ContentPage
{
    public RegistroErroresPage()
    {
        InitializeComponent();

        // HU26: cartel de avisos y campanita
        Notificaciones.Preparar(this);
    }

    // Se ejecuta cada vez que se muestra la pantalla (evento del ciclo de vida)
    protected override void OnAppearing()
    {
        base.OnAppearing();

        lblRutas.Text = $"Registro: {RegistroErrores.RutaArchivo}\nCopia local de los viajes: {ServicioViajes.Instancia.RutaDatos}";

        try
        {
            lblContenido.Text = RegistroErrores.LeerTodo();
        }
        // FileNotFoundException hereda de IOException: va primero la más específica
        catch (FileNotFoundException)
        {
            lblContenido.Text = "Todavía no hay nada registrado.";
        }
        catch (IOException ex)
        {
            lblContenido.Text = $"No se pudo leer el registro: {ex.Message}";
        }
    }
}
