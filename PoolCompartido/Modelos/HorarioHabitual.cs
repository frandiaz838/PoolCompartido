namespace PoolCompartido;

/// <summary>
/// HU30: un viaje que el usuario hace todas las semanas ("lunes, miércoles y viernes a las 07:30, Saldán → UBP").
/// Con esto la app le marca los viajes que le sirven y le avisa cuando publican uno.
/// </summary>
/// <param name="Id">Clave en Firebase (/horarios/{uid}/{id}).</param>
/// <param name="Dias">Días de la semana en que viaja.</param>
/// <param name="Hora">Hora a la que le gustaría salir.</param>
/// <param name="Origen">Desde dónde sale (opcional: vacío = cualquier origen).</param>
/// <param name="Destino">A dónde va.</param>
public record HorarioHabitual(string Id, IReadOnlyList<DayOfWeek> Dias, TimeSpan Hora, string Origen, string Destino)
{
    private static readonly string[] Nombres = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };
    private static readonly string[] Cortos = { "Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb" };

    /// <summary>Lunes primero, como en el calendario argentino.</summary>
    public static IReadOnlyList<DayOfWeek> SemanaDeLunes { get; } = new[]
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    public static string NombreDia(DayOfWeek dia) => Nombres[(int)dia];
    public static string DiaCorto(DayOfWeek dia) => Cortos[(int)dia];

    public string HoraTexto => Hora.ToString(@"hh\:mm");

    /// <summary>"Lun, Mié, Vie".</summary>
    public string DiasTexto => string.Join(", ", SemanaDeLunes.Where(Dias.Contains).Select(DiaCorto));

    public string Ruta => string.IsNullOrWhiteSpace(Origen) ? $"→ {Destino}" : $"{Origen} → {Destino}";

    /// <summary>"Lun, Mié, Vie · 07:30".</summary>
    public string Resumen => $"{DiasTexto} · {HoraTexto}";

    /// <summary>Para los avisos: "lunes 07:30" (el día del viaje que coincide).</summary>
    public string DescripcionPara(DayOfWeek dia) => $"{NombreDia(dia)} {HoraTexto}";
}
