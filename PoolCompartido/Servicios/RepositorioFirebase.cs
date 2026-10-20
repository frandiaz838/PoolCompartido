using System.Net;
using System.Text;
using System.Text.Json;

namespace PoolCompartido;

/// <summary>
/// Lee y escribe los viajes en Firebase Realtime Database (HU13) usando su API REST.
/// Todos los pedidos van con el token del usuario, y las reglas de seguridad de Firebase
/// deciden qué puede hacer cada uno.
/// </summary>
internal class RepositorioFirebase
{
    private readonly Func<Task<string>> _obtenerToken;

    public RepositorioFirebase(Func<Task<string>> obtenerToken)
    {
        ArgumentNullException.ThrowIfNull(obtenerToken);
        _obtenerToken = obtenerToken;
    }

    private async Task<string> UrlAsync(string ruta) =>
        $"{Configuracion.FirebaseUrlBase}/{ruta}.json?auth={await _obtenerToken()}";

    /// <summary>Trae todos los viajes (id → datos).</summary>
    public async Task<Dictionary<string, ViajeDatos>> ObtenerTodosAsync()
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, await UrlAsync("viajes"));
        using var respuesta = await ClienteHttp.EnviarAsync(pedido);
        Verificar(respuesta);

        string cuerpo = await respuesta.Content.ReadAsStringAsync();
        // Si todavía no hay viajes, Firebase responde "null"
        return JsonSerializer.Deserialize(cuerpo, JsonPool.Default.DictionaryStringViajeDatos)
               ?? new Dictionary<string, ViajeDatos>();
    }

    /// <summary>Crea un viaje nuevo y devuelve el id que le asignó Firebase.</summary>
    public async Task<string> CrearAsync(ViajeDatos datos)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, await UrlAsync("viajes"))
        {
            Content = Json(datos)
        };
        using var respuesta = await ClienteHttp.EnviarAsync(pedido);
        Verificar(respuesta);

        string cuerpo = await respuesta.Content.ReadAsStringAsync();
        var creado = JsonSerializer.Deserialize(cuerpo, JsonPool.Default.RespuestaCreacion);
        if (creado is null || string.IsNullOrEmpty(creado.Name))
            throw new ServidorException("no devolvió el id del viaje");
        return creado.Name;
    }

    /// <summary>
    /// Lee un viaje junto con su ETag (una "huella" de la versión actual).
    /// Sirve para guardar después solo si nadie lo cambió en el medio.
    /// </summary>
    public async Task<(ViajeDatos? Datos, string Etag)> ObtenerConEtagAsync(string id)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, await UrlAsync($"viajes/{id}"));
        pedido.Headers.Add("X-Firebase-ETag", "true");
        using var respuesta = await ClienteHttp.EnviarAsync(pedido);
        Verificar(respuesta);

        if (!respuesta.Headers.TryGetValues("ETag", out var valores))
            throw new ServidorException("no devolvió el ETag");

        string cuerpo = await respuesta.Content.ReadAsStringAsync();
        return (JsonSerializer.Deserialize(cuerpo, JsonPool.Default.ViajeDatos), valores.First());
    }

    /// <summary>
    /// Guarda el viaje solo si sigue igual que cuando se leyó (mismo ETag).
    /// Devuelve false si otra persona lo modificó en el medio (respuesta 412).
    /// </summary>
    public async Task<bool> ReemplazarSiNoCambioAsync(string id, ViajeDatos datos, string etag)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Put, await UrlAsync($"viajes/{id}"))
        {
            Content = Json(datos)
        };
        pedido.Headers.TryAddWithoutValidation("if-match", etag);
        using var respuesta = await ClienteHttp.EnviarAsync(pedido);

        if (respuesta.StatusCode == HttpStatusCode.PreconditionFailed)
            return false;

        Verificar(respuesta);
        return true;
    }

    private static StringContent Json(ViajeDatos datos) =>
        new(JsonSerializer.Serialize(datos, JsonPool.Default.ViajeDatos), Encoding.UTF8, "application/json");

    /// <summary>Convierte las respuestas de error de Firebase en excepciones de la app.</summary>
    private static void Verificar(HttpResponseMessage respuesta)
    {
        if (respuesta.IsSuccessStatusCode)
            return;

        if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PermisoDenegadoException();

        throw new ServidorException($"código {(int)respuesta.StatusCode}");
    }
}
