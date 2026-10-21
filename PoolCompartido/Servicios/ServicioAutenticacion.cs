using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PoolCompartido;

/// <summary>
/// Registro, inicio y cierre de sesión con Firebase Authentication (HU17).
/// Las contraseñas las guarda Firebase de forma segura: la app nunca las almacena.
/// </summary>
public class ServicioAutenticacion
{
    private const string UrlCuentas = "https://identitytoolkit.googleapis.com/v1/accounts:";
    private const string UrlToken = "https://securetoken.googleapis.com/v1/token";
    private const int LargoMinimoContrasena = 6;

    public static ServicioAutenticacion Instancia { get; } = new();

    /// <summary>Uid del usuario que inició sesión (null si no hay sesión).</summary>
    public static string? UidActual => Instancia.UsuarioActual?.Uid;

    public Usuario? UsuarioActual { get; private set; }

    /// <summary>Si al registrarse se pudo mandar el mail de verificación.</summary>
    public bool MailDeVerificacionEnviado { get; private set; }

    /// <summary>El mismo despachador genérico de los viajes, ahora con otro tipo de evento y de dato.</summary>
    public Despachador<EventoSesion, Usuario> Eventos { get; } = new();

    // El idToken dura una hora; el refreshToken sirve para pedir uno nuevo sin volver a escribir la contraseña
    private string? _idToken;
    private string? _refreshToken;
    private DateTime _venceUtc = DateTime.MinValue;

    private static string RutaSesion => Path.Combine(Configuracion.CarpetaDatos, "sesion.json");

    private ServicioAutenticacion() { }

    /// <summary>Crea una cuenta nueva e inicia sesión.</summary>
    /// <exception cref="ArgumentException">Datos inválidos (nombre vacío, mail mal escrito, contraseña corta).</exception>
    /// <exception cref="UsuarioExistenteException">Ya hay una cuenta con ese mail.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<Usuario> RegistrarseAsync(string? nombre, string? email, string? contrasena)
    {
        // Validación temprana
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Escribí tu nombre, así los demás saben quién sos.");
        ValidarCredenciales(email, contrasena);

        var cuenta = await EnviarAsync($"{UrlCuentas}signUp",
            new PedidoAutenticacion { Email = email!.Trim(), Password = contrasena! },
            JsonPool.Default.PedidoAutenticacion, JsonPool.Default.RespuestaAutenticacion);

        // Firebase no recibe el nombre al registrarse: se guarda con un segundo pedido
        await EnviarAsync($"{UrlCuentas}update",
            new PedidoActualizarPerfil { IdToken = cuenta.IdToken, DisplayName = nombre.Trim() },
            JsonPool.Default.PedidoActualizarPerfil, JsonPool.Default.RespuestaAutenticacion);

        MailDeVerificacionEnviado = await EnviarMailDeVerificacionAsync(cuenta.IdToken);

        return Ingresar(cuenta, nombre.Trim());
    }

    /// <summary>
    /// Pide a Firebase que mande el mail para verificar la cuenta.
    /// Si falla, la cuenta queda creada igual: solo se registra el error.
    /// </summary>
    private static async Task<bool> EnviarMailDeVerificacionAsync(string idToken)
    {
        try
        {
            await EnviarAsync($"{UrlCuentas}sendOobCode",
                new PedidoCodigo { RequestType = "VERIFY_EMAIL", IdToken = idToken },
                JsonPool.Default.PedidoCodigo, JsonPool.Default.RespuestaAutenticacion);
            return true;
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or AutenticacionException)
        {
            RegistroErrores.RegistrarError(ex, "Enviar mail de verificación");
            return false;
        }
    }

    /// <summary>Inicia sesión con mail y contraseña.</summary>
    /// <exception cref="ArgumentException">Mail mal escrito o contraseña vacía.</exception>
    /// <exception cref="CredencialesInvalidasException">Mail o contraseña incorrectos.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<Usuario> IniciarSesionAsync(string? email, string? contrasena)
    {
        ValidarCredenciales(email, contrasena);

        var cuenta = await EnviarAsync($"{UrlCuentas}signInWithPassword",
            new PedidoAutenticacion { Email = email!.Trim(), Password = contrasena! },
            JsonPool.Default.PedidoAutenticacion, JsonPool.Default.RespuestaAutenticacion);

        return Ingresar(cuenta, cuenta.DisplayName);
    }

    /// <summary>
    /// Intenta entrar con la sesión guardada en el dispositivo.
    /// Sin internet entra igual (con los datos guardados) y renueva el token cuando vuelva la conexión.
    /// </summary>
    public async Task<bool> RestaurarSesionAsync()
    {
        SesionGuardada? guardada;
        try
        {
            if (!File.Exists(RutaSesion))
                return false;

            using (var flujo = new FileStream(RutaSesion, FileMode.Open, FileAccess.Read))
            {
                guardada = JsonSerializer.Deserialize(flujo, JsonPool.Default.SesionGuardada);
            }
        }
        catch (IOException ex)
        {
            RegistroErrores.RegistrarError(ex, "Leer sesión guardada");
            return false;
        }
        catch (JsonException ex)
        {
            RegistroErrores.RegistrarError(ex, "Leer sesión guardada (archivo dañado)");
            LimpiarSesion();
            return false;
        }

        if (guardada is null || string.IsNullOrEmpty(guardada.RefreshToken))
            return false;

        _refreshToken = guardada.RefreshToken;
        UsuarioActual = new Usuario(guardada.Uid, guardada.Nombre, guardada.Email);

        try
        {
            await RefrescarTokenAsync();
        }
        catch (SinConexionException ex)
        {
            // Sin internet: se entra igual y el token se renueva más tarde
            RegistroErrores.RegistrarAviso(ex, "Restaurar sesión");
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Restaurar sesión");
            LimpiarSesion();
            return false;
        }

        Eventos.Disparar(EventoSesion.SESION_INICIADA, UsuarioActual);
        return true;
    }

    /// <summary>Devuelve un token válido para hablar con la base de datos, renovándolo si hace falta.</summary>
    /// <exception cref="SesionVencidaException">La sesión ya no es válida (se cierra y se vuelve al login).</exception>
    /// <exception cref="SinConexionException">No hay internet para renovar el token.</exception>
    public async Task<string> ObtenerTokenAsync()
    {
        if (_idToken is null || DateTime.UtcNow >= _venceUtc.AddMinutes(-1))
        {
            try
            {
                await RefrescarTokenAsync();
            }
            catch (SesionVencidaException)
            {
                CerrarSesion();
                throw;
            }
        }
        return _idToken!;
    }

    // ───────────────────────── Perfil ─────────────────────────

    /// <summary>Consulta en Firebase si el usuario ya tocó el link del mail de verificación.</summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<bool> MailVerificadoAsync()
    {
        var respuesta = await EnviarAsync($"{UrlCuentas}lookup",
            new PedidoToken { IdToken = await ObtenerTokenAsync() },
            JsonPool.Default.PedidoToken, JsonPool.Default.RespuestaConsultaCuenta);
        return respuesta.Users?.FirstOrDefault()?.EmailVerified ?? false;
    }

    /// <summary>Vuelve a mandar el mail de verificación.</summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="AutenticacionException">Firebase no lo permitió (por ejemplo, demasiados intentos).</exception>
    public async Task ReenviarMailDeVerificacionAsync()
    {
        await EnviarAsync($"{UrlCuentas}sendOobCode",
            new PedidoCodigo { RequestType = "VERIFY_EMAIL", IdToken = await ObtenerTokenAsync() },
            JsonPool.Default.PedidoCodigo, JsonPool.Default.RespuestaAutenticacion);
    }

    /// <summary>
    /// Manda un mail con un link para elegir una contraseña nueva.
    /// Sirve tanto desde el perfil como desde "¿Olvidaste tu contraseña?".
    /// </summary>
    /// <exception cref="ArgumentException">El mail no es válido.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task EnviarMailCambioContrasenaAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || !email.Contains('.'))
            throw new ArgumentException("Escribí un mail válido.");

        try
        {
            await EnviarAsync($"{UrlCuentas}sendOobCode",
                new PedidoCodigo { RequestType = "PASSWORD_RESET", Email = email.Trim() },
                JsonPool.Default.PedidoCodigo, JsonPool.Default.RespuestaAutenticacion);
        }
        catch (CredencialesInvalidasException)
        {
            // No hay cuenta con ese mail. No se lo decimos a quien pregunta,
            // así nadie puede averiguar qué mails están registrados.
        }
    }

    /// <summary>Cambia el nombre que ven los demás.</summary>
    /// <exception cref="ArgumentException">Nombre vacío o demasiado largo.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task CambiarNombreAsync(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Escribí tu nombre.");
        if (nombre.Trim().Length > 40)
            throw new ArgumentOutOfRangeException("El nombre puede tener hasta 40 caracteres.", innerException: null);

        var usuario = UsuarioActual ?? throw new SesionVencidaException();
        await EnviarAsync($"{UrlCuentas}update",
            new PedidoActualizarPerfil { IdToken = await ObtenerTokenAsync(), DisplayName = nombre.Trim() },
            JsonPool.Default.PedidoActualizarPerfil, JsonPool.Default.RespuestaAutenticacion);

        UsuarioActual = usuario with { Nombre = nombre.Trim() };
        GuardarSesion();
    }

    // ───────────────────────── Borrar cuenta (HU21) ─────────────────────────

    /// <summary>
    /// Vuelve a pedir la contraseña antes de algo que no se puede deshacer, como borrar la cuenta.
    /// Además deja un token recién emitido: Firebase solo deja borrar la cuenta si el ingreso es reciente.
    /// </summary>
    /// <exception cref="ArgumentException">No escribió la contraseña.</exception>
    /// <exception cref="CredencialesInvalidasException">La contraseña es incorrecta.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task ConfirmarContrasenaAsync(string? contrasena)
    {
        var usuario = UsuarioActual ?? throw new SesionVencidaException();
        if (string.IsNullOrEmpty(contrasena))
            throw new ArgumentException("Escribí tu contraseña para confirmar.");

        var cuenta = await EnviarAsync($"{UrlCuentas}signInWithPassword",
            new PedidoAutenticacion { Email = usuario.Email, Password = contrasena },
            JsonPool.Default.PedidoAutenticacion, JsonPool.Default.RespuestaAutenticacion);

        // No se usa Ingresar(): no es un inicio de sesión nuevo, así que no se dispara SESION_INICIADA
        _idToken = cuenta.IdToken;
        _refreshToken = cuenta.RefreshToken;
        _venceUtc = DateTime.UtcNow.AddSeconds(LeerSegundos(cuenta.ExpiresIn));
        GuardarSesion();
    }

    /// <summary>
    /// Borra la cuenta de Firebase Authentication para siempre y cierra la sesión (SESION_CERRADA).
    /// Antes hay que llamar a ConfirmarContrasenaAsync y dejar los viajes activos.
    /// </summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="AutenticacionException">Firebase no lo permitió (por ejemplo, el ingreso no es reciente).</exception>
    public async Task BorrarCuentaAsync()
    {
        var usuario = UsuarioActual ?? throw new SesionVencidaException();

        await EnviarAsync($"{UrlCuentas}delete",
            new PedidoToken { IdToken = await ObtenerTokenAsync() },
            JsonPool.Default.PedidoToken, JsonPool.Default.RespuestaAutenticacion);

        // La cuenta ya no existe: se borra la sesión guardada y la app vuelve al login
        LimpiarSesion();
        Eventos.Disparar(EventoSesion.SESION_CERRADA, usuario);
    }

    /// <summary>Cierra la sesión y avisa con el evento SESION_CERRADA.</summary>
    public void CerrarSesion()
    {
        var usuario = UsuarioActual;
        LimpiarSesion();
        if (usuario is not null)
            Eventos.Disparar(EventoSesion.SESION_CERRADA, usuario);
    }

    private static void ValidarCredenciales(string? email, string? contrasena)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || !email.Contains('.'))
            throw new ArgumentException("Escribí un mail válido.");
        if (string.IsNullOrEmpty(contrasena) || contrasena.Length < LargoMinimoContrasena)
            throw new ArgumentException($"La contraseña tiene que tener al menos {LargoMinimoContrasena} caracteres.");
    }

    private Usuario Ingresar(RespuestaAutenticacion cuenta, string? nombre)
    {
        _idToken = cuenta.IdToken;
        _refreshToken = cuenta.RefreshToken;
        _venceUtc = DateTime.UtcNow.AddSeconds(LeerSegundos(cuenta.ExpiresIn));

        string nombreVisible = string.IsNullOrWhiteSpace(nombre) ? cuenta.Email.Split('@')[0] : nombre;
        UsuarioActual = new Usuario(cuenta.LocalId, nombreVisible, cuenta.Email);

        GuardarSesion();
        Eventos.Disparar(EventoSesion.SESION_INICIADA, UsuarioActual);
        return UsuarioActual;
    }

    /// <exception cref="SesionVencidaException">El refreshToken ya no sirve.</exception>
    private async Task RefrescarTokenAsync()
    {
        if (_refreshToken is null)
            throw new SesionVencidaException();

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{UrlToken}?key={Configuracion.FirebaseApiKey}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken
            })
        };
        using var respuesta = await ClienteHttp.EnviarAsync(pedido);
        string cuerpo = await respuesta.Content.ReadAsStringAsync();

        if (respuesta.StatusCode == HttpStatusCode.BadRequest)
            throw new SesionVencidaException();
        if (!respuesta.IsSuccessStatusCode)
            throw new ServidorException($"código {(int)respuesta.StatusCode}");

        var nuevo = JsonSerializer.Deserialize(cuerpo, JsonPool.Default.RespuestaRefresco)
                    ?? throw new ServidorException("respuesta vacía");
        _idToken = nuevo.IdToken;
        _refreshToken = nuevo.RefreshToken;
        _venceUtc = DateTime.UtcNow.AddSeconds(LeerSegundos(nuevo.ExpiresIn));
        GuardarSesion();
    }

    /// <summary>
    /// Envía un pedido JSON a Firebase Authentication y convierte la respuesta.
    /// Es genérico: sirve para cualquier tipo de pedido y de respuesta.
    /// </summary>
    private static async Task<TRespuesta> EnviarAsync<TPedido, TRespuesta>(
        string url, TPedido datos, JsonTypeInfo<TPedido> tipoPedido, JsonTypeInfo<TRespuesta> tipoRespuesta)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{url}?key={Configuracion.FirebaseApiKey}")
        {
            Content = new StringContent(JsonSerializer.Serialize(datos, tipoPedido), Encoding.UTF8, "application/json")
        };
        using var respuesta = await ClienteHttp.EnviarAsync(pedido);
        string cuerpo = await respuesta.Content.ReadAsStringAsync();

        if (!respuesta.IsSuccessStatusCode)
            throw TraducirError(cuerpo, respuesta.StatusCode);

        return JsonSerializer.Deserialize(cuerpo, tipoRespuesta)
               ?? throw new ServidorException("respuesta vacía");
    }

    /// <summary>Convierte los códigos de error de Firebase en excepciones con mensajes claros.</summary>
    private static Exception TraducirError(string cuerpo, HttpStatusCode estado)
    {
        string codigo;
        try
        {
            codigo = JsonSerializer.Deserialize(cuerpo, JsonPool.Default.RespuestaErrorAuth)?.Error?.Message ?? "";
        }
        catch (JsonException)
        {
            codigo = "";
        }

        // Algunos códigos vienen con detalle: "WEAK_PASSWORD : Password should be..."
        codigo = codigo.Split(' ', ':')[0];

        return codigo switch
        {
            "EMAIL_EXISTS" => new UsuarioExistenteException(),
            "INVALID_LOGIN_CREDENTIALS" or "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" => new CredencialesInvalidasException(),
            "INVALID_EMAIL" => new ArgumentException("Escribí un mail válido."),
            "WEAK_PASSWORD" => new ArgumentException($"La contraseña tiene que tener al menos {LargoMinimoContrasena} caracteres."),
            "USER_DISABLED" => new AutenticacionException("Esta cuenta está deshabilitada."),
            "TOO_MANY_ATTEMPTS_TRY_LATER" => new AutenticacionException("Demasiados intentos. Esperá unos minutos y probá de nuevo."),
            "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" => new AutenticacionException("Por seguridad, volvé a escribir tu contraseña y probá de nuevo."),
            "OPERATION_NOT_ALLOWED" => new AutenticacionException("El ingreso con mail y contraseña no está activado en Firebase."),
            _ => new ServidorException(string.IsNullOrEmpty(codigo) ? $"código {(int)estado}" : codigo)
        };
    }

    private static int LeerSegundos(string texto) =>
        int.TryParse(texto, out int segundos) ? segundos : 3600;

    private void GuardarSesion()
    {
        if (UsuarioActual is null || _refreshToken is null)
            return;

        var datos = new SesionGuardada
        {
            Uid = UsuarioActual.Uid,
            Nombre = UsuarioActual.Nombre,
            Email = UsuarioActual.Email,
            RefreshToken = _refreshToken
        };

        try
        {
            using (var flujo = new FileStream(RutaSesion, FileMode.Create, FileAccess.Write))
            {
                JsonSerializer.Serialize(flujo, datos, JsonPool.Default.SesionGuardada);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La sesión funciona igual; solo no se va a recordar al cerrar la app
            RegistroErrores.RegistrarError(ex, "Guardar sesión");
        }
    }

    private void LimpiarSesion()
    {
        _idToken = null;
        _refreshToken = null;
        _venceUtc = DateTime.MinValue;
        UsuarioActual = null;

        try
        {
            File.Delete(RutaSesion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RegistroErrores.RegistrarError(ex, "Borrar sesión guardada");
        }
    }
}
