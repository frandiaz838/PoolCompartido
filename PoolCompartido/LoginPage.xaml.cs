namespace PoolCompartido;

/// <summary>HU17: pantalla de inicio de sesión y registro.</summary>
public partial class LoginPage : ContentPage
{
    private readonly ServicioAutenticacion _auth = ServicioAutenticacion.Instancia;
    private bool _modoRegistro;
    private bool _yaIntentoRestaurar;

    public LoginPage()
    {
        InitializeComponent();
    }

    // Al abrir la app: si quedó una sesión guardada, se entra directo
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_yaIntentoRestaurar) return;
        _yaIntentoRestaurar = true;

        MostrarCargando(true);
        try
        {
            if (await _auth.RestaurarSesionAsync())
                await App.Actual.EntrarAsync();
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            MostrarError(ex, esFallaDelSistema: true);
        }
        finally
        {
            MostrarCargando(false);
        }
    }

    private async void OnPrincipalClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;
        MostrarCargando(true);
        try
        {
            if (_modoRegistro)
            {
                var usuario = await _auth.RegistrarseAsync(entryNombre.Text, entryEmail.Text, entryContrasena.Text);

                // Confirmación de que la cuenta se creó
                string detalle = _auth.MailDeVerificacionEnviado
                    ? $"Te mandamos un mail a {usuario.Email} para verificar tu cuenta (si no lo ves, revisá en spam)."
                    : "Ya podés publicar viajes y anotarte.";
                await DisplayAlertAsync("¡Cuenta creada!", $"¡Hola, {usuario.Nombre}! Tu cuenta quedó creada. {detalle}", "Entrar");
            }
            else
                await _auth.IniciarSesionAsync(entryEmail.Text, entryContrasena.Text);

            await App.Actual.EntrarAsync();
        }
        // Errores del usuario: específicos primero
        catch (CredencialesInvalidasException ex)
        {
            MostrarError(ex);
        }
        catch (UsuarioExistenteException ex)
        {
            MostrarError(ex);
        }
        catch (ArgumentException ex)
        {
            MostrarError(ex);
        }
        catch (AutenticacionException ex)
        {
            MostrarError(ex);
        }
        // Fallas del sistema
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            MostrarError(ex, esFallaDelSistema: true);
        }
        finally
        {
            MostrarCargando(false);
        }
    }

    /// <summary>Muestra u oculta la contraseña.</summary>
    private void OnVerContrasenaClicked(object? sender, EventArgs e)
    {
        entryContrasena.IsPassword = !entryContrasena.IsPassword;
        bool visible = !entryContrasena.IsPassword;
        btnVerContrasena.Source = visible ? "ojo_tachado.png" : "ojo.png";
        SemanticProperties.SetDescription(btnVerContrasena, visible ? "Ocultar contraseña" : "Mostrar contraseña");
    }

    private async void OnOlvideClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;
        MostrarCargando(true);
        try
        {
            await _auth.EnviarMailCambioContrasenaAsync(entryEmail.Text);
            await DisplayAlertAsync("Revisá tu mail",
                "Si hay una cuenta con ese mail, te llegó un link para elegir una contraseña nueva.", "OK");
        }
        catch (ArgumentException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Olvidé mi contraseña");
            lblError.Text = "Escribí tu mail arriba y volvé a tocar \"¿Olvidaste tu contraseña?\".";
            lblError.IsVisible = true;
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or AutenticacionException)
        {
            MostrarError(ex, esFallaDelSistema: true);
        }
        finally
        {
            MostrarCargando(false);
        }
    }

    private void OnCambiarModoClicked(object? sender, EventArgs e)
    {
        _modoRegistro = !_modoRegistro;
        btnOlvide.IsVisible = !_modoRegistro;
        lblNombre.IsVisible = entryNombre.IsVisible = _modoRegistro;
        lblSubtitulo.Text = _modoRegistro ? "Creá tu cuenta para empezar" : "Iniciá sesión para ver y compartir viajes";
        btnPrincipal.Text = _modoRegistro ? "Crear cuenta" : "Iniciar sesión";
        btnCambiarModo.Text = _modoRegistro ? "¿Ya tenés cuenta? Iniciá sesión" : "¿No tenés cuenta? Creá una";
        lblError.IsVisible = false;
    }

    private void MostrarCargando(bool cargando)
    {
        indicador.IsRunning = indicador.IsVisible = cargando;
        btnPrincipal.IsEnabled = btnCambiarModo.IsEnabled = btnOlvide.IsEnabled = !cargando;
    }

    private void MostrarError(Exception ex, bool esFallaDelSistema = false)
    {
        // Nunca se registra la contraseña: los mensajes de las excepciones no la incluyen
        if (esFallaDelSistema)
            RegistroErrores.RegistrarError(ex, "Inicio de sesión");
        else
            RegistroErrores.RegistrarAviso(ex, "Inicio de sesión");

        lblError.Text = ex.Message;
        lblError.IsVisible = true;
    }
}
