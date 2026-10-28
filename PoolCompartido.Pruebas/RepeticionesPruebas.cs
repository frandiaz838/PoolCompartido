using PoolCompartido;

namespace PoolCompartido.Pruebas;

/// <summary>HU28: pruebas de las fechas de un viaje que se repite.</summary>
public class RepeticionesPruebas
{
    // El 03/11/2025 fue lunes
    private static readonly DateTime Lunes = new(2025, 11, 3);

    [Fact]
    public void LunesYMiercolesDuranteDosSemanas()
    {
        var fechas = Repeticiones.Fechas(Lunes, Lunes.AddDays(13), new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });

        Assert.Equal(new[] { Lunes, Lunes.AddDays(2), Lunes.AddDays(7), Lunes.AddDays(9) }, fechas);
    }

    [Fact]
    public void IncluyeElUltimoDia()
    {
        var fechas = Repeticiones.Fechas(Lunes, Lunes.AddDays(7), new[] { DayOfWeek.Monday });

        Assert.Equal(new[] { Lunes, Lunes.AddDays(7) }, fechas);
    }

    [Fact]
    public void ArrancaElPrimerDiaElegidoAunqueNoSeaElInicio()
    {
        // Desde un lunes, solo viernes: el primero es el 07/11
        var fechas = Repeticiones.Fechas(Lunes, Lunes.AddDays(6), new[] { DayOfWeek.Friday });

        Assert.Equal(new[] { Lunes.AddDays(4) }, fechas);
    }

    [Fact]
    public void IgnoraLaHoraYLosDiasRepetidos()
    {
        var fechas = Repeticiones.Fechas(Lunes.AddHours(8), Lunes.AddDays(1).AddHours(3),
                                         new[] { DayOfWeek.Tuesday, DayOfWeek.Tuesday });

        Assert.Equal(new[] { Lunes.AddDays(1) }, fechas);
    }

    [Fact]
    public void DieciseisSemanasSePuede()
    {
        var fechas = Repeticiones.Fechas(Lunes, Lunes.AddDays(16 * 7), new[] { DayOfWeek.Monday });

        Assert.Equal(17, fechas.Count);   // el primero y uno por cada semana
    }

    [Fact]
    public void MasDeDieciseisSemanasLanzaArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Repeticiones.Fechas(Lunes, Lunes.AddDays(16 * 7 + 1), new[] { DayOfWeek.Monday }));
    }

    [Fact]
    public void SinDiasLanzaArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Repeticiones.Fechas(Lunes, Lunes.AddDays(7), Array.Empty<DayOfWeek>()));
        Assert.IsNotType<ArgumentOutOfRangeException>(ex);
    }

    [Fact]
    public void HastaAntesQueDesdeLanzaArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Repeticiones.Fechas(Lunes, Lunes.AddDays(-1), new[] { DayOfWeek.Monday }));
        Assert.IsNotType<ArgumentOutOfRangeException>(ex);
    }

    [Fact]
    public void GenerarFuncionaConNumeros()
    {
        // El método genérico no es solo para fechas: los pares del 1 al 10
        var pares = Repeticiones.Generar(1, 10, n => n + 1, n => n % 2 == 0);

        Assert.Equal(new[] { 2, 4, 6, 8, 10 }, pares);
    }

    [Fact]
    public void GenerarConUnaFuncionQueNoAvanzaLanzaArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Repeticiones.Generar(1, 10, n => n, _ => true));
    }
}
