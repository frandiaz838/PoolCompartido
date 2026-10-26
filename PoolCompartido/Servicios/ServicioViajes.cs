using System.Text.Json;

namespace PoolCompartido;

/// <summary>
/// Lógica del negocio: publicar viajes, anotarse, cancelar, sincronizar con Firebase.
/// No sabe nada de pantallas: valida, lanza excepciones si algo está mal
/// y dispara eventos para que las pantallas reaccionen.
/// </summary>
public class ServicioViajes
{
    private const int ReintentosPorConflicto = 3;

    /// <summary>Única instancia compartida por toda la app.</summary>
    public static ServicioViajes Instancia { get; } = new();

    /// <summary>Bucle despachador con la tabla de eventos de los viajes.</summary>
    public Despachador<ViajeEvento, Viaje> Eventos { get; private set; } = new();

    /// <summary>Evento estándar de .NET: avisa cuando se pierde o se recupera la conexión.</summary>
    public event EventHandler<bool>? ConexionCambiada;

    public bool HayConexion { get; private set; } = true;

    /// <summary>Mensaje para mostrar al entrar si hubo un problema al cargar.</summary>
    public string? AvisoAlIniciar { get; private set; }

    private readonly List<Viaje> _viajes = new();
    public IReadOnlyList<Viaje> Viajes => _viajes;

    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());
    private readonly RepositorioViajes _cache = new(Path.Combine(Configuracion.CarpetaDatos, "viajes_cache.json"));
    public string RutaDatos => _cache.Ruta;

    private bool _sincronizando;

    private ServicioViajes() { }

    private static Usuario UsuarioRequerido =>
        ServicioAutenticacion.Instancia.UsuarioActual ?? throw new SesionVencidaException();

    // ───────────────────────── Carga y sincronización (HU13) ─────────────────────────

    /// <summary>Carga inicial al entrar. Sin internet, usa los últimos viajes guardados en el dispositivo.</summary>
    /// <exception cref="PermisoDenegadoException">Firebase rechazó la lectura (revisar reglas).</exception>
    public async Task CargarAsync()
    {
        _viajes.Clear();
        AvisoAlIniciar = null;

        try
        {
            var remotos = await _remoto.ObtenerTodosAsync();
            AgregarDesde(remotos);
            CambiarConexion(true);
            GuardarCache();
        }
        catch (SinConexionException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Cargar viajes");
            AgregarDesde(LeerCache());
            HayConexion = false;
            AvisoAlIniciar = "Sin conexión: se muestran los últimos viajes guardados en este dispositivo.";
        }
    }

    /// <summary>
    /// Se llama cada pocos segundos (timer). Compara con lo que hay en Firebase y dispara
    /// los mismos eventos que si el cambio se hubiera hecho en este dispositivo:
    /// así, cuando otra persona se anota a tu viaje, te llega el aviso.
    /// </summary>
    public async Task SincronizarAsync()
    {
        if (_sincronizando) return;
        _sincronizando = true;

        try
        {
            var remotos = await _remoto.ObtenerTodosAsync();
            CambiarConexion(true);

            var eventos = new List<(ViajeEvento Evento, Viaje Viaje)>();
            foreach (var (id, datos) in remotos)
            {
                var local = _viajes.FirstOrDefault(v => v.Id == id);
                if (local is null)
                {
                    if (CrearDesde(id, datos) is Viaje nuevo)
                    {
                        _viajes.Add(nuevo);
                        eventos.Add((ViajeEvento.VIAJE_PUBLICADO, nuevo));
                    }
                    continue;
                }

                int pasajerosAntes = local.PasajerosPorUid.Count;
                bool estabaCancelado = local.Cancelado;
                local.ActualizarDesde(datos);

                if (!estabaCancelado && local.Cancelado)
                    eventos.Add((ViajeEvento.VIAJE_CANCELADO, local));
                else if (local.PasajerosPorUid.Count > pasajerosAntes)
                    eventos.Add((ViajeEvento.ASIENTO_RESERVADO, local));
                else if (local.PasajerosPorUid.Count < pasajerosAntes)
                    eventos.Add((ViajeEvento.ASIENTO_LIBERADO, local));
            }
            _viajes.RemoveAll(v => !remotos.ContainsKey(v.Id));

            // Primero se actualiza todo y después se despachan los eventos, en orden
            foreach (var (evento, viaje) in eventos)
                Eventos.Disparar(evento, viaje);

            GuardarCache();
        }
        catch (SinConexionException)
        {
            CambiarConexion(false);
        }
        finally
        {
            _sincronizando = false;
        }
    }

    /// <summary>Al cerrar sesión: borra los datos en memoria y las suscripciones de las pantallas viejas.</summary>
    public void Reiniciar()
    {
        _viajes.Clear();
        Eventos = new Despachador<ViajeEvento, Viaje>();
        ConexionCambiada = null;
        HayConexion = true;
        AvisoAlIniciar = null;
    }

    // ───────────────────────── Operaciones ─────────────────────────

    /// <summary>HU1: el usuario actual publica un viaje como conductor.</summary>
    /// <exception cref="ArgumentException">Faltan datos obligatorios.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fecha pasada o asientos fuera de rango.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<Viaje> PublicarAsync(string? origen, string? destino, DateTime fecha, string? hora, int asientos, string? comentario)
    {
        // Validación temprana de la entrada
        if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            throw new ArgumentException("Completá el origen y el destino.");

        if (string.IsNullOrWhiteSpace(hora))
            throw new ArgumentException("Elegí un horario de salida.");

        if (!FranjasHorarias.TryLeerHora(hora, out TimeSpan horaSalida))
            throw new ArgumentException("La hora de salida no es válida.");

        // innerException: null elige el constructor que deja el mensaje limpio
        if (fecha.Date < DateTime.Today)
            throw new ArgumentOutOfRangeException("La fecha del viaje no puede ser anterior a hoy.", innerException: null);

        if (fecha.Date == DateTime.Today && horaSalida < DateTime.Now.TimeOfDay)
            throw new ArgumentOutOfRangeException("Esa hora de hoy ya pasó. Elegí una hora más tarde.", innerException: null);

        if (asientos < 1 || asientos > 4)
            throw new ArgumentOutOfRangeException("Elegí entre 1 y 4 asientos disponibles.", innerException: null);

        var usuario = UsuarioRequerido;
        var datos = new ViajeDatos
        {
            Origen = origen.Trim(),
            Destino = destino.Trim(),
            Fecha = ConversorViajes.FechaATexto(fecha.Date),
            Hora = hora,
            ConductorUid = usuario.Uid,
            ConductorNombre = usuario.Nombre,
            Comentario = comentario?.Trim() ?? "",
            AsientosDisponibles = asientos,
            Cancelado = false
        };

        string id = await _remoto.CrearAsync(datos);
        var viaje = ConversorViajes.AViaje(id, datos);

        _viajes.Add(viaje);
        Eventos.Disparar(ViajeEvento.VIAJE_PUBLICADO, viaje);
        GuardarCache();
        return viaje;
    }

    /// <summary>
    /// HU3: el usuario actual se anota como pasajero.
    /// Si dos personas se anotan al mismo tiempo, Firebase acepta solo una (ETag) y la otra vuelve a intentar
    /// con los datos nuevos: si ya no quedan lugares, recibe ViajeCompletoException.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ViajeException">El estado del viaje no permite anotarse.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task AnotarseAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        var usuario = UsuarioRequerido;

        // Validación temprana con lo que ya sabemos
        ValidarAnotarse(viaje.Cancelado, viaje.ConductorUid, viaje.PasajerosPorUid.ContainsKey(usuario.Uid),
                        viaje.Fecha, viaje.AsientosDisponibles, usuario.Uid);

        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null)
                throw new ViajeCanceladoException();

            datos.Pasajeros ??= new Dictionary<string, string>();
            try
            {
                // Se vuelve a validar con los datos frescos: otra persona pudo anotarse recién
                ValidarAnotarse(datos.Cancelado, datos.ConductorUid, datos.Pasajeros.ContainsKey(usuario.Uid),
                                viaje.Fecha, datos.AsientosDisponibles, usuario.Uid);
            }
            catch (ViajeException)
            {
                viaje.ActualizarDesde(datos);   // la pantalla muestra el estado real
                throw;
            }

            datos.AsientosDisponibles--;
            datos.Pasajeros[usuario.Uid] = usuario.Nombre;

            if (await _remoto.ReemplazarSiNoCambioAsync(viaje.Id, datos, etag))
            {
                int pasajerosAntes = viaje.PasajerosPorUid.Count;
                viaje.ActualizarDesde(datos);
                if (viaje.PasajerosPorUid.Count > pasajerosAntes)
                    Eventos.Disparar(ViajeEvento.ASIENTO_RESERVADO, viaje);
                GuardarCache();
                return;
            }
            // Otra persona modificó el viaje entre que lo leímos y lo guardamos: se reintenta
        }

        throw new ConflictoException();
    }

    /// <summary>
    /// HU5: el conductor cancela su viaje. Dispara VIAJE_CANCELADO, y los pasajeros
    /// reciben el aviso en sus dispositivos en la próxima sincronización.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ViajeException">No es el conductor, o el viaje ya fue cancelado o ya pasó.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task CancelarViajeAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        var usuario = UsuarioRequerido;

        if (viaje.ConductorUid != usuario.Uid) throw new NoEsConductorException();
        if (viaje.Cancelado) throw new ViajeCanceladoException();
        if (viaje.Fecha.Date < DateTime.Today) throw new ViajeVencidoException();

        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null || datos.Cancelado)
                throw new ViajeCanceladoException();

            datos.Cancelado = true;

            if (await _remoto.ReemplazarSiNoCambioAsync(viaje.Id, datos, etag))
            {
                viaje.ActualizarDesde(datos);
                Eventos.Disparar(ViajeEvento.VIAJE_CANCELADO, viaje);
                GuardarCache();
                return;
            }
        }

        throw new ConflictoException();
    }

    /// <summary>
    /// HU4: el usuario actual se sale de un viaje en el que estaba anotado como pasajero.
    /// Se libera su asiento y se dispara ASIENTO_LIBERADO (el conductor recibe el aviso por la sincronización).
    /// No se busca reemplazo: eso solo pasa cuando cancela el conductor (HU6).
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ViajeException">No está anotado, o el viaje ya fue cancelado o ya pasó.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task SalirseAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        var usuario = UsuarioRequerido;

        // Validación temprana con lo que ya sabemos
        ValidarSalirse(viaje.Cancelado, viaje.PasajerosPorUid.ContainsKey(usuario.Uid), viaje.Fecha);

        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null)
                throw new ViajeCanceladoException();

            datos.Pasajeros ??= new Dictionary<string, string>();
            try
            {
                // Se vuelve a validar con los datos frescos: el conductor pudo cancelar recién
                ValidarSalirse(datos.Cancelado, datos.Pasajeros.ContainsKey(usuario.Uid), viaje.Fecha);
            }
            catch (ViajeException)
            {
                viaje.ActualizarDesde(datos);   // la pantalla muestra el estado real
                throw;
            }

            datos.AsientosDisponibles++;
            datos.Pasajeros.Remove(usuario.Uid);
            if (datos.Pasajeros.Count == 0)
                datos.Pasajeros = null;   // así no se guarda un "pasajeros" vacío en Firebase

            if (await _remoto.ReemplazarSiNoCambioAsync(viaje.Id, datos, etag))
            {
                int pasajerosAntes = viaje.PasajerosPorUid.Count;
                viaje.ActualizarDesde(datos);
                if (viaje.PasajerosPorUid.Count < pasajerosAntes)
                    Eventos.Disparar(ViajeEvento.ASIENTO_LIBERADO, viaje);
                GuardarCache();
                return;
            }
            // Otra persona modificó el viaje entre que lo leímos y lo guardamos: se reintenta
        }

        throw new ConflictoException();
    }

    /// <summary>
    /// HU21: antes de borrar la cuenta, cancela los viajes activos que publicó y se sale
    /// de los que está anotado, así nadie queda esperando a alguien que ya no existe.
    /// Los viajes viejos o ya cancelados quedan en Firebase como historial.
    /// </summary>
    /// <exception cref="ViajeException">No se pudo cancelar o salir de algún viaje (por ejemplo, conflicto).</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task DejarMisViajesAsync()
    {
        // Se copia la lista con ToList(): cancelar o salirse cambia los viajes mientras se recorren
        var activos = MisViajes().Where(v => !v.Cancelado && v.Fecha.Date >= DateTime.Today).ToList();

        foreach (var viaje in activos)
        {
            try
            {
                if (viaje.EsDelUsuarioActual)
                    await CancelarViajeAsync(viaje);
                else
                    await SalirseAsync(viaje);
            }
            catch (Exception ex) when (ex is ViajeCanceladoException or NoAnotadoException)
            {
                // Otro ya lo canceló o ya no estaba anotado: el viaje ya quedó como queríamos
                RegistroErrores.RegistrarAviso(ex, "Dejar viaje al borrar la cuenta");
            }
        }
    }

    /// <summary>HU21: borra la copia local de los viajes del dispositivo.</summary>
    public void BorrarDatosLocales()
    {
        try
        {
            File.Delete(_cache.Ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RegistroErrores.RegistrarError(ex, "Borrar caché de viajes");
        }
    }

    /// <summary>HU8: viajes en los que participa el usuario actual (como conductor o pasajero), por fecha.</summary>
    public IEnumerable<Viaje> MisViajes() =>
        _viajes.Where(v => v.EsDelUsuarioActual || v.EstoyAnotado)
               .OrderBy(v => v.Fecha)
               .ThenBy(v => v.Hora);

    // ───────────────────────── Auxiliares ─────────────────────────

    private static void ValidarAnotarse(bool cancelado, string conductorUid, bool yaAnotado,
                                        DateTime fecha, int asientos, string uid)
    {
        if (cancelado) throw new ViajeCanceladoException();
        if (conductorUid == uid) throw new AutoReservaException();
        if (yaAnotado) throw new YaAnotadoException();
        if (fecha.Date < DateTime.Today) throw new ViajeVencidoException();
        if (asientos <= 0) throw new ViajeCompletoException();
    }

    private static void ValidarSalirse(bool cancelado, bool anotado, DateTime fecha)
    {
        if (cancelado) throw new ViajeCanceladoException();
        if (!anotado) throw new NoAnotadoException();
        if (fecha.Date < DateTime.Today) throw new ViajeVencidoException();
    }

    private void CambiarConexion(bool hayConexion)
    {
        if (HayConexion == hayConexion) return;
        HayConexion = hayConexion;
        ConexionCambiada?.Invoke(this, hayConexion);
    }

    private void AgregarDesde(Dictionary<string, ViajeDatos> datos)
    {
        foreach (var (id, viajeDatos) in datos)
        {
            if (CrearDesde(id, viajeDatos) is Viaje viaje)
                _viajes.Add(viaje);
        }
    }

    /// <summary>Convierte los datos en un Viaje; si están dañados, lo saltea y lo registra.</summary>
    private static Viaje? CrearDesde(string id, ViajeDatos datos)
    {
        try
        {
            return ConversorViajes.AViaje(id, datos);
        }
        catch (FormatException ex)
        {
            RegistroErrores.RegistrarAviso(ex, $"Viaje {id} con datos inválidos");
            return null;
        }
    }

    private Dictionary<string, ViajeDatos> LeerCache()
    {
        try
        {
            return _cache.Cargar();
        }
        catch (IOException ex)
        {
            RegistroErrores.RegistrarError(ex, "Leer caché de viajes");
        }
        catch (JsonException ex)
        {
            RegistroErrores.RegistrarError(ex, "Leer caché de viajes (archivo dañado)");
        }
        return new Dictionary<string, ViajeDatos>();
    }

    private void GuardarCache()
    {
        try
        {
            _cache.Guardar(_viajes.ToDictionary(v => v.Id, ConversorViajes.ADatos));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Los datos ya están en Firebase: solo falla la copia local
            RegistroErrores.RegistrarError(ex, "Guardar caché de viajes");
        }
    }
}
