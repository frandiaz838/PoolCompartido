using System.Text.Json;

namespace PoolCompartido;

/// <summary>
/// HU25: chat grupal de cada viaje (el conductor y los pasajeros anotados).
/// Los mensajes se guardan en Firebase en /chats/{viajeId}/{clave}; las reglas solo dejan
/// leer y escribir a quienes participan del viaje.
/// Cada pocos segundos (con el timer de la app) se buscan mensajes nuevos: si llegan y el chat
/// no está abierto, se dispara MENSAJE_RECIBIDO en el despachador y aparece el aviso.
/// </summary>
public class ServicioChat
{
    public const int LargoMaximo = 500;

    public static ServicioChat Instancia { get; } = new();

    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());

    // Último mensaje que ya se trajo de cada viaje (para pedir solo los nuevos)
    private readonly Dictionary<string, string> _ultimoTraido = new();

    // Último mensaje leído de cada viaje: se guarda en el dispositivo para que no vuelvan
    // a aparecer como nuevos al abrir la app otra vez
    private Dictionary<string, string>? _ultimoLeido;

    private bool _revisando;

    /// <summary>Viaje cuyo chat está abierto en pantalla (sus mensajes no generan aviso).</summary>
    public string? ChatAbierto { get; set; }

    private ServicioChat() { }

    private static string RutaLeidos => Path.Combine(Configuracion.CarpetaDatos, "chats_leidos.json");

    private static Usuario UsuarioRequerido =>
        ServicioAutenticacion.Instancia.UsuarioActual ?? throw new SesionVencidaException();

    /// <summary>Participa del viaje: es el conductor o está anotado.</summary>
    public static bool Participa(Viaje viaje) => viaje.EsDelUsuarioActual || viaje.EstoyAnotado;

    /// <summary>Envía un mensaje al grupo del viaje.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ArgumentException">El mensaje está vacío o es demasiado largo.</exception>
    /// <exception cref="NoAnotadoException">No participa del viaje.</exception>
    /// <exception cref="ViajeVencidoException">El viaje ya terminó.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task EnviarAsync(Viaje viaje, string? texto)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("Escribí un mensaje.");

        string limpio = texto.Trim();
        if (limpio.Length > LargoMaximo)
            throw new ArgumentException($"El mensaje puede tener hasta {LargoMaximo} caracteres.");

        await EscribirAsync(viaje, limpio, sistema: false);
    }

    /// <summary>
    /// Mensaje automático del sistema ("Ana se sumó al viaje"). Lo escribe la app de quien hizo
    /// la acción. Si falla no se avisa nada: la acción principal ya se hizo.
    /// </summary>
    public async Task AvisarAlGrupoAsync(Viaje viaje, string texto)
    {
        try
        {
            await EscribirAsync(viaje, texto, sistema: true);
        }
        catch (Exception ex) when (ex is ViajeException or SinConexionException or PermisoDenegadoException
                                   or ServidorException or SesionVencidaException)
        {
            RegistroErrores.RegistrarAviso(ex, "Mensaje automático del chat");
        }
    }

    private async Task EscribirAsync(Viaje viaje, string texto, bool sistema)
    {
        var usuario = UsuarioRequerido;
        if (!Participa(viaje))
            throw new NoAnotadoException();
        if (viaje.Terminado)
            throw new ViajeVencidoException();

        await _remoto.CrearMensajeAsync(viaje.Id, new MensajeNuevoDatos
        {
            AutorUid = usuario.Uid,
            AutorNombre = usuario.Nombre,
            Texto = texto,
            Sistema = sistema ? true : null
        });
    }

    /// <summary>Los mensajes del viaje que llegaron después de <paramref name="desdeClave"/> (o todos).</summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="PermisoDenegadoException">Ya no participa del viaje.</exception>
    public async Task<IReadOnlyList<MensajeChat>> ObtenerAsync(Viaje viaje, string? desdeClave = null)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        var datos = await _remoto.ObtenerMensajesAsync(viaje.Id, desdeClave);

        return datos
            .Where(m => m.Key != desdeClave)          // startAt incluye el último que ya teníamos
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .Select(m => new MensajeChat(m.Key, m.Value.AutorUid, m.Value.AutorNombre, m.Value.Texto,
                                         DateTimeOffset.FromUnixTimeMilliseconds(m.Value.Hora).LocalDateTime,
                                         m.Value.Sistema == true))
            .ToList();
    }

    /// <summary>Se abrió el chat: todo lo que hay hasta ese mensaje queda leído.</summary>
    public void MarcarLeido(Viaje viaje, string? ultimaClave)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        viaje.MensajesSinLeer = 0;
        if (ultimaClave is null) return;

        _ultimoTraido[viaje.Id] = ultimaClave;
        var leidos = CargarLeidos();
        leidos[viaje.Id] = ultimaClave;
        GuardarLeidos(leidos);
    }

    /// <summary>
    /// Lo llama el timer de la app: busca mensajes nuevos en los viajes donde participo.
    /// Por cada mensaje nuevo de otra persona (con el chat cerrado) dispara MENSAJE_RECIBIDO.
    /// </summary>
    public async Task RevisarNuevosAsync()
    {
        if (_revisando || ServicioAutenticacion.Instancia.UsuarioActual is null) return;
        _revisando = true;
        try
        {
            var leidos = CargarLeidos();
            var viajes = ServicioViajes.Instancia.MisViajes().Where(v => !v.Terminado && !v.Cancelado).ToList();

            foreach (var viaje in viajes)
            {
                string? desde = _ultimoTraido.TryGetValue(viaje.Id, out var t) ? t
                              : leidos.TryGetValue(viaje.Id, out var l) ? l
                              : null;

                IReadOnlyList<MensajeChat> nuevos;
                try
                {
                    nuevos = await ObtenerAsync(viaje, desde);
                }
                catch (PermisoDenegadoException ex)
                {
                    // Se bajó del viaje hace instantes: ya no puede leer ese chat
                    RegistroErrores.RegistrarAviso(ex, "Revisar chat");
                    continue;
                }

                if (nuevos.Count == 0) continue;
                _ultimoTraido[viaje.Id] = nuevos[^1].Clave;

                if (ChatAbierto == viaje.Id) continue;   // la pantalla del chat los muestra sola

                var deOtros = nuevos.Where(m => !m.EsMio).ToList();
                if (deOtros.Count == 0) continue;

                viaje.MensajesSinLeer += deOtros.Count;
                viaje.UltimoMensaje = deOtros[^1];
                ServicioViajes.Instancia.Eventos.Disparar(ViajeEvento.MENSAJE_RECIBIDO, viaje);
            }
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            // Sin internet: se vuelve a probar en la próxima vuelta del timer
            RegistroErrores.RegistrarAviso(ex, "Revisar chat");
        }
        finally
        {
            _revisando = false;
        }
    }

    /// <summary>Al cerrar sesión: se olvida lo que se trajo (los leídos quedan guardados en el dispositivo).</summary>
    public void Reiniciar()
    {
        _ultimoTraido.Clear();
        _ultimoLeido = null;
        ChatAbierto = null;
    }

    private Dictionary<string, string> CargarLeidos()
    {
        if (_ultimoLeido is not null) return _ultimoLeido;
        try
        {
            if (File.Exists(RutaLeidos))
            {
                using var archivo = File.OpenRead(RutaLeidos);
                _ultimoLeido = JsonSerializer.Deserialize(archivo, JsonPool.Default.DictionaryStringString);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            RegistroErrores.RegistrarError(ex, "Leer chats leídos");
        }
        return _ultimoLeido ??= new Dictionary<string, string>();
    }

    private static void GuardarLeidos(Dictionary<string, string> leidos)
    {
        try
        {
            using var archivo = File.Create(RutaLeidos);
            JsonSerializer.Serialize(archivo, leidos, JsonPool.Default.DictionaryStringString);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RegistroErrores.RegistrarError(ex, "Guardar chats leídos");
        }
    }
}
