namespace PoolCompartido;

/// <summary>HU17: perfil del usuario (se abre desde el ícono de arriba a la derecha).</summary>
public partial class PerfilPage : ContentPage
{
    private readonly ServicioAutenticacion _auth = ServicioAutenticacion.Instancia;

    public PerfilPage()
    {
        InitializeComponent();
    }

    // Cada vez que se muestra: datos del usuario y si ya verificó el mail
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MostrarDatos();

        lblVerificacion.Text = "Consultando estado del mail…";
        try
        {
            bool verificado = await _auth.MailVerificadoAsync();
            lblVerificacion.Text = verificado ? "✓ Mail verificado" : "Mail sin verificar";
            lblVerificacion.TextColor = verificado ? Color.FromArgb("#2E9E5B") : Color.FromArgb("#D9822B");
            btnReenviar.IsVisible = !verificado;
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Perfil");
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or AutenticacionException)
        {
            RegistroErrores.RegistrarAviso(ex, "Consultar verificación del mail");
            lblVerificacion.Text = "No se pudo consultar el estado del mail";
        }
    }

    private void MostrarDatos()
    {
        var usuario = _auth.UsuarioActual;
        if (usuario is null) return;

        lblNombre.Text = usuario.Nombre;
        lblEmail.Text = usuario.Email;
        lblIniciales.Text = Iniciales(usuario.Nombre);
        entryNombre.Text = usuario.Nombre;
    }

    /// <summary>"Francisco Diaz" → "FD"; "Male" → "M".</summary>
    private static string Iniciales(string nombre)
    {
        var partes = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(partes.Take(2).Select(p => char.ToUpper(p[0])));
    }

    private async void OnGuardarNombreClicked(object? sender, EventArgs e)
    {
        btnGuardarNombre.IsEnabled = false;
        try
        {
            await _auth.CambiarNombreAsync(entryNombre.Text);
            MostrarDatos();
            await DisplayAlertAsync("Listo", "Tu nombre quedó actualizado.", "OK");
        }
        // ArgumentOutOfRangeException hereda de ArgumentException: va primero la más específica
        catch (ArgumentOutOfRangeException ex)
        {
            await MostrarAvisoAsync("Revisá el nombre", ex);
        }
        catch (ArgumentException ex)
        {
            await MostrarAvisoAsync("Revisá el nombre", ex);
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Cambiar nombre");
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or AutenticacionException)
        {
            RegistroErrores.RegistrarError(ex, "Cambiar nombre");
            await DisplayAlertAsync("No se pudo guardar", ex.Message, "OK");
        }
        finally
        {
            btnGuardarNombre.IsEnabled = true;
        }
    }

    private async void OnCambiarContrasenaClicked(object? sender, EventArgs e)
    {
        var usuario = _auth.UsuarioActual;
        if (usuario is null) return;

        btnContrasena.IsEnabled = false;
        try
        {
            await _auth.EnviarMailCambioContrasenaAsync(usuario.Email);
            await DisplayAlertAsync("Revisá tu mail",
                $"Te mandamos un mail a {usuario.Email} con un link para elegir una contraseña nueva.", "OK");
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or AutenticacionException)
        {
            RegistroErrores.RegistrarError(ex, "Cambiar contraseña");
            await DisplayAlertAsync("No se pudo enviar", ex.Message, "OK");
        }
        finally
        {
            btnContrasena.IsEnabled = true;
        }
    }

    private async void OnReenviarClicked(object? sender, EventArgs e)
    {
        btnReenviar.IsEnabled = false;
        try
        {
            await _auth.ReenviarMailDeVerificacionAsync();
            await DisplayAlertAsync("Mail enviado", "Revisá tu bandeja de entrada (y la carpeta de spam).", "OK");
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Reenviar verificación");
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or AutenticacionException)
        {
            RegistroErrores.RegistrarError(ex, "Reenviar verificación");
            await DisplayAlertAsync("No se pudo enviar", ex.Message, "OK");
        }
        finally
        {
            btnReenviar.IsEnabled = true;
        }
    }

    private async void OnRegistroClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new RegistroErroresPage());
    }

    private async void OnCerrarSesionClicked(object? sender, EventArgs e)
    {
        bool confirmar = await DisplayAlertAsync("Cerrar sesión", "¿Querés cerrar la sesión?", "Sí", "No");
        if (confirmar)
            _auth.CerrarSesion();   // dispara SESION_CERRADA y la app vuelve al login
    }

    // ───────────────────────── Borrar cuenta (HU21) ─────────────────────────

    private void OnBorrarCuentaClicked(object? sender, EventArgs e)
    {
        panelBorrar.IsVisible = !panelBorrar.IsVisible;
        entryContrasenaBorrar.Text = "";
    }

    private void OnVerContrasenaClicked(object? sender, EventArgs e)
    {
        entryContrasenaBorrar.IsPassword = !entryContrasenaBorrar.IsPassword;
        bool visible = !entryContrasenaBorrar.IsPassword;
        btnVerContrasena.Source = visible ? "ojo_tachado.png" : "ojo.png";
        SemanticProperties.SetDescription(btnVerContrasena, visible ? "Ocultar contraseña" : "Mostrar contraseña");
    }

    /// <summary>
    /// Orden: confirmar la contraseña, dejar los viajes activos y recién ahí borrar la cuenta.
    /// Si algo falla en el medio, la cuenta NO se borra y se avisa.
    /// </summary>
    private async void OnConfirmarBorradoClicked(object? sender, EventArgs e)
    {
        bool confirmar = await DisplayAlertAsync("Borrar cuenta",
            "Tu cuenta se borra para siempre y no se puede recuperar. ¿Querés seguir?", "Borrar", "No");
        if (!confirmar) return;

        btnBorrarCuenta.IsEnabled = false;
        btnConfirmarBorrado.IsEnabled = false;
        try
        {
            await _auth.ConfirmarContrasenaAsync(entryContrasenaBorrar.Text);
            await ServicioViajes.Instancia.DejarMisViajesAsync();
            await _auth.BorrarCuentaAsync();   // dispara SESION_CERRADA y la app vuelve al login
            ServicioViajes.Instancia.BorrarDatosLocales();
        }
        catch (ArgumentException ex)
        {
            await MostrarAvisoAsync("Falta la contraseña", ex);
        }
        // Hereda de AutenticacionException: va antes que el catch general
        catch (CredencialesInvalidasException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Borrar cuenta");
            await DisplayAlertAsync("Contraseña incorrecta", "La contraseña no es correcta. Tu cuenta no se borró.", "OK");
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Borrar cuenta");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Borrar cuenta (dejar viajes)");
            await DisplayAlertAsync("No se pudo borrar la cuenta", $"{ex.Message} Tu cuenta no se borró.", "OK");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException or AutenticacionException)
        {
            RegistroErrores.RegistrarError(ex, "Borrar cuenta");
            await DisplayAlertAsync("No se pudo borrar la cuenta", $"{ex.Message} Tu cuenta no se borró.", "OK");
        }
        finally
        {
            btnBorrarCuenta.IsEnabled = true;
            btnConfirmarBorrado.IsEnabled = true;
        }
    }

    private async Task MostrarAvisoAsync(string titulo, Exception ex)
    {
        RegistroErrores.RegistrarAviso(ex, "Perfil");
        await DisplayAlertAsync(titulo, ex.Message, "OK");
    }
}
