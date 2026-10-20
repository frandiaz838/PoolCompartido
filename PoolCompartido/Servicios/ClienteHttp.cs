namespace PoolCompartido;

/// <summary>
/// Cliente HTTP compartido por toda la app para hablar con Firebase.
/// Convierte los errores de red en SinConexionException, para que el resto del código
/// no tenga que conocer los detalles de HTTP.
/// </summary>
public static class ClienteHttp
{
    /// <summary>Se puede reemplazar en las pruebas por uno falso.</summary>
    public static HttpClient Instancia { get; set; } = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <exception cref="SinConexionException">No hay internet o el servidor tardó demasiado.</exception>
    public static async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage pedido)
    {
        try
        {
            return await Instancia.SendAsync(pedido);
        }
        catch (HttpRequestException ex)
        {
            throw new SinConexionException(ex);
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient lanza TaskCanceledException cuando se vence el tiempo de espera
            throw new SinConexionException(ex);
        }
    }
}
