namespace PoolCompartido;

/// <summary>
/// HU30: horarios habituales del usuario (se guardan en Firebase, en /horarios/{uid}, que solo lee y escribe él).
/// Compara cada viaje con esos horarios: si sale el mismo día, con una hora de diferencia como máximo,
/// y va al mismo lugar, "coincide". Para elegir el mejor horario usa el método genérico Busqueda.MasCercano.
/// Cuando alguien publica un viaje que coincide, dispara VIAJE_COINCIDENTE encadenado a VIAJE_PUBLICADO (HU11).
/// </summary>
public class ServicioHorarios
{
    /// <summary>Cuántos horarios se pueden cargar como máximo.</summary>
    public const int Maximo = 10;

    /// <summary>Diferencia máxima entre la hora del viaje y la de mi horario (igual que el reemplazo, HU6).</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromHours(1);

    public static ServicioHorarios Instancia { get; } = new();

    private readonly List<HorarioHabitual> _lista = new();
    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());
    private Despachador<ViajeEvento, Viaje>? _eventos;

    private ServicioHorarios() { }

    public IReadOnlyList<HorarioHabitual> Lista => _lista;

    private static string UidRequerido => ServicioAutenticacion.UidActual ?? throw new SesionVencidaException();

    // ---------- Firebase ----------

    /// <summary>Trae los horarios del usuario.</summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task CargarAsync()
    {
        var datos = await _remoto.ObtenerHorariosAsync(UidRequerido);
        _lista.Clear();
        _lista.AddRange(datos.Select(d => d.Value.AHorario(d.Key)).OfType<HorarioHabitual>());
    }

    /// <summary>Agrega o reemplaza un horario (si el id ya existe, lo edita).</summary>
    /// <exception cref="ArgumentException">Sin días, sin destino o textos demasiado largos.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Ya tiene el máximo de horarios.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<HorarioHabitual> GuardarAsync(string? id, IEnumerable<DayOfWeek> dias, TimeSpan hora, string? origen, string? destino)
    {
        var horario = Validar(id, dias, hora, origen, destino);

        bool esNuevo = !_lista.Any(h => h.Id == horario.Id);
        if (esNuevo && _lista.Count >= Maximo)
            throw new ArgumentOutOfRangeException(null, $"Podés cargar hasta {Maximo} horarios.");

        await _remoto.GuardarHorarioAsync(UidRequerido, horario.Id, HorarioDatos.De(horario));

        int i = _lista.FindIndex(h => h.Id == horario.Id);
        if (i >= 0) _lista[i] = horario; else _lista.Add(horario);
        return horario;
    }

    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task BorrarAsync(HorarioHabitual horario)
    {
        ArgumentNullException.ThrowIfNull(horario);
        await _remoto.BorrarHorarioAsync(UidRequerido, horario.Id);
        _lista.RemoveAll(h => h.Id == horario.Id);
    }

    /// <summary>Validación temprana, antes de ir a Firebase.</summary>
    /// <exception cref="ArgumentException">Sin días, sin destino o textos demasiado largos.</exception>
    public static HorarioHabitual Validar(string? id, IEnumerable<DayOfWeek>? dias, TimeSpan hora, string? origen, string? destino)
    {
        var lista = (dias ?? Array.Empty<DayOfWeek>()).Distinct().OrderBy(d => d).ToList();
        if (lista.Count == 0)
            throw new ArgumentException("Elegí al menos un día de la semana.");
        if (hora < TimeSpan.Zero || hora >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(null, "La hora no es válida.");

        string destinoLimpio = ServicioEncuentros.ValidarTexto(destino, "Escribí a dónde vas.");
        string origenLimpio = string.IsNullOrWhiteSpace(origen) ? "" : ServicioEncuentros.ValidarTexto(origen, "");

        string idFinal = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N")[..12] : id;
        // Se guarda sin segundos: "07:30"
        return new HorarioHabitual(idFinal, lista, new TimeSpan(hora.Hours, hora.Minutes, 0), origenLimpio, destinoLimpio);
    }

    // ---------- Coincidencias ----------

    /// <summary>
    /// El horario mío que mejor coincide con el viaje (el de hora más cercana), o null si ninguno.
    /// Mis propios viajes nunca "coinciden": los publiqué yo.
    /// </summary>
    public HorarioHabitual? MejorCoincidencia(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        if (viaje.EsDelUsuarioActual || _lista.Count == 0) return null;

        // Programación genérica: el mismo buscador que usa el reemplazo (HU6), ahora con horarios
        return Busqueda.MasCercano(_lista, h => Coincide(h, viaje), h => Diferencia(h, viaje));
    }

    /// <summary>Mismo día, una hora de diferencia como máximo y el mismo destino (y origen, si lo cargó).</summary>
    public static bool Coincide(HorarioHabitual horario, Viaje viaje) =>
        horario.Dias.Contains(viaje.Fecha.DayOfWeek)
        && Diferencia(horario, viaje) <= Tolerancia
        && LugarParecido(horario.Destino, viaje.Destino)
        && (string.IsNullOrWhiteSpace(horario.Origen)
            || LugarParecido(horario.Origen, viaje.Origen)
            || LugarParecido(horario.Origen, viaje.PuntoPartida));

    public static TimeSpan Diferencia(HorarioHabitual horario, Viaje viaje) =>
        (viaje.Salida.TimeOfDay - horario.Hora).Duration();

    /// <summary>Palabras que no sirven para decir que dos lugares son el mismo.</summary>
    private static readonly HashSet<string> PalabrasComunes = new()
    {
        "de", "del", "la", "el", "los", "las", "y", "en", "a", "al", "av", "avenida", "calle", "barrio", "centro",
        "universidad", "facultad", "facu", "colegio", "escuela", "plaza", "parque", "club", "cordoba", "argentina"
    };

    /// <summary>
    /// "Saldán" y "Saldán Centro", o "UBP" y "Universidad Blas Pascal", son el mismo lugar:
    /// comparten al menos una palabra importante (sin tildes, mayúsculas ni siglas).
    /// </summary>
    public static bool LugarParecido(string? a, string? b)
    {
        var pa = PalabrasImportantes(a);
        var pb = PalabrasImportantes(b);
        return pa.Count > 0 && pb.Count > 0 && pa.Overlaps(pb);
    }

    private static HashSet<string> PalabrasImportantes(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return new HashSet<string>();
        string completo = Geocodificador.ExpandirSiglas(texto) ?? texto;
        return Geocodificador.Normalizar(completo)
            .Split(new[] { ' ', ',', '.', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 3 && !PalabrasComunes.Contains(p))
            .ToHashSet();
    }

    /// <summary>Marca en cada viaje con cuál de mis horarios coincide (para la etiqueta y el orden de la lista).</summary>
    public void ActualizarCoincidencias(IEnumerable<Viaje> viajes)
    {
        ArgumentNullException.ThrowIfNull(viajes);
        foreach (var viaje in viajes)
            viaje.Coincidencia = MejorCoincidencia(viaje)?.DescripcionPara(viaje.Fecha.DayOfWeek);
    }

    // ---------- Eventos ----------

    /// <summary>
    /// Al iniciar sesión: cuando alguien publica un viaje, si coincide con un horario mío y todavía
    /// me puedo anotar, se dispara VIAJE_COINCIDENTE (encadenado). El centro de notificaciones (HU26) lo avisa.
    /// Hay que conectarlo ANTES que ServicioNotificaciones, así el aviso de "Nuevo viaje" ya sabe que coincide.
    /// </summary>
    public void Conectar(Despachador<ViajeEvento, Viaje> eventosDeViajes)
    {
        ArgumentNullException.ThrowIfNull(eventosDeViajes);
        _eventos = eventosDeViajes;
        eventosDeViajes.Registrar(ViajeEvento.VIAJE_PUBLICADO, AlPublicarse);
    }

    private void AlPublicarse(Viaje viaje)
    {
        if (viaje.EstoyAnotado || !viaje.AceptaPasajeros || MejorCoincidencia(viaje) is not HorarioHabitual horario)
            return;

        viaje.Coincidencia = horario.DescripcionPara(viaje.Fecha.DayOfWeek);
        _eventos?.Disparar(ViajeEvento.VIAJE_COINCIDENTE, viaje);
    }

    /// <summary>Al cerrar sesión.</summary>
    public void Reiniciar()
    {
        _lista.Clear();
        _eventos = null;
    }
}
