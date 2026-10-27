using System.Text.Json;

namespace PoolCompartido;

/// <summary>HU26: eventos del centro de notificaciones.</summary>
public enum EventoNotificacion
{
    /// <summary>Llegó un aviso (o se le sumó texto a uno recién llegado): se muestra el cartel.</summary>
    NOTIFICACION_NUEVA,
    /// <summary>Cambió cuántos avisos hay sin leer: se actualiza la campanita.</summary>
    SIN_LEER_CAMBIO
}

/// <summary>
/// HU26: centro de notificaciones.
/// Escucha TODOS los eventos de viajes, los convierte en texto con ConversorMensajes (HU7)
/// y los guarda (los últimos 50, en el dispositivo). Después avisa con su propio despachador
/// (otra instancia del genérico Despachador&lt;TEvento, TDatos&gt;): así el cartel y la campanita
/// de cada pantalla se enteran, sin importar en qué pantalla esté el usuario.
/// </summary>
public class ServicioNotificaciones
{
    /// <summary>Cuántos avisos se guardan como máximo (los más viejos se borran).</summary>
    public const int Maximo = 50;

    /// <summary>
    /// Un evento encadenado (por ejemplo COSTO_RECALCULADO después de "Ana se anotó") que llega
    /// dentro de este tiempo se suma al aviso anterior del mismo viaje, en vez de ser otro aviso.
    /// </summary>
    public static readonly TimeSpan VentanaEncadenado = TimeSpan.FromSeconds(3);

    public static ServicioNotificaciones Instancia { get; } = new();

    public Despachador<EventoNotificacion, Notificacion?> Eventos { get; private set; } = new();

    /// <summary>Reloj de la app (se puede cambiar en las pruebas).</summary>
    public Func<DateTime> Reloj { get; set; } = () => DateTime.Now;

    // La más nueva primero
    private readonly List<Notificacion> _lista = new();
    private string? _uid;

    /// <summary>Último aviso mostrado en un cartel, por si la pantalla todavía no estaba lista para verlo.</summary>
    public Notificacion? UltimoCartel { get; private set; }

    public IReadOnlyList<Notificacion> Lista => _lista;

    public int SinLeer => _lista.Count(n => !n.Leida);

    private ServicioNotificaciones() { }

    private string? RutaArchivo =>
        _uid is null ? null : Path.Combine(Configuracion.CarpetaDatos, $"notificaciones_{_uid}.json");

    /// <summary>
    /// Al iniciar sesión: carga los avisos guardados de ese usuario y registra una callback
    /// para cada evento de viajes en el despachador de ServicioViajes.
    /// </summary>
    public void Conectar(Despachador<ViajeEvento, Viaje> eventosDeViajes, string uid)
    {
        ArgumentNullException.ThrowIfNull(eventosDeViajes);
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        _uid = uid;
        Cargar();

        foreach (ViajeEvento evento in Enum.GetValues<ViajeEvento>())
            eventosDeViajes.Registrar(evento, viaje => AlOcurrirEvento(evento, viaje));

        Eventos.Disparar(EventoNotificacion.SIN_LEER_CAMBIO, null);
    }

    /// <summary>Callback de los eventos de viajes: arma el aviso para el usuario actual (o nada).</summary>
    internal void AlOcurrirEvento(ViajeEvento evento, Viaje viaje)
    {
        string? texto = ConversorMensajes.Convertir(evento, viaje);
        if (texto is null) return;   // a este usuario no le importa

        // HU14 / HU16: el nuevo costo o el lugar liberado llegan encadenados justo después de otro aviso
        // del mismo viaje: se agregan abajo de ese aviso en lugar de taparlo
        bool encadenado = evento == ViajeEvento.COSTO_RECALCULADO
                          || (evento == ViajeEvento.ASIENTO_LIBERADO && viaje.Iniciado);
        if (encadenado && _lista.FirstOrDefault() is Notificacion anterior
            && anterior.ViajeId == viaje.Id && Reloj() - anterior.Hora <= VentanaEncadenado)
        {
            anterior.Texto += "\n" + texto;
            anterior.Leida = false;
            Guardar();
            Mostrar(anterior);
            return;
        }

        Agregar(new Notificacion { ViajeId = viaje.Id, Evento = evento, Hora = Reloj(), Texto = texto });
    }

    /// <summary>Un aviso general que solo se muestra en el cartel (por ejemplo, "Sin conexión").</summary>
    public void Avisar(string texto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        Mostrar(new Notificacion { Texto = texto, Hora = Reloj(), Guardada = false, Leida = true });
    }

    private void Agregar(Notificacion notificacion)
    {
        _lista.Insert(0, notificacion);
        if (_lista.Count > Maximo)
            _lista.RemoveRange(Maximo, _lista.Count - Maximo);

        Guardar();
        Mostrar(notificacion);
    }

    private void Mostrar(Notificacion notificacion)
    {
        UltimoCartel = notificacion;
        Eventos.Disparar(EventoNotificacion.NOTIFICACION_NUEVA, notificacion);
        Eventos.Disparar(EventoNotificacion.SIN_LEER_CAMBIO, notificacion);
    }

    public void MarcarLeida(Notificacion notificacion)
    {
        ArgumentNullException.ThrowIfNull(notificacion);
        if (notificacion.Leida) return;

        notificacion.Leida = true;
        Guardar();
        Eventos.Disparar(EventoNotificacion.SIN_LEER_CAMBIO, notificacion);
    }

    public void MarcarTodasLeidas()
    {
        if (SinLeer == 0) return;

        foreach (var notificacion in _lista)
            notificacion.Leida = true;
        Guardar();
        Eventos.Disparar(EventoNotificacion.SIN_LEER_CAMBIO, null);
    }

    /// <summary>Al cerrar sesión: se olvida la lista (la del próximo usuario se carga de su archivo).</summary>
    public void Reiniciar()
    {
        _lista.Clear();
        _uid = null;
        UltimoCartel = null;
        Eventos = new Despachador<EventoNotificacion, Notificacion?>();
    }

    // ---------- Archivo local (notificaciones_{uid}.json) ----------

    private void Cargar()
    {
        _lista.Clear();
        if (RutaArchivo is not string ruta || !File.Exists(ruta)) return;

        try
        {
            using var archivo = File.OpenRead(ruta);
            var datos = JsonSerializer.Deserialize(archivo, JsonPool.Default.ListNotificacionDatos) ?? new();
            _lista.AddRange(datos.Take(Maximo).Select(d => d.ANotificacion()));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Si el archivo está roto se empieza con la lista vacía: no es grave
            RegistroErrores.RegistrarError(ex, "Leer notificaciones guardadas");
        }
    }

    private void Guardar()
    {
        if (RutaArchivo is not string ruta) return;

        try
        {
            using var archivo = File.Create(ruta);
            JsonSerializer.Serialize(archivo, _lista.Select(NotificacionDatos.De).ToList(), JsonPool.Default.ListNotificacionDatos);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RegistroErrores.RegistrarError(ex, "Guardar notificaciones");
        }
    }
}
