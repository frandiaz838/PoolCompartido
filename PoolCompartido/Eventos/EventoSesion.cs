namespace PoolCompartido;

/// <summary>
/// Eventos de la sesión (HU17). Usan el mismo Despachador genérico que los viajes:
/// Despachador&lt;EventoSesion, Usuario&gt;.
/// </summary>
public enum EventoSesion
{
    SESION_INICIADA,
    SESION_CERRADA
}
