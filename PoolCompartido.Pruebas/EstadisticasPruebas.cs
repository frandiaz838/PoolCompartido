using PoolCompartido;

namespace PoolCompartido.Pruebas;

/// <summary>HU31: pruebas del promedio genérico y del texto de las calificaciones.</summary>
public class EstadisticasPruebas
{
    private record Nota(string Autor, int Estrellas);

    [Fact]
    public void PromedioDeLasEstrellas()
    {
        var notas = new[] { new Nota("Ana", 5), new Nota("Juan", 4), new Nota("Pía", 5) };

        double? promedio = Estadisticas.Promedio(notas, n => n.Estrellas);

        Assert.Equal(14.0 / 3, promedio!.Value, precision: 10);
    }

    [Fact]
    public void PromedioDeUnaListaVaciaEsNull()
    {
        Assert.Null(Estadisticas.Promedio(Array.Empty<Nota>(), n => n.Estrellas));
    }

    [Fact]
    public void PromedioFuncionaConCualquierTipo()
    {
        // El método genérico no es solo para calificaciones: el promedio de largo de palabras
        Assert.Equal(4.5, Estadisticas.Promedio(new[] { "auto", "viaje" }, p => p.Length));
    }

    [Fact]
    public void PromedioSinListaLanzaArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => Estadisticas.Promedio<Nota>(null!, n => n.Estrellas));
    }

    [Fact]
    public void TextoConComaYCantidad()
    {
        Assert.Equal("★ 4,6 (12)", Estadisticas.TextoCalificacion(4.58, 12));
    }

    [Fact]
    public void TextoSinCalificacionesEsNuevo()
    {
        Assert.Equal("Nuevo", Estadisticas.TextoCalificacion(null, 0));
    }

    [Fact]
    public void TextoConUnaSolaCalificacion()
    {
        Assert.Equal("★ 5,0 (1)", Estadisticas.TextoCalificacion(5, 1));
    }
}
