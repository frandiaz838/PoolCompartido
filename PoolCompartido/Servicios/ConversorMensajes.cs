namespace PoolCompartido;

/// <summary>Papel que cumple el usuario actual en un viaje.</summary>
public enum RolEnViaje
{
    Ninguno,
    Conductor,
    Pasajero
}

/// <summary>
/// HU7: conversión de mensajes. Traduce cada evento del sistema (un código como ASIENTO_RESERVADO)
/// a un texto claro para el usuario, distinto según si es conductor o pasajero del viaje.
/// Devuelve null cuando el evento no le importa a ese usuario, así no se muestra nada.
/// </summary>
public static class ConversorMensajes
{
    /// <summary>Qué papel cumple el usuario que inició sesión en este viaje.</summary>
    public static RolEnViaje RolDe(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);

        if (viaje.EsDelUsuarioActual) return RolEnViaje.Conductor;
        if (viaje.EstoyAnotado) return RolEnViaje.Pasajero;
        return RolEnViaje.Ninguno;
    }

    /// <summary>Convierte un evento en el mensaje para el usuario actual, o null si no corresponde avisar.</summary>
    public static string? Convertir(ViajeEvento evento, Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);

        var rol = RolDe(viaje);
        string? yo = ServicioAutenticacion.UidActual;
        string cuando = $"del {viaje.Fecha:dd/MM} a las {viaje.Hora}";

        // switch sobre el par (evento, rol): cada combinación tiene su mensaje
        return (evento, rol) switch
        {
            // Se publicó un viaje
            (ViajeEvento.VIAJE_PUBLICADO, RolEnViaje.Conductor) =>
                $"Publicaste tu viaje {viaje.Ruta} {cuando}.",
            (ViajeEvento.VIAJE_PUBLICADO, _) =>
                $"Nuevo viaje de {viaje.Conductor}: {viaje.Ruta} {cuando}.",

            // Alguien se anotó
            (ViajeEvento.ASIENTO_RESERVADO, RolEnViaje.Conductor) =>
                $"{viaje.UltimoPasajeroAnotado} se anotó a tu viaje a {viaje.Destino}. {Asientos(viaje)}",
            (ViajeEvento.ASIENTO_RESERVADO, RolEnViaje.Pasajero) when viaje.UltimoPasajeroAnotadoUid != yo =>
                $"{viaje.UltimoPasajeroAnotado} también viaja con {viaje.Conductor} a {viaje.Destino}.",

            // Alguien se bajó (HU4)
            (ViajeEvento.ASIENTO_LIBERADO, RolEnViaje.Conductor) =>
                $"{viaje.UltimoPasajeroQueSalio} se bajó de tu viaje a {viaje.Destino}. {Asientos(viaje)}",

            // El conductor canceló (HU5)
            (ViajeEvento.VIAJE_CANCELADO, RolEnViaje.Conductor) =>
                $"Cancelaste tu viaje {viaje.Ruta}. Se avisó a {Pasajeros(viaje.Pasajeros.Count)}.",
            (ViajeEvento.VIAJE_CANCELADO, RolEnViaje.Pasajero) =>
                $"{viaje.Conductor} canceló el viaje {viaje.Ruta} {cuando}.",

            // Búsqueda de reemplazo (HU6)
            (ViajeEvento.REEMPLAZO_ENCONTRADO, RolEnViaje.Pasajero) when viaje.Reemplazo is Viaje otro =>
                $"{viaje.Conductor} canceló, pero hay otro viaje a {otro.Destino}: {otro.Conductor} sale a las {otro.Hora} desde {otro.Origen}.",
            (ViajeEvento.SIN_REEMPLAZO, RolEnViaje.Pasajero) =>
                $"{viaje.Conductor} canceló y no encontramos otro viaje a {viaje.Destino} cerca de las {viaje.Hora}. Vas a tener que buscar otra opción.",

            // Cambió entre cuántos se divide el costo (HU14, encadenado a ASIENTO_RESERVADO / ASIENTO_LIBERADO)
            (ViajeEvento.COSTO_RECALCULADO, RolEnViaje.Conductor) when viaje.Pasajeros.Count == 0 =>
                $"Ya no quedan pasajeros en tu viaje a {viaje.Destino}: el próximo que se anote paga {Viaje.Pesos(viaje.CostoPorPasajero)}.",
            (ViajeEvento.COSTO_RECALCULADO, RolEnViaje.Conductor) =>
                $"Ahora cada pasajero de tu viaje a {viaje.Destino} paga {Viaje.Pesos(viaje.CostoPorPasajero)}.",
            (ViajeEvento.COSTO_RECALCULADO, RolEnViaje.Pasajero) =>
                $"Ahora pagás {Viaje.Pesos(viaje.CostoPorPasajero)} ({SonPasajeros(viaje.Pasajeros.Count)}).",

            // Cualquier otra combinación no le interesa a este usuario
            _ => null
        };
    }

    private static string Asientos(Viaje viaje) => viaje.AsientosDisponibles switch
    {
        0 => "Ya no quedan asientos libres.",
        1 => "Queda 1 asiento libre.",
        int n => $"Quedan {n} asientos libres."
    };

    private static string SonPasajeros(int cantidad) => cantidad == 1
        ? "sos el único pasajero"
        : $"son {cantidad} pasajeros";

    private static string Pasajeros(int cantidad) => cantidad switch
    {
        0 => "nadie, porque no había pasajeros anotados",
        1 => "1 pasajero",
        int n => $"{n} pasajeros"
    };
}
