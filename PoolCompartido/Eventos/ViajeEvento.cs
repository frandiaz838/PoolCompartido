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
    SIN_REEMPLAZO          // HU6
}
