using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Evento estándar de .NET: algún viaje cambió de estado porque pasó el tiempo (cerró, salió o terminó).</summary>
    public event EventHandler? EstadosCambiados;

    public bool HayConexion { get; private set; } = true;

    /// <summary>Mensaje para mostrar al entrar si hubo un problema al cargar.</summary>
    public string? AvisoAlIniciar { get; private set; }

    private readonly List<Viaje> _viajes = new();
    public IReadOnlyList<Viaje> Viajes => _viajes;

    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());
    private readonly RepositorioViajes _cache = new(Path.Combine(Configuracion.CarpetaDatos, "viajes_cache.json"));
    public string RutaDatos => _cache.Ruta;

    private bool _sincronizando;

    private ServicioViajes()
    {
        RegistrarBusquedaDeReemplazo();
        RegistrarRecalculoDeCosto();
    }

    /// <summary>
    /// HU6 + HU11: el servicio también se suscribe a la tabla de eventos.
    /// Cuando cancelan un viaje donde estoy anotado, se busca otro parecido y se dispara
    /// un segundo evento encadenado: REEMPLAZO_ENCONTRADO o SIN_REEMPLAZO.
    /// El despachador lo encola y lo procesa cuando terminan las callbacks de VIAJE_CANCELADO.
    /// </summary>
    private void RegistrarBusquedaDeReemplazo()
    {
        Eventos.Registrar(ViajeEvento.VIAJE_CANCELADO, BuscarReemplazo);
    }

    private void BuscarReemplazo(Viaje cancelado)
    {
        // Solo le interesa al pasajero: el conductor fue el que canceló
        if (cancelado.EsDelUsuarioActual || !cancelado.EstoyAnotado) return;

        // Si ya tiene otro viaje a esa hora, no necesita reemplazo
        if (ViajeQueSeSuperpone(cancelado.Salida, exceptoId: cancelado.Id) is not null) return;

        // El reemplazo tampoco puede chocar con otro viaje suyo
        cancelado.Reemplazo = BuscadorDeReemplazo.Buscar(cancelado, _viajes, ServicioAutenticacion.UidActual,
            ademas: v => ViajeQueSeSuperpone(v.Salida, exceptoId: cancelado.Id) is null);

        var siguiente = cancelado.Reemplazo is null ? ViajeEvento.SIN_REEMPLAZO : ViajeEvento.REEMPLAZO_ENCONTRADO;
        Eventos.Disparar(siguiente, cancelado);
    }

    /// <summary>
    /// HU14 + HU11: cada vez que alguien se anota o se baja de un viaje con costo, cambia entre cuántos
    /// se divide. El servicio dispara un evento encadenado, COSTO_RECALCULADO, que el despachador
    /// procesa cuando terminan las callbacks de ASIENTO_RESERVADO o ASIENTO_LIBERADO.
    /// </summary>
    private void RegistrarRecalculoDeCosto()
    {
        Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, RecalcularCosto);
        Eventos.Registrar(ViajeEvento.ASIENTO_LIBERADO, RecalcularCosto);
    }

    private void RecalcularCosto(Viaje viaje)
    {
        // Los viajes sin costo no tienen nada que recalcular.
        // A quién le importa el aviso (conductor o pasajeros) lo decide ConversorMensajes
        if (viaje.TieneCosto)
            Eventos.Disparar(ViajeEvento.COSTO_RECALCULADO, viaje);
    }

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
                bool estabaIniciado = local.Iniciado;
                bool estabaFinalizado = local.Finalizado;
                int aBordoAntes = local.ABordo.Count;
                local.ActualizarDesde(datos);

                if (!estabaCancelado && local.Cancelado)
                    eventos.Add((ViajeEvento.VIAJE_CANCELADO, local));
                else if (local.PasajerosPorUid.Count > pasajerosAntes)
                    eventos.Add((ViajeEvento.ASIENTO_RESERVADO, local));
                else if (local.PasajerosPorUid.Count < pasajerosAntes)
                    eventos.Add((ViajeEvento.ASIENTO_LIBERADO, local));

                // HU15: lo que marcó el conductor en su dispositivo, en el orden en que pasa
                if (!estabaIniciado && local.Iniciado)
                    eventos.Add((ViajeEvento.VIAJE_INICIADO, local));
                if (local.ABordo.Count > aBordoAntes)
                    eventos.Add((ViajeEvento.PASAJERO_A_BORDO, local));
                if (!estabaFinalizado && local.Finalizado)
                    eventos.Add((ViajeEvento.VIAJE_FINALIZADO, local));
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
        RegistrarBusquedaDeReemplazo();   // la tabla nueva arranca vacía
        RegistrarRecalculoDeCosto();
        ConexionCambiada = null;
        EstadosCambiados = null;
        HayConexion = true;
        AvisoAlIniciar = null;
    }

    /// <summary>
    /// Se llama con el timer: revisa la hora de cada viaje y, si alguno cambió de estado
    /// (cerró las anotaciones, salió o terminó), dispara EstadosCambiados para que las pantallas se redibujen.
    /// </summary>
    public void ActualizarEstados()
    {
        bool huboCambios = false;
        foreach (var viaje in _viajes)
            huboCambios |= viaje.RefrescarEstado();

        if (huboCambios)
            EstadosCambiados?.Invoke(this, EventArgs.Empty);
    }

    // ───────────────────────── Operaciones ─────────────────────────

    /// <summary>HU1: el usuario actual publica un viaje como conductor.</summary>
    /// <exception cref="ArgumentException">Faltan datos obligatorios.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fecha pasada o asientos fuera de rango.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<Viaje> PublicarAsync(string? origen, string? destino, DateTime fecha, string? hora, int asientos, string? comentario,
                                           string? puntoPartida = null, decimal? costoTotal = null)
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

        DateTime salida = fecha.Date + horaSalida;
        if (salida <= DateTime.Now)
            throw new ArgumentOutOfRangeException("Esa hora de hoy ya pasó. Elegí una hora más tarde.", innerException: null);

        // Las anotaciones se cierran 5 minutos antes: con menos tiempo nadie llegaría a anotarse
        if (salida - DateTime.Now < Viaje.CierreAnotaciones)
            throw new ArgumentOutOfRangeException("El viaje tiene que salir dentro de más de 5 minutos, así los pasajeros llegan a anotarse.", innerException: null);

        if (asientos < 1 || asientos > 4)
            throw new ArgumentOutOfRangeException("Elegí entre 1 y 4 asientos disponibles.", innerException: null);

        // HU24: el punto de partida es opcional, pero si se escribe tiene que ser razonable
        string? partida = string.IsNullOrWhiteSpace(puntoPartida)
            ? null
            : ServicioEncuentros.ValidarTexto(puntoPartida, "");

        // HU14: el costo es opcional, pero si viene tiene que ser razonable
        ValidarCosto(costoTotal);

        // No puede manejar un viaje a la misma hora que otro donde ya participa
        if (ViajeQueSeSuperpone(salida) is Viaje otro)
            throw new HorarioOcupadoException(otro);

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
            Cancelado = false,
            PuntoPartida = partida,
            CostoTotal = costoTotal is > 0 ? costoTotal : null   // $0 es lo mismo que sin costo
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
        ValidarAnotarse(viaje.Cancelado, viaje.Iniciado, viaje.ConductorUid, viaje.PasajerosPorUid.ContainsKey(usuario.Uid),
                        viaje.Salida, viaje.AsientosDisponibles, usuario.Uid);

        // No puede estar en dos viajes al mismo tiempo
        if (ViajeQueSeSuperpone(viaje.Salida, exceptoId: viaje.Id) is Viaje otro)
            throw new HorarioOcupadoException(otro);

        // HU15: el PIN se guarda primero en /pines (solo lo lee el pasajero) y en el viaje va solo su hash.
        // Si anotarse falla después, queda un PIN suelto que no sirve para nada: no hace daño
        string pin = GenerarPin();
        await _remoto.GuardarPinAsync(usuario.Uid, viaje.Id, pin);

        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null)
                throw new ViajeCanceladoException();

            datos.Pasajeros ??= new Dictionary<string, string>();
            try
            {
                // Se vuelve a validar con los datos frescos: otra persona pudo anotarse recién
                ValidarAnotarse(datos.Cancelado, datos.Iniciado == true, datos.ConductorUid, datos.Pasajeros.ContainsKey(usuario.Uid),
                                viaje.Salida, datos.AsientosDisponibles, usuario.Uid);
            }
            catch (ViajeException)
            {
                viaje.ActualizarDesde(datos);   // la pantalla muestra el estado real
                throw;
            }

            datos.AsientosDisponibles--;
            datos.Pasajeros[usuario.Uid] = usuario.Nombre;
            datos.PinHash ??= new Dictionary<string, string>();
            datos.PinHash[usuario.Uid] = HashDelPin(viaje.Id, usuario.Uid, pin);   // HU15, en el mismo guardado

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
        if (DateTime.Now >= viaje.Salida || viaje.Iniciado) throw new ViajeVencidoException();   // HU15: ya arrancó

        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null || datos.Cancelado)
                throw new ViajeCanceladoException();
            if (datos.Iniciado == true)
                throw new ViajeVencidoException();

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
        ValidarSalirse(viaje.Cancelado, viaje.Iniciado, viaje.PasajerosPorUid.ContainsKey(usuario.Uid), viaje.Salida);

        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null)
                throw new ViajeCanceladoException();

            datos.Pasajeros ??= new Dictionary<string, string>();
            try
            {
                // Se vuelve a validar con los datos frescos: el conductor pudo cancelar recién
                ValidarSalirse(datos.Cancelado, datos.Iniciado == true, datos.Pasajeros.ContainsKey(usuario.Uid), viaje.Salida);
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

            // HU15: también se va el hash de su PIN
            datos.PinHash?.Remove(usuario.Uid);
            if (datos.PinHash?.Count == 0)
                datos.PinHash = null;

            if (await _remoto.ReemplazarSiNoCambioAsync(viaje.Id, datos, etag))
            {
                int pasajerosAntes = viaje.PasajerosPorUid.Count;
                viaje.ActualizarDesde(datos);
                if (viaje.PasajerosPorUid.Count < pasajerosAntes)
                    Eventos.Disparar(ViajeEvento.ASIENTO_LIBERADO, viaje);
                GuardarCache();
                await BorrarPinSinFallarAsync(usuario.Uid, viaje.Id);
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
        // Solo los que todavía no salieron (los en curso o terminados no se pueden modificar).
        // Se copia la lista con ToList(): cancelar o salirse cambia los viajes mientras se recorren
        var activos = MisViajes().Where(v => v.SePuedeModificar).ToList();

        foreach (var viaje in activos)
        {
            try
            {
                if (viaje.EsDelUsuarioActual)
                    await CancelarViajeAsync(viaje);
                else
                    await SalirseAsync(viaje);
            }
            catch (Exception ex) when (ex is ViajeCanceladoException or NoAnotadoException or ViajeVencidoException)
            {
                // Otro ya lo canceló, ya no estaba anotado o justo salió: no hay nada que dejar
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

    // ───────────────────────── HU15: check-in y ciclo de vida ─────────────────────────

    /// <summary>
    /// HU15: el conductor marca que arrancó (desde 15 minutos antes de la salida).
    /// Desde ese momento el viaje está "En curso" aunque no haya llegado la hora:
    /// ya no se anota, no se baja nadie y no se puede cancelar.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="InicioAnticipadoException">Faltan más de 15 minutos para salir.</exception>
    /// <exception cref="ViajeException">No es el conductor, o el viaje está cancelado, terminado o ya iniciado.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task IniciarViajeAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        ValidarConductor(viaje);

        if (viaje.Cancelado) throw new ViajeCanceladoException();
        if (viaje.Terminado) throw new ViajeException("Este viaje ya terminó.");
        if (viaje.Iniciado) throw new ViajeException("El viaje ya está en curso.");
        if (DateTime.Now < viaje.InicioPermitido) throw new InicioAnticipadoException(viaje.InicioPermitido);

        await ModificarComoConductorAsync(viaje, datos =>
        {
            if (datos.Cancelado) throw new ViajeCanceladoException();
            datos.Iniciado = true;
        });
        Eventos.Disparar(ViajeEvento.VIAJE_INICIADO, viaje);
    }

    /// <summary>HU15: el conductor marca que llegaron. El viaje pasa al historial.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ViajeNoIniciadoException">Todavía no tocó "Iniciar viaje".</exception>
    /// <exception cref="ViajeException">No es el conductor, o el viaje ya terminó.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task FinalizarViajeAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        ValidarConductor(viaje);

        if (viaje.Terminado) throw new ViajeException("Este viaje ya terminó.");
        if (!viaje.Iniciado) throw new ViajeNoIniciadoException();

        await ModificarComoConductorAsync(viaje, datos => datos.Finalizado = true);
        Eventos.Disparar(ViajeEvento.VIAJE_FINALIZADO, viaje);
    }

    /// <summary>
    /// HU15: check-in. El pasajero le dice su PIN al conductor, que lo escribe: la app busca
    /// a qué pasajero corresponde comparando hashes, lo marca a bordo y dispara PASAJERO_A_BORDO.
    /// </summary>
    /// <returns>El nombre del pasajero que subió.</returns>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="PinInvalidoException">No son 4 números, o no es el PIN de ningún pasajero que falte subir.</exception>
    /// <exception cref="ViajeNoIniciadoException">Todavía no tocó "Iniciar viaje".</exception>
    /// <exception cref="ViajeException">No es el conductor, o el viaje ya terminó.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<string> ConfirmarPinAsync(Viaje viaje, string? pin)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        ValidarConductor(viaje);

        if (viaje.Terminado) throw new ViajeException("Este viaje ya terminó.");
        if (!viaje.Iniciado) throw new ViajeNoIniciadoException();

        string limpio = pin?.Trim() ?? "";
        if (!EsPinValido(limpio))
            throw new PinInvalidoException("El PIN tiene que ser de 4 números.");

        string? uid = null;
        await ModificarComoConductorAsync(viaje, datos =>
        {
            // Se busca con los datos frescos: el pasajero pudo bajarse recién
            uid = PasajeroDelPin(viaje.Id, datos, limpio) ?? throw new PinInvalidoException();
            datos.ABordo ??= new Dictionary<string, bool>();
            datos.ABordo[uid] = true;
        });

        Eventos.Disparar(ViajeEvento.PASAJERO_A_BORDO, viaje);
        return viaje.PasajerosPorUid.GetValueOrDefault(uid!, "El pasajero");
    }

    /// <summary>HU15: el PIN del usuario actual para ese viaje. Lo lee de /pines, que solo puede leer él.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="NoAnotadoException">No está anotado en ese viaje.</exception>
    /// <exception cref="PinInvalidoException">Se anotó antes de que existiera el PIN.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<string> ObtenerMiPinAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        var usuario = UsuarioRequerido;

        if (!viaje.PasajerosPorUid.ContainsKey(usuario.Uid))
            throw new NoAnotadoException();

        return await _remoto.ObtenerPinAsync(usuario.Uid, viaje.Id)
            ?? throw new PinInvalidoException("No tenés PIN para este viaje porque te anotaste antes de que existiera. Salite y volvé a anotarte.");
    }

    /// <summary>PIN de 4 números al azar. RandomNumberGenerator, a diferencia de Random, no se puede predecir.</summary>
    internal static string GenerarPin() =>
        RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4", CultureInfo.InvariantCulture);

    internal static bool EsPinValido(string pin) => pin.Length == 4 && pin.All(char.IsAsciiDigit);

    /// <summary>
    /// Hash SHA-256 del PIN. Se mezcla con el viaje y el pasajero, así el mismo PIN
    /// da un hash distinto en cada viaje y el conductor no lo puede leer del viaje.
    /// </summary>
    internal static string HashDelPin(string viajeId, string uid, string pin) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{viajeId}:{uid}:{pin}")));

    /// <summary>El uid del pasajero que todavía no subió y tiene ese PIN, o null si no hay ninguno.</summary>
    private static string? PasajeroDelPin(string viajeId, ViajeDatos datos, string pin) =>
        datos.Pasajeros?.Keys.FirstOrDefault(uid =>
            datos.ABordo?.GetValueOrDefault(uid) != true
            && datos.PinHash?.GetValueOrDefault(uid) == HashDelPin(viajeId, uid, pin));

    /// <summary>Al salirse, el PIN ya no sirve. Si falla el borrado no pasa nada: el hash ya no está en el viaje.</summary>
    private async Task BorrarPinSinFallarAsync(string uid, string viajeId)
    {
        try
        {
            await _remoto.BorrarPinAsync(uid, viajeId);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarAviso(ex, "Borrar PIN al salirse");
        }
    }

    private static void ValidarConductor(Viaje viaje)
    {
        if (viaje.ConductorUid != UsuarioRequerido.Uid)
            throw new NoEsConductorException("Solo el conductor puede hacer esto.");
    }

    /// <summary>
    /// HU15: el conductor cambia su viaje con el mismo patrón ETag que anotarse o cancelar:
    /// lee, aplica el cambio y guarda solo si nadie lo modificó en el medio.
    /// </summary>
    private async Task ModificarComoConductorAsync(Viaje viaje, Action<ViajeDatos> cambiar)
    {
        for (int intento = 1; intento <= ReintentosPorConflicto; intento++)
        {
            var (datos, etag) = await _remoto.ObtenerConEtagAsync(viaje.Id);
            if (datos is null)
                throw new ViajeCanceladoException();

            try
            {
                cambiar(datos);
            }
            catch (ViajeException)
            {
                viaje.ActualizarDesde(datos);   // la pantalla muestra el estado real
                throw;
            }

            if (await _remoto.ReemplazarSiNoCambioAsync(viaje.Id, datos, etag))
            {
                viaje.ActualizarDesde(datos);
                GuardarCache();
                return;
            }
            // Un pasajero se bajó o se anotó entre que lo leímos y lo guardamos: se reintenta
        }

        throw new ConflictoException();
    }

    /// <summary>
    /// Separación mínima entre dos viajes de una misma persona.
    /// No sabemos cuánto dura cada viaje, así que se pide al menos una hora entre salidas.
    /// </summary>
    public static readonly TimeSpan SeparacionMinima = TimeSpan.FromHours(1);

    /// <summary>
    /// Otro viaje activo del usuario (como conductor o pasajero) que sale a menos de una hora
    /// de ese horario, o null si está libre. Usa la búsqueda genérica.
    /// </summary>
    public Viaje? ViajeQueSeSuperpone(DateTime salida, string? exceptoId = null) =>
        Busqueda.MasCercano(MisViajes(),
            sirve: v => v.Id != exceptoId && !v.Cancelado && !v.Terminado
                        && (v.Salida - salida).Duration() < SeparacionMinima,
            distancia: v => (v.Salida - salida).Duration());

    /// <summary>HU8: viajes en los que participa el usuario actual (como conductor o pasajero), por fecha.</summary>
    public IEnumerable<Viaje> MisViajes() =>
        _viajes.Where(v => v.EsDelUsuarioActual || v.EstoyAnotado)
               .OrderBy(v => v.Fecha)
               .ThenBy(v => v.Hora);

    // ───────────────────────── HU14: costo ─────────────────────────

    /// <summary>Tope del costo total: más que esto seguro es un error de tipeo.</summary>
    public const decimal CostoMaximo = 1_000_000;

    // Se escribe en pesos enteros, con o sin punto de miles: "6000" o "6.000"
    private static readonly NumberFormatInfo FormatoCosto = new() { NumberGroupSeparator = "." };

    /// <summary>
    /// HU14: convierte lo que escribió el conductor en el costo total. Vacío = viaje sin costo (null).
    /// </summary>
    /// <exception cref="FormatException">No es un número entero de pesos.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Es negativo o supera el máximo.</exception>
    public static decimal? LeerCosto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        // Se acepta que escriba el signo $ y espacios
        string limpio = texto.Replace("$", "").Trim();

        if (!decimal.TryParse(limpio, NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                              FormatoCosto, out decimal costo))
            throw new FormatException("El costo tiene que ser un número de pesos, sin centavos. Ej: 6000");

        ValidarCosto(costo);
        return costo;
    }

    /// <exception cref="ArgumentOutOfRangeException">El costo es negativo o supera el máximo.</exception>
    private static void ValidarCosto(decimal? costo)
    {
        if (costo < 0)
            throw new ArgumentOutOfRangeException("El costo no puede ser negativo.", innerException: null);
        if (costo > CostoMaximo)
            throw new ArgumentOutOfRangeException($"El costo no puede superar {Viaje.Pesos(CostoMaximo)}.", innerException: null);
    }

    // ───────────────────────── Auxiliares ─────────────────────────

    private static void ValidarAnotarse(bool cancelado, bool iniciado, string conductorUid, bool yaAnotado,
                                        DateTime salida, int asientos, string uid)
    {
        if (cancelado) throw new ViajeCanceladoException();
        if (iniciado) throw new ViajeVencidoException();   // HU15: el conductor ya arrancó
        if (conductorUid == uid) throw new AutoReservaException();
        if (yaAnotado) throw new YaAnotadoException();
        if (DateTime.Now >= salida) throw new ViajeVencidoException();
        if (DateTime.Now >= salida - Viaje.CierreAnotaciones) throw new AnotacionesCerradasException();
        if (asientos <= 0) throw new ViajeCompletoException();
    }

    private static void ValidarSalirse(bool cancelado, bool iniciado, bool anotado, DateTime salida)
    {
        if (cancelado) throw new ViajeCanceladoException();
        if (!anotado) throw new NoAnotadoException();
        if (DateTime.Now >= salida || iniciado) throw new ViajeVencidoException();   // HU15: o ya arrancó
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
