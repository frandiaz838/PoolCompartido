namespace PoolCompartido;

/// <summary>
/// Todos los eventos que puede disparar la app.
/// Agregar un evento nuevo = agregar una línea acá y registrar su callback (HU10).
/// </summary>
public enum ViajeEvento
{
    VIAJE_PUBLICADO,       // HU1
    ASIENTO_RESERVADO,     // HU3
    ASIENTO_LIBERADO,      // HU4
    VIAJE_CANCELADO,       // HU5
    REEMPLAZO_ENCONTRADO,  // HU6
    SIN_REEMPLAZO,         // HU6
    COSTO_RECALCULADO,     // HU14
    VIAJE_INICIADO,        // HU15
    VIAJE_FINALIZADO,      // HU15
    PASAJERO_A_BORDO,      // HU15
    MENSAJE_RECIBIDO,      // HU25
    NO_SHOW_DETECTADO,     // HU16
    VIAJE_COINCIDENTE      // HU30: un viaje nuevo coincide con mis horarios (encadenado a VIAJE_PUBLICADO)
}
