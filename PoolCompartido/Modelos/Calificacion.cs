namespace PoolCompartido;

/// <summary>
/// HU31: lo que una persona opinó de otra después de un viaje.
/// Se guarda en Firebase en /calificaciones/{calificadoUid}/{viajeId}_{autorUid}.
/// </summary>
public record Calificacion(string CalificadoUid, string ViajeId, string AutorUid, string AutorNombre,
                           int Estrellas, string? Comentario, DateTime Hora)
{
    /// <summary>Clave en Firebase: una sola calificación por viaje y por autor.</summary>
    public string Clave => ClaveDe(ViajeId, AutorUid);

    public static string ClaveDe(string viajeId, string autorUid) => $"{viajeId}_{autorUid}";

    /// <summary>"★★★★☆": las estrellas siempre van con el número al lado, nunca solas.</summary>
    public static string Dibujar(int estrellas) =>
        new string('★', Math.Clamp(estrellas, 0, 5)) + new string('☆', 5 - Math.Clamp(estrellas, 0, 5));
}

/// <summary>HU31: alguien con quien viajé y todavía no califiqué.</summary>
public record PersonaACalificar(string Uid, string Nombre, bool EsConductor)
{
    public string Descripcion => EsConductor ? $"{Nombre} (conductor)" : $"{Nombre} (pasajero)";
}
