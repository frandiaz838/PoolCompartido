namespace PoolCompartido;

/// <summary>
/// HU28: arma las fechas de un viaje que se repite (rutina fija).
/// No usa nada de la app, así se puede probar solo (proyecto PoolCompartido.Pruebas).
/// </summary>
public static class Repeticiones
{
    /// <summary>Como máximo un cuatrimestre.</summary>
    public const int SemanasMaximas = 16;

    /// <summary>
    /// Método genérico: arranca en "desde", avanza con "siguiente" hasta pasar "hasta"
    /// y devuelve los valores que cumplen "incluir". Sirve para fechas, números, etc.
    /// </summary>
    /// <typeparam name="T">Cualquier cosa comparable (DateTime, int...).</typeparam>
    /// <exception cref="ArgumentNullException">Falta alguna de las funciones.</exception>
    /// <exception cref="ArgumentException">"siguiente" no avanza (se quedaría repitiendo para siempre).</exception>
    public static IEnumerable<T> Generar<T>(T desde, T hasta, Func<T, T> siguiente, Func<T, bool> incluir)
        where T : IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(siguiente);
        ArgumentNullException.ThrowIfNull(incluir);

        var resultado = new List<T>();
        for (T actual = desde; actual.CompareTo(hasta) <= 0;)
        {
            if (incluir(actual))
                resultado.Add(actual);

            T proximo = siguiente(actual);
            if (proximo.CompareTo(actual) <= 0)
                throw new ArgumentException("La función siguiente tiene que avanzar.", nameof(siguiente));
            actual = proximo;
        }
        return resultado;
    }

    /// <summary>
    /// Las fechas entre "desde" y "hasta" (las dos incluidas) que caen en alguno de los días elegidos.
    /// Ejemplo: del lunes 03/11 al domingo 16/11, lunes y miércoles → 03/11, 05/11, 10/11 y 12/11.
    /// </summary>
    /// <exception cref="ArgumentNullException">Falta la lista de días.</exception>
    /// <exception cref="ArgumentException">No se eligió ningún día, o "hasta" es anterior a "desde".</exception>
    /// <exception cref="ArgumentOutOfRangeException">Son más de 16 semanas.</exception>
    public static IReadOnlyList<DateTime> Fechas(DateTime desde, DateTime hasta, IEnumerable<DayOfWeek> dias)
    {
        ArgumentNullException.ThrowIfNull(dias);
        var elegidos = dias.ToHashSet();

        // Validación temprana de la entrada
        if (elegidos.Count == 0)
            throw new ArgumentException("Elegí al menos un día de la semana para repetir el viaje.");

        DateTime inicio = desde.Date;
        DateTime fin = hasta.Date;
        if (fin < inicio)
            throw new ArgumentException("La fecha \"hasta\" no puede ser anterior a la del primer viaje.");

        // innerException: null elige el constructor que deja el mensaje limpio
        if (fin > inicio.AddDays(SemanasMaximas * 7))
            throw new ArgumentOutOfRangeException($"Se puede repetir como máximo {SemanasMaximas} semanas (un cuatrimestre).", innerException: null);

        return Generar(inicio, fin, fecha => fecha.AddDays(1), fecha => elegidos.Contains(fecha.DayOfWeek)).ToList();
    }
}
