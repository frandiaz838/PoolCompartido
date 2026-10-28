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
            // HU30: si coincide con mis horarios, el aviso lo da VIAJE_COINCIDENTE (que llega encadenado)
            (ViajeEvento.VIAJE_PUBLICADO, RolEnViaje.Ninguno)
                when viaje.AceptaPasajeros && ServicioHorarios.Instancia.MejorCoincidencia(viaje) is not null => null,
            (ViajeEvento.VIAJE_PUBLICADO, _) =>
                $"Nuevo viaje de {viaje.Conductor}: {viaje.Ruta} {cuando}.",

            // HU30: un viaje nuevo coincide con uno de mis horarios habituales
            (ViajeEvento.VIAJE_COINCIDENTE, RolEnViaje.Ninguno) =>
                $"Nuevo viaje para tu horario del {viaje.Coincidencia}: {viaje.Conductor} va de {viaje.Ruta}, {viaje.DiaTexto.ToLowerInvariant()} a las {viaje.Hora}. ¡Anotate!",

            // Alguien se anotó
            (ViajeEvento.ASIENTO_RESERVADO, RolEnViaje.Conductor) =>
                $"{viaje.UltimoPasajeroAnotado} se anotó a tu viaje a {viaje.Destino}. {Asientos(viaje)}",
            (ViajeEvento.ASIENTO_RESERVADO, RolEnViaje.Pasajero) when viaje.UltimoPasajeroAnotadoUid != yo =>
                $"{viaje.UltimoPasajeroAnotado} también viaja con {viaje.Conductor} a {viaje.Destino}.",

            // HU16: con el viaje iniciado, el lugar se libera porque alguien no subió (llega encadenado a NO_SHOW_DETECTADO)
            (ViajeEvento.ASIENTO_LIBERADO, RolEnViaje.Conductor) when viaje.Iniciado =>
                $"{(viaje.UltimosNoShow.Count > 1 ? "Se liberaron sus lugares" : "Se liberó su lugar")}. {Asientos(viaje)}",

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

            // Check-in y ciclo de vida del viaje (HU15)
            (ViajeEvento.VIAJE_INICIADO, RolEnViaje.Conductor) =>
                $"Iniciaste el viaje a {viaje.Destino}. Cuando suba cada pasajero, pedile su PIN y tocá \"Confirmar PIN\".",
            (ViajeEvento.VIAJE_INICIADO, RolEnViaje.Pasajero) =>
                $"{viaje.Conductor} inició el viaje a {viaje.Destino}. Cuando subas, decile tu PIN (lo ves en Mis viajes).",
            (ViajeEvento.PASAJERO_A_BORDO, RolEnViaje.Conductor) =>
                $"{viaje.UltimoPasajeroABordo} subió. {viaje.ABordoTexto}.",
            (ViajeEvento.PASAJERO_A_BORDO, RolEnViaje.Pasajero) when viaje.UltimoPasajeroABordoUid == yo =>
                $"Check-in listo: ya estás a bordo del viaje de {viaje.Conductor}.",
            (ViajeEvento.VIAJE_FINALIZADO, RolEnViaje.Conductor) =>
                $"Finalizaste el viaje a {viaje.Destino}. Quedó en tu historial.",
            (ViajeEvento.VIAJE_FINALIZADO, RolEnViaje.Pasajero) =>
                $"Llegaron a {viaje.Destino}: {viaje.Conductor} finalizó el viaje.",

            // HU25: llegó un mensaje al chat del viaje (los automáticos no generan aviso)
            (ViajeEvento.MENSAJE_RECIBIDO, RolEnViaje.Conductor or RolEnViaje.Pasajero)
                when viaje.UltimoMensaje is MensajeChat m && !m.Sistema && !m.EsMio =>
                $"{m.AutorNombre} en el chat de {viaje.Ruta}: {Recortar(m.Texto, 80)}",

            // No-show (HU16). Al pasajero que sacaron ya no le figura el viaje (rol Ninguno): se lo reconoce por su uid
            (ViajeEvento.NO_SHOW_DETECTADO, _) when yo is not null && viaje.UltimosNoShow.ContainsKey(yo) =>
                $"No llegaste a subir al viaje de {viaje.Conductor} y se liberó tu lugar.",
            (ViajeEvento.NO_SHOW_DETECTADO, RolEnViaje.Conductor) when viaje.UltimosNoShow.Count == 1 =>
                $"{viaje.NoShowTexto} no hizo check-in a tiempo y se lo sacó de tu viaje a {viaje.Destino}.",
            (ViajeEvento.NO_SHOW_DETECTADO, RolEnViaje.Conductor) =>
                $"{viaje.NoShowTexto} no hicieron check-in a tiempo y se los sacó de tu viaje a {viaje.Destino}.",

            // Cualquier otra combinación no le interesa a este usuario
            _ => null
        };
    }

    private static string Recortar(string texto, int largo) =>
        texto.Length <= largo ? texto : texto[..(largo - 1)].TrimEnd() + "…";

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
