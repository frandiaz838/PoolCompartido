namespace PoolCompartido;

/// <summary>
/// Búsqueda genérica (Programación Genérica): sirve para cualquier tipo de dato,
/// no solo para viajes. Recibe qué condición tiene que cumplir un candidato y
/// cómo medir qué tan "cerca" está, y devuelve el mejor.
/// </summary>
public static class Busqueda
{
    /// <summary>
    /// Devuelve el candidato que cumple la condición y tiene la menor distancia,
    /// o null si ninguno la cumple.
    /// </summary>
    /// <typeparam name="T">Tipo de los candidatos (en la app, Viaje).</typeparam>
    /// <typeparam name="TDistancia">Cualquier cosa comparable (TimeSpan, int, double...).</typeparam>
    /// <exception cref="ArgumentNullException">Falta la lista o alguna de las funciones.</exception>
    public static T? MasCercano<T, TDistancia>(IEnumerable<T> candidatos,
                                               Func<T, bool> sirve,
                                               Func<T, TDistancia> distancia)
        where T : class
        where TDistancia : IComparable<TDistancia>
    {
        ArgumentNullException.ThrowIfNull(candidatos);
        ArgumentNullException.ThrowIfNull(sirve);
        ArgumentNullException.ThrowIfNull(distancia);

        T? mejor = null;
        TDistancia? mejorDistancia = default;

        foreach (var candidato in candidatos)
        {
            if (!sirve(candidato)) continue;

            var d = distancia(candidato);
            if (mejor is null || d.CompareTo(mejorDistancia!) < 0)
            {
                mejor = candidato;
                mejorDistancia = d;
            }
        }
        return mejor;
    }
}

/// <summary>
/// HU6: cuando el conductor cancela, busca otro viaje parecido para el pasajero:
/// mismo destino, que salga con una hora de diferencia como máximo, con lugar y abierto.
/// </summary>
public static class BuscadorDeReemplazo
{
    /// <summary>Diferencia máxima de horario para considerar que un viaje "sirve" de reemplazo.</summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromHours(1);

    /// <summary>Devuelve el viaje que más se parece al cancelado, o null si no hay ninguno.</summary>
    /// <param name="cancelado">El viaje que se canceló.</param>
    /// <param name="viajes">Todos los viajes conocidos.</param>
    /// <param name="uid">Usuario que necesita el reemplazo (no se le ofrece su propio viaje).</param>
    /// <param name="ademas">Condición extra opcional (por ejemplo, que no choque con otro viaje suyo).</param>
    /// <exception cref="ArgumentNullException">Falta el viaje cancelado o la lista.</exception>
    public static Viaje? Buscar(Viaje cancelado, IEnumerable<Viaje> viajes, string? uid, Func<Viaje, bool>? ademas = null)
    {
        ArgumentNullException.ThrowIfNull(cancelado);
        ArgumentNullException.ThrowIfNull(viajes);

        return Busqueda.MasCercano(viajes,
            sirve: v => v.Id != cancelado.Id
                        && v.AceptaPasajeros            // ni cancelado ni a menos de 5 minutos de salir
                        && v.AsientosDisponibles > 0
                        && v.ConductorUid != uid        // no se le ofrece su propio viaje
                        && !v.EstoyAnotado
                        && MismoLugar(v.Destino, cancelado.Destino)
                        && Diferencia(v, cancelado) <= Tolerancia
                        && (ademas is null || ademas(v)),
            distancia: v => Diferencia(v, cancelado));
    }

    private static TimeSpan Diferencia(Viaje a, Viaje b) => (a.Salida - b.Salida).Duration();

    private static bool MismoLugar(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
