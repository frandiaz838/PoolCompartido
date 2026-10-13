namespace PoolCompartido;

/// <summary>
/// Bucle despachador genérico con tabla de eventos (HU10).
///
/// Es la versión en C# del std::map&lt;Evento, std::function&lt;void()&gt;&gt; visto en clase:
///   - la "tabla" es un Dictionary que asocia cada evento con sus callbacks (Action&lt;TDatos&gt;);
///   - Disparar() mete el evento en una cola y el bucle lo despacha a sus callbacks.
///
/// Es genérico (Programación Genérica): sirve para cualquier enum de eventos y cualquier tipo de dato.
/// En esta app se usa como Despachador&lt;ViajeEvento, Viaje&gt;.
/// </summary>
public class Despachador<TEvento, TDatos> where TEvento : struct, Enum
{
    private readonly Dictionary<TEvento, List<Action<TDatos>>> _tabla = new();
    private readonly Queue<(TEvento Evento, TDatos Datos)> _cola = new();
    private bool _procesando;

    /// <summary>Registra una callback para un evento.</summary>
    public void Registrar(TEvento evento, Action<TDatos> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (!_tabla.TryGetValue(evento, out var callbacks))
        {
            callbacks = new List<Action<TDatos>>();
            _tabla[evento] = callbacks;
        }
        callbacks.Add(callback);
    }

    /// <summary>
    /// Dispara un evento. Si una callback dispara otro evento (encadenamiento, HU11),
    /// ese segundo evento se encola y se procesa después, en orden.
    /// </summary>
    public void Disparar(TEvento evento, TDatos datos)
    {
        _cola.Enqueue((evento, datos));

        if (_procesando) return; // ya hay un bucle corriendo: va a tomar este evento de la cola

        _procesando = true;
        try
        {
            // Bucle despachador: saca eventos de la cola hasta vaciarla
            while (_cola.Count > 0)
            {
                var (ev, d) = _cola.Dequeue();

                if (_tabla.TryGetValue(ev, out var callbacks))
                {
                    foreach (var callback in callbacks.ToList())
                        callback(d);
                }
            }
        }
        finally
        {
            // Pase lo que pase (aunque una callback lance una excepción), el bucle queda libre
            _procesando = false;
        }
    }
}
