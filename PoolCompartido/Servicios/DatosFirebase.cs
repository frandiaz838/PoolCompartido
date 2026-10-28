using System.Globalization;
using System.Text.Json.Serialization;

namespace PoolCompartido;

// Clases que representan cómo viajan los datos en JSON (Firebase y archivos locales).
// No tienen lógica: solo datos.

/// <summary>Cómo se guarda un viaje en Firebase y en la caché local.</summary>
internal class ViajeDatos
{
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    /// <summary>Formato yyyy-MM-dd, para no depender de la zona horaria.</summary>
    public string Fecha { get; set; } = "";
    public string Hora { get; set; } = "";
    public string ConductorUid { get; set; } = "";
    public string ConductorNombre { get; set; } = "";
    public string Comentario { get; set; } = "";
    public int AsientosDisponibles { get; set; }
    public bool Cancelado { get; set; }
    /// <summary>uid → nombre de cada pasajero.</summary>
    public Dictionary<string, string>? Pasajeros { get; set; }
    /// <summary>HU24: lugar público desde donde sale el conductor (opcional).</summary>
    public string? PuntoPartida { get; set; }
    /// <summary>HU14: costo total del viaje en pesos, o null si es sin costo (así no se guarda en Firebase).</summary>
    public decimal? CostoTotal { get; set; }
    /// <summary>HU15: el conductor inició el viaje. Solo se guarda si es true (null = no), así los viajes viejos no cambian.</summary>
    public bool? Iniciado { get; set; }
    /// <summary>HU15: el conductor finalizó el viaje. Solo se guarda si es true (null = no).</summary>
    public bool? Finalizado { get; set; }
    /// <summary>HU15: uid → true de los pasajeros que ya subieron (check-in con PIN). Solo lo escribe el conductor.</summary>
    public Dictionary<string, bool>? ABordo { get; set; }
    /// <summary>HU15: uid → hash SHA-256 del PIN de cada pasajero. El PIN en sí está en /pines/{uid}, que solo lee él.</summary>
    public Dictionary<string, string>? PinHash { get; set; }
    /// <summary>HU28: identificador común de los viajes de una rutina fija, o null si es un viaje suelto.</summary>
    public string? SerieId { get; set; }
}

/// <summary>HU31: una calificación como se lee de Firebase (/calificaciones/{calificadoUid}/{viajeId}_{autorUid}).</summary>
internal class CalificacionDatos
{
    public string ViajeId { get; set; } = "";
    public string AutorUid { get; set; } = "";
    public string AutorNombre { get; set; } = "";
    public int Estrellas { get; set; }
    public string? Comentario { get; set; }
    /// <summary>Milisegundos desde 1970 (la hora la pone el servidor de Firebase).</summary>
    public long Hora { get; set; }
}

/// <summary>HU31: una calificación nueva. La hora la completa Firebase, así nadie la puede inventar.</summary>
internal class CalificacionNuevaDatos
{
    public string ViajeId { get; set; } = "";
    public string AutorUid { get; set; } = "";
    public string AutorNombre { get; set; } = "";
    public int Estrellas { get; set; }
    public string? Comentario { get; set; }
    public Dictionary<string, string> Hora { get; set; } = new() { [".sv"] = "timestamp" };
}

/// <summary>HU24: punto de encuentro privado de un pasajero (/encuentros/{viajeId}/{uid}).</summary>
internal class PuntoEncuentroDatos
{
    public string Texto { get; set; } = "";
    /// <summary>HU23: coordenadas exactas si el pasajero usó "Mi ubicación" (privadas, solo las ve el conductor).</summary>
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>
    /// true si las coordenadas se calcularon a partir de la dirección escrita (aproximadas):
    /// sirven para ordenar la ruta, pero en el mapa se muestra la dirección tal cual la escribió.
    /// </summary>
    public bool? Aproximado { get; set; }
    /// <summary>
    /// Localidad y provincia donde queda la dirección escrita (por ejemplo "Villa Allende, Córdoba"),
    /// para que Google Maps la encuentre. null en los puntos guardados antes de este cambio.
    /// </summary>
    public string? Zona { get; set; }
}

/// <summary>
/// Ruta óptima: un lugar ya encontrado en el mapa, guardado en el dispositivo (lugares.json)
/// para no volver a buscarlo. No se sube a Firebase.
/// </summary>
internal class LugarGuardadoDatos
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string? Localidad { get; set; }
    public string? Provincia { get; set; }
    public string? Nombre { get; set; }
    /// <summary>Cuándo se guardó (para borrar los más viejos).</summary>
    public DateTime Guardado { get; set; }

    public LugarEncontrado ALugar() => new(new Coordenada(Lat, Lon), Localidad, Provincia, Nombre);

    public static LugarGuardadoDatos De(LugarEncontrado lugar) => new()
    {
        Lat = lugar.Punto.Latitud,
        Lon = lugar.Punto.Longitud,
        Localidad = lugar.Localidad,
        Provincia = lugar.Provincia,
        Nombre = lugar.Nombre,
        Guardado = DateTime.UtcNow
    };
}

/// <summary>HU26: un aviso del centro de notificaciones, guardado en el dispositivo (no va a Firebase).</summary>
internal class NotificacionDatos
{
    public string Id { get; set; } = "";
    public string? ViajeId { get; set; }
    /// <summary>Se guarda con el nombre ("ASIENTO_RESERVADO"), así no se rompe si se agregan eventos en el medio.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ViajeEvento>))]
    public ViajeEvento? Evento { get; set; }
    public string Texto { get; set; } = "";
    public DateTime Hora { get; set; }
    public bool Leida { get; set; }

    public Notificacion ANotificacion() => new()
    {
        Id = string.IsNullOrEmpty(Id) ? Guid.NewGuid().ToString("N") : Id,
        ViajeId = ViajeId,
        Evento = Evento,
        Texto = Texto,
        Hora = Hora,
        Leida = Leida
    };

    public static NotificacionDatos De(Notificacion n) => new()
    {
        Id = n.Id,
        ViajeId = n.ViajeId,
        Evento = n.Evento,
        Texto = n.Texto,
        Hora = n.Hora,
        Leida = n.Leida
    };
}

/// <summary>HU32: mi teléfono (/telefonos/{uid}, solo lo lee el dueño).</summary>
internal class TelefonoDatos
{
    public string Numero { get; set; } = "";
}

/// <summary>HU30: un horario habitual tal como se guarda en Firebase (/horarios/{uid}/{id}).</summary>
internal class HorarioDatos
{
    /// <summary>Días de la semana separados por coma (0 = domingo … 6 = sábado), por ejemplo "1,3,5".</summary>
    public string Dias { get; set; } = "";
    /// <summary>"07:30".</summary>
    public string Hora { get; set; } = "";
    public string? Origen { get; set; }
    public string Destino { get; set; } = "";

    /// <summary>Convierte a modelo; null si los datos están rotos (no se muestran).</summary>
    public HorarioHabitual? AHorario(string id)
    {
        var dias = Dias.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => int.TryParse(d, out int n) && n is >= 0 and <= 6 ? (DayOfWeek?)n : null)
            .ToList();
        if (dias.Count == 0 || dias.Any(d => d is null) || string.IsNullOrWhiteSpace(Destino)
            || !TimeSpan.TryParseExact(Hora, @"hh\:mm", CultureInfo.InvariantCulture, out var hora))
            return null;
        return new HorarioHabitual(id, dias.Select(d => d!.Value).ToList(), hora, Origen ?? "", Destino);
    }

    public static HorarioDatos De(HorarioHabitual h) => new()
    {
        Dias = string.Join(",", h.Dias.Select(d => (int)d)),
        Hora = h.HoraTexto,
        Origen = string.IsNullOrWhiteSpace(h.Origen) ? null : h.Origen,
        Destino = h.Destino
    };
}

/// <summary>HU25: un mensaje del chat tal como se lee de Firebase (/chats/{viajeId}/{clave}).</summary>
internal class MensajeDatos
{
    public string AutorUid { get; set; } = "";
    public string AutorNombre { get; set; } = "";
    public string Texto { get; set; } = "";
    /// <summary>Milisegundos desde 1970 (hora del servidor de Firebase).</summary>
    public long Hora { get; set; }
    public bool? Sistema { get; set; }
}

/// <summary>
/// HU25: un mensaje nuevo para enviar. La hora la pone el servidor de Firebase ({".sv": "timestamp"}),
/// así todos ven el mismo orden aunque el reloj de algún celular esté mal.
/// </summary>
internal class MensajeNuevoDatos
{
    public string AutorUid { get; set; } = "";
    public string AutorNombre { get; set; } = "";
    public string Texto { get; set; } = "";
    public Dictionary<string, string> Hora { get; set; } = new() { [".sv"] = "timestamp" };
    public bool? Sistema { get; set; }
}

/// <summary>Respuesta de Firebase al crear un viaje: el id que le asignó.</summary>
internal class RespuestaCreacion
{
    public string Name { get; set; } = "";
}

/// <summary>Sesión guardada en el dispositivo (nunca se guarda la contraseña).</summary>
internal class SesionGuardada
{
    public string Uid { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Email { get; set; } = "";
    public string RefreshToken { get; set; } = "";
}

internal class PedidoAutenticacion
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public bool ReturnSecureToken { get; set; } = true;
}

internal class PedidoActualizarPerfil
{
    public string IdToken { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool ReturnSecureToken { get; set; }
}

/// <summary>Pedido para que Firebase mande un mail (por ejemplo, el de verificar la cuenta).</summary>
internal class PedidoCodigo
{
    public string RequestType { get; set; } = "";
    /// <summary>Para VERIFY_EMAIL (mail del usuario con sesión iniciada).</summary>
    public string? IdToken { get; set; }
    /// <summary>Para PASSWORD_RESET (no hace falta haber iniciado sesión).</summary>
    public string? Email { get; set; }
}

/// <summary>Pedido que solo lleva el token del usuario.</summary>
internal class PedidoToken
{
    public string IdToken { get; set; } = "";
}

/// <summary>Datos de la cuenta que devuelve Firebase (accounts:lookup).</summary>
internal class RespuestaConsultaCuenta
{
    public List<CuentaFirebase>? Users { get; set; }
}

internal class CuentaFirebase
{
    public string LocalId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool EmailVerified { get; set; }
}

internal class RespuestaAutenticacion
{
    public string IdToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string LocalId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? DisplayName { get; set; }
    public string ExpiresIn { get; set; } = "3600";
}

/// <summary>El servicio de tokens responde con nombres en snake_case.</summary>
internal class RespuestaRefresco
{
    [JsonPropertyName("id_token")] public string IdToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public string ExpiresIn { get; set; } = "3600";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
}

internal class RespuestaErrorAuth
{
    public DetalleErrorAuth? Error { get; set; }
}

internal class DetalleErrorAuth
{
    public int Code { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Le indica a .NET cómo convertir estas clases a JSON y de vuelta.
/// Se genera en tiempo de compilación, así funciona igual en Android.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(ViajeDatos))]
[JsonSerializable(typeof(Dictionary<string, ViajeDatos>))]
[JsonSerializable(typeof(RespuestaCreacion))]
[JsonSerializable(typeof(MensajeNuevoDatos))]                      // HU25: chat
[JsonSerializable(typeof(Dictionary<string, MensajeDatos>))]
[JsonSerializable(typeof(Dictionary<string, string>))]             // HU25: chats leídos en el dispositivo
[JsonSerializable(typeof(string))]   // HU15: el PIN en /pines
[JsonSerializable(typeof(PuntoEncuentroDatos))]
[JsonSerializable(typeof(Dictionary<string, PuntoEncuentroDatos>))]
[JsonSerializable(typeof(Dictionary<string, LugarGuardadoDatos>))]   // lugares ya encontrados en el mapa
[JsonSerializable(typeof(List<NotificacionDatos>))]                // HU26: notificaciones guardadas
[JsonSerializable(typeof(HorarioDatos))]                           // HU30: horarios habituales
[JsonSerializable(typeof(Dictionary<string, HorarioDatos>))]
[JsonSerializable(typeof(TelefonoDatos))]                          // HU32: teléfono de contacto
[JsonSerializable(typeof(SesionGuardada))]
[JsonSerializable(typeof(PedidoAutenticacion))]
[JsonSerializable(typeof(PedidoActualizarPerfil))]
[JsonSerializable(typeof(PedidoCodigo))]
[JsonSerializable(typeof(PedidoToken))]
[JsonSerializable(typeof(RespuestaConsultaCuenta))]
[JsonSerializable(typeof(RespuestaAutenticacion))]
[JsonSerializable(typeof(RespuestaRefresco))]
[JsonSerializable(typeof(RespuestaErrorAuth))]
[JsonSerializable(typeof(CalificacionNuevaDatos))]                                         // HU31: calificaciones
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, CalificacionDatos>>))]
[JsonSerializable(typeof(List<string>))]                                                   // HU31: calificaciones ya avisadas
internal partial class JsonPool : JsonSerializerContext
{
}

/// <summary>Funciones de conversión entre el modelo de la app (Viaje) y los datos guardados (ViajeDatos).</summary>
internal static class ConversorViajes
{
    private const string FormatoFecha = "yyyy-MM-dd";

    public static string FechaATexto(DateTime fecha) =>
        fecha.ToString(FormatoFecha, CultureInfo.InvariantCulture);

    /// <exception cref="FormatException">La fecha guardada no tiene el formato esperado.</exception>
    public static DateTime TextoAFecha(string texto) =>
        DateTime.ParseExact(texto, FormatoFecha, CultureInfo.InvariantCulture);

    /// <exception cref="FormatException">El viaje guardado tiene una fecha inválida.</exception>
    public static Viaje AViaje(string id, ViajeDatos datos)
    {
        var viaje = new Viaje
        {
            Id = id,
            Origen = datos.Origen,
            Destino = datos.Destino,
            Fecha = TextoAFecha(datos.Fecha),
            Hora = datos.Hora,
            ConductorUid = datos.ConductorUid,
            Conductor = datos.ConductorNombre,
            Comentario = datos.Comentario,
            PuntoPartida = datos.PuntoPartida ?? "",
            CostoTotal = datos.CostoTotal,
            SerieId = datos.SerieId
        };
        viaje.ActualizarDesde(datos);
        return viaje;
    }

    public static ViajeDatos ADatos(Viaje viaje) => new()
    {
        Origen = viaje.Origen,
        Destino = viaje.Destino,
        Fecha = FechaATexto(viaje.Fecha),
        Hora = viaje.Hora,
        ConductorUid = viaje.ConductorUid,
        ConductorNombre = viaje.Conductor,
        Comentario = viaje.Comentario,
        AsientosDisponibles = viaje.AsientosDisponibles,
        Cancelado = viaje.Cancelado,
        Pasajeros = viaje.PasajerosPorUid.Count > 0
            ? new Dictionary<string, string>(viaje.PasajerosPorUid)
            : null,
        PuntoPartida = viaje.TienePuntoPartida ? viaje.PuntoPartida : null,
        CostoTotal = viaje.CostoTotal,
        Iniciado = viaje.Iniciado ? true : null,
        Finalizado = viaje.Finalizado ? true : null,
        ABordo = viaje.ABordo.Count > 0 ? viaje.ABordo.ToDictionary(uid => uid, _ => true) : null,
        PinHash = viaje.PinHashPorUid.Count > 0 ? new Dictionary<string, string>(viaje.PinHashPorUid) : null,
        SerieId = viaje.SerieId
    };
}
