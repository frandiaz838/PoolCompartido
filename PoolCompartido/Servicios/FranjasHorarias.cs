using System.Globalization;

namespace PoolCompartido;

/// <summary>Franjas del día para filtrar la lista de viajes.</summary>
public enum FranjaHoraria
{
    Todas,
    Manana,   // de 6 a 12
    Tarde,    // de 12 a 19
    Noche     // de 19 a 6
}

/// <summary>Funciones para trabajar con la hora de salida de los viajes (formato "HH:mm").</summary>
public static class FranjasHorarias
{
    /// <summary>Textos para el filtro, en el mismo orden que el enum FranjaHoraria.</summary>
    public static readonly string[] Nombres = { "Todas", "Mañana", "Tarde", "Noche" };

    /// <summary>Convierte "07:45" en una hora. Devuelve false si el texto no es una hora válida.</summary>
    public static bool TryLeerHora(string? texto, out TimeSpan hora) =>
        TimeSpan.TryParseExact(texto, @"hh\:mm", CultureInfo.InvariantCulture, out hora);

    public static string HoraATexto(TimeSpan hora) => hora.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    /// <summary>Indica si un viaje que sale a esa hora pertenece a la franja elegida.</summary>
    public static bool Incluye(FranjaHoraria franja, string hora)
    {
        if (franja == FranjaHoraria.Todas)
            return true;
        if (!TryLeerHora(hora, out var h))
            return false;   // hora inválida: no se muestra en ninguna franja en particular

        return franja switch
        {
            FranjaHoraria.Manana => h.Hours >= 6 && h.Hours < 12,
            FranjaHoraria.Tarde => h.Hours >= 12 && h.Hours < 19,
            FranjaHoraria.Noche => h.Hours >= 19 || h.Hours < 6,
            _ => true
        };
    }
}
