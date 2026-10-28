using System.Text.Json;

namespace PoolCompartido;

/// <summary>
/// HU31: calificaciones después del viaje.
/// Cada calificación se guarda en Firebase en /calificaciones/{calificadoUid}/{viajeId}_{autorUid}:
/// la clave hace que haya una sola por viaje y por autor, y las reglas no dejan modificarla.
/// Con el timer de la app se traen todas: así se calcula el promedio de cada persona y,
/// si me llegó una nueva, se dispara CALIFICACION_RECIBIDA en el despachador de viajes.
/// </summary>
public class ServicioCalificaciones
{
    public const int EstrellasMinimas = 1;
    public const int EstrellasMaximas = 5;
    public const int ComentarioMaximo = 200;

    public static ServicioCalificaciones Instancia { get; } = new();

    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());

    // Calificado → sus calificaciones (lo último que se trajo de Firebase)
    private Dictionary<string, List<Calificacion>> _porCalificado = new();

    // Claves de las calificaciones que ya me llegaron y ya se avisaron (se guardan en el dispositivo)
    private HashSet<string>? _yaAvisadas;

    private bool _revisando;

    private ServicioCalificaciones() { }

    private static string? RutaAvisadas =>
        ServicioAutenticacion.UidActual is string uid
            ? Path.Combine(Configuracion.CarpetaDatos, $"calificaciones_{uid}.json")
            : null;

    private static Usuario UsuarioRequerido =>
        ServicioAutenticacion.Instancia.UsuarioActual ?? throw new SesionVencidaException();

    /// <summary>Las calificaciones que recibió una persona.</summary>
    public IReadOnlyList<Calificacion> DeLaPersona(string uid) =>
        _porCalificado.TryGetValue(uid, out var lista) ? lista : Array.Empty<Calificacion>();

    /// <summary>"★ 4,6 (12)" o "Nuevo". Usa el método genérico Estadisticas.Promedio.</summary>
    public string TextoDe(string uid)
    {
        var lista = DeLaPersona(uid);
        return Estadisticas.TextoCalificacion(Estadisticas.Promedio(lista, c => c.Estrellas), lista.Count);
    }

    /// <summary>Con quiénes viajé en ese viaje y todavía no califiqué (vacío si no viajé o no está finalizado).</summary>
    public IReadOnlyList<PersonaACalificar> PorCalificar(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        if (ServicioAutenticacion.UidActual is not string yo || !viaje.Finalizado || !viaje.YoViaje)
            return Array.Empty<PersonaACalificar>();

        string clave = Calificacion.ClaveDe(viaje.Id, yo);
        return viaje.QuienesViajaron
                    .Where(p => p.Uid != yo && !DeLaPersona(p.Uid).Any(c => c.Clave == clave))
                    .ToList();
    }

    /// <summary>
    /// Califica a una persona con la que viajé.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje o la persona.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Las estrellas no están entre 1 y 5.</exception>
    /// <exception cref="ArgumentException">El comentario es demasiado largo, o se quiere calificar a sí mismo.</exception>
    /// <exception cref="ViajeNoFinalizadoException">El conductor todavía no finalizó el viaje.</exception>
    /// <exception cref="NoViajasteException">Yo o la otra persona no viajamos (no hicimos check-in).</exception>
    /// <exception cref="YaCalificasteException">Ya la califiqué en este viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task CalificarAsync(Viaje viaje, PersonaACalificar persona, int estrellas, string? comentario)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        ArgumentNullException.ThrowIfNull(persona);
        var usuario = UsuarioRequerido;

        // Validación temprana de la entrada
        if (estrellas < EstrellasMinimas || estrellas > EstrellasMaximas)
            throw new ArgumentOutOfRangeException($"Elegí de {EstrellasMinimas} a {EstrellasMaximas} estrellas.", innerException: null);

        string? texto = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();
        if (texto?.Length > ComentarioMaximo)
            throw new ArgumentException($"El comentario puede tener hasta {ComentarioMaximo} caracteres.");

        if (persona.Uid == usuario.Uid)
            throw new ArgumentException("No te podés calificar a vos mismo.");

        // Estado del viaje
        if (!viaje.Finalizado)
            throw new ViajeNoFinalizadoException();
        if (!viaje.YoViaje)
            throw new NoViajasteException();
        if (!viaje.QuienesViajaron.Any(p => p.Uid == persona.Uid))
            throw new NoViajasteException($"{persona.Nombre} no hizo check-in en este viaje, así que no se le puede calificar.");

        string clave = Calificacion.ClaveDe(viaje.Id, usuario.Uid);
        if (DeLaPersona(persona.Uid).Any(c => c.Clave == clave)
            || await _remoto.ExisteCalificacionAsync(persona.Uid, clave))
            throw new YaCalificasteException(persona.Nombre);

        await _remoto.GuardarCalificacionAsync(persona.Uid, clave, new CalificacionNuevaDatos
        {
            ViajeId = viaje.Id,
            AutorUid = usuario.Uid,
            AutorNombre = usuario.Nombre,
            Estrellas = estrellas,
            Comentario = texto
        });

        // Se agrega enseguida, sin esperar a la próxima vuelta del timer
        if (!_porCalificado.TryGetValue(persona.Uid, out var lista))
            _porCalificado[persona.Uid] = lista = new List<Calificacion>();
        lista.Add(new Calificacion(persona.Uid, viaje.Id, usuario.Uid, usuario.Nombre, estrellas, texto, DateTime.Now));
        ActualizarViajes();
    }

    /// <summary>
    /// Lo llama el timer de la app: trae todas las calificaciones, actualiza los promedios de las tarjetas
    /// y, por cada calificación nueva que recibí, dispara CALIFICACION_RECIBIDA.
    /// La primera vez en este dispositivo no avisa las viejas: solo las que lleguen desde ahora.
    /// </summary>
    public async Task RevisarAsync()
    {
        if (_revisando || ServicioAutenticacion.UidActual is not string yo) return;
        _revisando = true;
        try
        {
            var datos = await _remoto.ObtenerCalificacionesAsync();
            _porCalificado = datos.ToDictionary(
                d => d.Key,
                d => d.Value.Values
                    .Select(c => new Calificacion(d.Key, c.ViajeId, c.AutorUid, c.AutorNombre, c.Estrellas, c.Comentario,
                                                  DateTimeOffset.FromUnixTimeMilliseconds(c.Hora).LocalDateTime))
                    .OrderBy(c => c.Hora)
                    .ToList());

            ActualizarViajes();
            AvisarNuevas(yo);
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException or PermisoDenegadoException)
        {
            // Se vuelve a probar en la próxima vuelta del timer
            RegistroErrores.RegistrarAviso(ex, "Revisar calificaciones");
        }
        finally
        {
            _revisando = false;
        }
    }

    /// <summary>Promedio del conductor y cuántos me faltan calificar, en cada viaje de la lista.</summary>
    private void ActualizarViajes()
    {
        foreach (var viaje in ServicioViajes.Instancia.Viajes)
        {
            viaje.CalificacionConductor = TextoDe(viaje.ConductorUid);
            viaje.PendientesDeCalificar = PorCalificar(viaje).Count;
        }
    }

    private void AvisarNuevas(string yo)
    {
        var recibidas = DeLaPersona(yo);
        bool primeraVez = CargarAvisadas() is null;
        var avisadas = _yaAvisadas ??= new HashSet<string>();

        var nuevas = recibidas.Where(c => !avisadas.Contains(c.Clave)).ToList();
        if (nuevas.Count == 0 && !primeraVez) return;   // la primera vez se guarda igual, aunque esté vacía

        foreach (var calificacion in nuevas)
        {
            avisadas.Add(calificacion.Clave);
            if (primeraVez) continue;   // las viejas no se avisan

            // Solo se avisa si el viaje está en la lista (si se borró de Firebase, no hay a qué asociarlo)
            if (ServicioViajes.Instancia.Viajes.FirstOrDefault(v => v.Id == calificacion.ViajeId) is Viaje viaje)
            {
                viaje.UltimaCalificacion = calificacion;
                ServicioViajes.Instancia.Eventos.Disparar(ViajeEvento.CALIFICACION_RECIBIDA, viaje);
            }
        }
        GuardarAvisadas(avisadas);
    }

    /// <summary>Al cerrar sesión: se olvida todo (lo avisado queda guardado en el dispositivo, por usuario).</summary>
    public void Reiniciar()
    {
        _porCalificado = new Dictionary<string, List<Calificacion>>();
        _yaAvisadas = null;
    }

    /// <summary>Lee las claves ya avisadas. Devuelve null si en este dispositivo nunca se guardaron.</summary>
    private HashSet<string>? CargarAvisadas()
    {
        if (_yaAvisadas is not null) return _yaAvisadas;
        if (RutaAvisadas is not string ruta || !File.Exists(ruta)) return null;
        try
        {
            using var archivo = File.OpenRead(ruta);
            var lista = JsonSerializer.Deserialize(archivo, JsonPool.Default.ListString);
            _yaAvisadas = lista?.ToHashSet() ?? new HashSet<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            RegistroErrores.RegistrarError(ex, "Leer calificaciones avisadas");
            _yaAvisadas = new HashSet<string>();
        }
        return _yaAvisadas;
    }

    private static void GuardarAvisadas(HashSet<string> avisadas)
    {
        if (RutaAvisadas is not string ruta) return;
        try
        {
            using var archivo = File.Create(ruta);
            JsonSerializer.Serialize(archivo, avisadas.ToList(), JsonPool.Default.ListString);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RegistroErrores.RegistrarError(ex, "Guardar calificaciones avisadas");
        }
    }
}
