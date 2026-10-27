using System.Globalization;

namespace PoolCompartido;

/// <summary>
/// HU25: un mensaje del chat grupal de un viaje.
/// Los mensajes de sistema ("Ana se sumó al viaje") se muestran centrados y sin globo.
/// </summary>
/// <param name="Clave">Id que le dio Firebase: está ordenado por fecha, así sirve para pedir "los nuevos".</param>
public record MensajeChat(string Clave, string AutorUid, string AutorNombre, string Texto, DateTime Hora, bool Sistema)
{
    public bool EsMio => AutorUid == ServicioAutenticacion.UidActual;
    public bool EsDeOtro => !Sistema && !EsMio;
    public bool EsMioNormal => !Sistema && EsMio;

    /// <summary>"14:32" si es de hoy, "26/10 14:32" si es de otro día.</summary>
    public string HoraTexto => Hora.Date == DateTime.Today
        ? Hora.ToString("HH:mm", CultureInfo.InvariantCulture)
        : Hora.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
}
