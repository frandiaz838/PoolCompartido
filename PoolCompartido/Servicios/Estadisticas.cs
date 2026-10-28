using System.Globalization;

namespace PoolCompartido;

/// <summary>
/// HU31: cuentas sobre listas (promedio de calificaciones).
/// No usa nada de la app, así se puede probar solo (proyecto PoolCompartido.Pruebas).
/// </summary>
public static class Estadisticas
{
    /// <summary>
    /// Método genérico: el promedio del valor que devuelve "selector" para cada elemento,
    /// o null si la lista está vacía (no hay promedio de nada).
    /// Ejemplo: Promedio(calificaciones, c => c.Estrellas).
    /// </summary>
    /// <typeparam name="T">Cualquier tipo (en la app, Calificacion).</typeparam>
    /// <exception cref="ArgumentNullException">Falta la lista o el selector.</exception>
    public static double? Promedio<T>(IEnumerable<T> lista, Func<T, double> selector)
    {
        ArgumentNullException.ThrowIfNull(lista);
        ArgumentNullException.ThrowIfNull(selector);

        double suma = 0;
        int cantidad = 0;
        foreach (var elemento in lista)
        {
            suma += selector(elemento);
            cantidad++;
        }
        return cantidad == 0 ? null : suma / cantidad;
    }

    /// <summary>"★ 4,6 (12)", o "Nuevo" si todavía no tiene calificaciones.</summary>
    public static string TextoCalificacion(double? promedio, int cantidad)
    {
        if (promedio is null || cantidad <= 0)
            return "Nuevo";

        // Con coma decimal, como se escribe en Argentina
        string numero = promedio.Value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        return $"★ {numero} ({cantidad})";
    }
}
