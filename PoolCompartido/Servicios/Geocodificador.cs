using System.Globalization;
using System.Text.Json;

namespace PoolCompartido;

/// <summary>Un punto del mapa.</summary>
public readonly record struct Coordenada(double Latitud, double Longitud)
{
    /// <summary>Formato "lat,lon" con punto decimal, el que entienden Google Maps y OSRM.</summary>
    public string ATexto() =>
        string.Create(CultureInfo.InvariantCulture, $"{Latitud:0.######},{Longitud:0.######}");
}

/// <summary>Un lugar encontrado en el mapa, con la localidad y la provincia donde queda.</summary>
/// <param name="Localidad">La ciudad o el pueblo (por ejemplo "Villa Allende"); null si no vino.</param>
/// <param name="Provincia">Por ejemplo "Córdoba"; null si no vino.</param>
/// <param name="Nombre">Cómo se llama en el mapa (por ejemplo "Universidad Blas Pascal"), para mostrárselo al usuario.</param>
public record LugarEncontrado(Coordenada Punto, string? Localidad, string? Provincia, string? Nombre = null)
{
    /// <summary>
    /// "Villa Allende, Córdoba": lo que se le agrega a una dirección escrita para que Google Maps
    /// la busque en el lugar correcto. Nunca es un barrio: Google Maps no conoce todos los barrios.
    /// </summary>
    public string? Zona
    {
        get
        {
            var partes = new[] { Localidad, Provincia }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return partes.Count == 0 ? null : string.Join(", ", partes);
        }
    }
}

/// <summary>
/// HU24 (ruta óptima): convierte una dirección escrita ("Av. Colón 1200") en coordenadas,
/// con los servicios gratuitos de OpenStreetMap:
/// 1) Nominatim (el mismo de la HU23): preciso, pero pide el nombre exacto.
/// 2) Photon: si Nominatim no encuentra nada. Tolera nombres incompletos, sin tildes o con errores de tipeo.
/// Nominatim pide un pedido por segundo como máximo; Photon es más rápido.
/// Los lugares encontrados se guardan en el dispositivo: la segunda vez la ruta sale al instante.
/// </summary>
public static class Geocodificador
{
    /// <summary>Un servicio con su propia fila de pedidos y su tiempo mínimo entre pedidos.</summary>
    private sealed class Turno(TimeSpan entrePedidos)
    {
        public readonly SemaphoreSlim Fila = new(1, 1);
        public readonly TimeSpan EntrePedidos = entrePedidos;
        public DateTime Ultimo = DateTime.MinValue;
    }

    private static readonly Dictionary<string, Turno> _turnos = new()
    {
        ["nominatim.openstreetmap.org"] = new(TimeSpan.FromSeconds(1.1)),
        ["photon.komoot.io"] = new(TimeSpan.FromSeconds(0.3))
    };

    /// <summary>Cuántos lugares encontrados se guardan en el dispositivo como máximo.</summary>
    private const int MaximoGuardados = 300;

    private static string RutaGuardados => Path.Combine(Configuracion.CarpetaDatos, "lugares.json");

    // Lugares encontrados en otras sesiones (se cargan del archivo la primera vez que se usan)
    private static Dictionary<string, LugarGuardadoDatos>? _guardados;

    /// <summary>Medio lado de la "caja" de búsqueda alrededor del viaje, en grados (unos 50 km).</summary>
    private const double RadioCaja = 0.5;

    // Pedidos ya hechos (también los que no encontraron nada, como null)
    private static readonly Dictionary<string, LugarEncontrado?> _memoria = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Siglas de lugares que la gente escribe abreviados ("facu ubp") y que en el mapa
    /// figuran con el nombre completo. Se agregan acá a medida que aparezcan.
    /// </summary>
    private static readonly Dictionary<string, string> Siglas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ubp"] = "Universidad Blas Pascal",
        ["ucc"] = "Universidad Católica de Córdoba",
        ["utn"] = "Universidad Tecnológica Nacional",
        ["unc"] = "Ciudad Universitaria",
        ["ues21"] = "Universidad Siglo 21"
    };

    /// <summary>Palabras que no ayudan a encontrar el lugar ("la facu ubp" = "ubp").</summary>
    private static readonly HashSet<string> Relleno = new(StringComparer.OrdinalIgnoreCase)
    {
        "la", "el", "facu", "uni", "mi"
    };

    // Campos de la respuesta que sirven como localidad, del más común al menos común
    private static readonly string[] CamposLocalidad = { "city", "town", "village", "municipality" };

    /// <summary>
    /// Dirección del pedido a Nominatim. Si se conoce un punto de la zona, se buscan resultados
    /// alrededor; con <paramref name="acotado"/> = true, solo dentro de esa zona.
    /// </summary>
    public static Uri ArmarUrl(string texto, Coordenada? cerca, bool acotado = false)
    {
        string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&countrycodes=ar&accept-language=es&addressdetails=1"
                     + "&q=" + Uri.EscapeDataString(texto);

        if (cerca is Coordenada c)
        {
            var inv = CultureInfo.InvariantCulture;
            url += string.Create(inv, $"&viewbox={c.Longitud - RadioCaja:0.####},{c.Latitud + RadioCaja:0.####},{c.Longitud + RadioCaja:0.####},{c.Latitud - RadioCaja:0.####}");
            url += acotado ? "&bounded=1" : "&bounded=0";
        }
        return new Uri(url);
    }

    /// <summary>
    /// Dirección del pedido a Photon. Si se conoce un punto de la zona, se prefieren resultados cercanos
    /// (lat/lon) y, con <paramref name="acotado"/>, solo dentro de la caja de unos 50 km.
    /// </summary>
    public static Uri ArmarUrlPhoton(string texto, Coordenada? cerca, bool acotado = false)
    {
        string url = "https://photon.komoot.io/api/?limit=5&q=" + Uri.EscapeDataString(texto);
        if (cerca is Coordenada c)
        {
            var inv = CultureInfo.InvariantCulture;
            url += string.Create(inv, $"&lat={c.Latitud:0.####}&lon={c.Longitud:0.####}");
            if (acotado)
                url += string.Create(inv, $"&bbox={c.Longitud - RadioCaja:0.####},{c.Latitud - RadioCaja:0.####},{c.Longitud + RadioCaja:0.####},{c.Latitud + RadioCaja:0.####}");
        }
        return new Uri(url);
    }

    /// <summary>Saca el primer lugar de Argentina de la respuesta de Photon, o null si no hay ninguno.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static LugarEncontrado? LeerLugarPhoton(string json)
    {
        using var documento = JsonDocument.Parse(json);
        if (!documento.RootElement.TryGetProperty("features", out var lugares) || lugares.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var lugar in lugares.EnumerateArray())
        {
            if (!lugar.TryGetProperty("properties", out var datos) || datos.ValueKind != JsonValueKind.Object
                || !string.Equals(Texto(datos, "countrycode"), "AR", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!lugar.TryGetProperty("geometry", out var geometria)
                || !geometria.TryGetProperty("coordinates", out var coordenadas)
                || coordenadas.ValueKind != JsonValueKind.Array || coordenadas.GetArrayLength() < 2
                || coordenadas[0].ValueKind != JsonValueKind.Number || coordenadas[1].ValueKind != JsonValueKind.Number)
                continue;

            // Photon usa [longitud, latitud]
            var punto = new Coordenada(coordenadas[1].GetDouble(), coordenadas[0].GetDouble());
            string? calle = Texto(datos, "street") is string c ? $"{c} {Texto(datos, "housenumber")}".Trim() : null;
            return new LugarEncontrado(punto,
                Texto(datos, "city") ?? Texto(datos, "town") ?? Texto(datos, "village") ?? Texto(datos, "county"),
                Texto(datos, "state"),
                Texto(datos, "name") ?? calle);
        }
        return null;
    }

    /// <summary>Saca el lugar de la respuesta de Nominatim, o null si no encontró nada.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static LugarEncontrado? LeerLugar(string json)
    {
        using var documento = JsonDocument.Parse(json);
        if (documento.RootElement.ValueKind != JsonValueKind.Array || documento.RootElement.GetArrayLength() == 0)
            return null;

        var primero = documento.RootElement[0];
        if (!(primero.TryGetProperty("lat", out var lat) && primero.TryGetProperty("lon", out var lon)
              && double.TryParse(lat.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double latitud)
              && double.TryParse(lon.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double longitud)))
            return null;

        string? localidad = null, provincia = null;
        if (primero.TryGetProperty("address", out var direccion) && direccion.ValueKind == JsonValueKind.Object)
        {
            localidad = CamposLocalidad.Select(campo => Texto(direccion, campo)).FirstOrDefault(v => v is not null);
            provincia = Texto(direccion, "state");
        }
        string? nombre = Texto(primero, "name");
        if (nombre is null && primero.TryGetProperty("address", out var dir) && dir.ValueKind == JsonValueKind.Object
            && Texto(dir, "road") is string calle)
            nombre = $"{calle} {Texto(dir, "house_number")}".Trim();
        return new LugarEncontrado(new Coordenada(latitud, longitud), localidad, provincia, nombre);
    }

    /// <summary>Saca la coordenada de la respuesta de Nominatim, o null si no encontró nada.</summary>
    /// <exception cref="JsonException">La respuesta no es un JSON válido.</exception>
    public static Coordenada? LeerRespuesta(string json) => LeerLugar(json)?.Punto;

    /// <summary>
    /// Versión "para el mapa" de un texto con siglas: "facu ubp" → "Universidad Blas Pascal".
    /// Devuelve null si no tiene ninguna sigla conocida (no hay nada que cambiar).
    /// </summary>
    public static string? ExpandirSiglas(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var palabras = texto.Split(new[] { ' ', ',', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (!palabras.Any(Siglas.ContainsKey))
            return null;

        return string.Join(" ", palabras
            .Where(p => !Relleno.Contains(p))
            .Select(p => Siglas.TryGetValue(p, out var completo) ? completo : p));
    }

    private static string? Texto(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(valor.GetString())
            ? valor.GetString()!.Trim()
            : null;

    /// <summary>
    /// Busca una dirección escrita. Primero tal cual la escribió la persona (dentro de la zona del viaje,
    /// si se conoce): así "Av. Goycoechea 250" se encuentra aunque quede en otra localidad que el origen.
    /// Si no aparece, se prueba agregándole la zona de respaldo (por ejemplo, la provincia).
    /// Devuelve null si no la encuentra.
    /// </summary>
    /// <param name="cerca">Un punto de la zona del viaje: se busca solo a unos 50 km de ahí.</param>
    /// <param name="zonaRespaldo">Se agrega al texto si no se encontró solo (por ejemplo "Córdoba").</param>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<LugarEncontrado?> BuscarLugarAsync(string? texto, Coordenada? cerca = null, string? zonaRespaldo = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        string solo = texto.Trim();
        string? conZona = string.IsNullOrWhiteSpace(zonaRespaldo) || solo.Contains(zonaRespaldo.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{solo}, {zonaRespaldo.Trim()}";
        bool acotado = cerca is not null;

        // "facu ubp" no existe en el mapa, "Universidad Blas Pascal" sí
        string? expandido = ExpandirSiglas(solo);
        string? expandidoConZona = expandido is null || conZona is null ? null : $"{expandido}, {zonaRespaldo!.Trim()}";

        // Orden de los intentos: primero Nominatim con el texto más probable, enseguida Photon
        // (que tolera errores y responde rápido) y recién después las variantes con la zona.
        // Sin un punto de referencia, el texto solo podría aparecer en cualquier lugar del país:
        // en ese caso se empieza por la versión con la zona.
        var intentos = new List<Uri>();
        void Nominatim(string? consulta) { if (consulta is not null) intentos.Add(ArmarUrl(consulta, cerca, acotado)); }
        void Photon(string? consulta) { if (consulta is not null) intentos.Add(ArmarUrlPhoton(consulta, cerca, acotado)); }

        if (acotado)
        {
            Nominatim(solo); Nominatim(expandido);
            Photon(expandido ?? solo);
            Nominatim(conZona); Nominatim(expandidoConZona);
        }
        else
        {
            Nominatim(conZona); Nominatim(expandidoConZona);
            Nominatim(solo); Nominatim(expandido);
            Photon(conZona ?? expandido ?? solo);
        }

        // La respuesta final también se guarda: la próxima vez no se repiten los intentos fallidos
        string claveBusqueda = string.Create(CultureInfo.InvariantCulture,
            $"buscar|{solo}|{zonaRespaldo?.Trim()}|{cerca?.ATexto()}");
        lock (_memoria)
        {
            if (Guardados().TryGetValue(claveBusqueda, out var yaEncontrado))
                return yaEncontrado.ALugar();
        }

        foreach (Uri url in intentos)
        {
            if (await PedirAsync(url) is LugarEncontrado lugar)
            {
                lock (_memoria)
                    Guardar(claveBusqueda, lugar);
                return lugar;
            }
        }
        return null;
    }

    /// <summary>Como <see cref="BuscarLugarAsync"/>, pero devuelve solo las coordenadas.</summary>
    /// <param name="zona">La localidad, para no confundir calles de otras ciudades.</param>
    /// <param name="cerca">Un punto conocido de la zona, para preferir resultados cercanos.</param>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<Coordenada?> BuscarAsync(string? texto, string? zona = null, Coordenada? cerca = null) =>
        (await BuscarLugarAsync(texto, cerca, zona))?.Punto;

    /// <summary>
    /// Un pedido a Nominatim o a Photon, o la respuesta que ya se tenía (en memoria o guardada en el dispositivo).
    /// Si Photon falla no se corta la búsqueda: se sigue con los demás intentos.
    /// </summary>
    private static async Task<LugarEncontrado?> PedirAsync(Uri url)
    {
        string clave = url.AbsoluteUri;
        bool esPhoton = url.Host == "photon.komoot.io";

        lock (_memoria)
        {
            if (_memoria.TryGetValue(clave, out var enMemoria))
                return enMemoria;
            if (Guardados().TryGetValue(clave, out var guardado))
                return _memoria[clave] = guardado.ALugar();
        }

        var turno = _turnos[url.Host];
        await turno.Fila.WaitAsync();
        try
        {
            // Respetar el tiempo mínimo entre pedidos de cada servicio
            var espera = turno.Ultimo + turno.EntrePedidos - DateTime.UtcNow;
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera);

            using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
            pedido.Headers.UserAgent.ParseAdd(BuscadorDeBarrio.UserAgent);

            using var respuesta = await ClienteHttp.EnviarAsync(pedido);
            turno.Ultimo = DateTime.UtcNow;
            if (!respuesta.IsSuccessStatusCode)
            {
                var error = new ServidorException($"{url.Host} respondió con código {(int)respuesta.StatusCode}");
                if (!esPhoton) throw error;
                RegistroErrores.RegistrarAviso(error, "Buscar dirección en Photon");
                return null;
            }

            LugarEncontrado? resultado;
            string json = await respuesta.Content.ReadAsStringAsync();
            try
            {
                resultado = esPhoton ? LeerLugarPhoton(json) : LeerLugar(json);
            }
            catch (JsonException ex)
            {
                RegistroErrores.RegistrarError(ex, $"Leer respuesta de {url.Host} (buscar dirección)");
                if (esPhoton) return null;
                throw new ServidorException("OpenStreetMap devolvió una respuesta inválida");
            }

            lock (_memoria)
            {
                _memoria[clave] = resultado;
                // Solo se guardan los encontrados: lo que hoy no aparece, quizás mañana sí
                if (resultado is not null)
                    Guardar(clave, resultado);
            }
            return resultado;
        }
        catch (SinConexionException) when (esPhoton)
        {
            return null;
        }
        finally
        {
            turno.Fila.Release();
        }
    }

    /// <summary>Los lugares guardados en el dispositivo (se leen del archivo una sola vez).</summary>
    private static Dictionary<string, LugarGuardadoDatos> Guardados()
    {
        if (_guardados is not null)
            return _guardados;
        try
        {
            if (File.Exists(RutaGuardados))
            {
                using var archivo = File.OpenRead(RutaGuardados);
                _guardados = JsonSerializer.Deserialize(archivo, JsonPool.Default.DictionaryStringLugarGuardadoDatos);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Si el archivo está roto, se empieza de cero: solo es una ayuda para ir más rápido
            RegistroErrores.RegistrarError(ex, "Leer lugares guardados");
        }
        return _guardados ??= new Dictionary<string, LugarGuardadoDatos>();
    }

    private static void Guardar(string clave, LugarEncontrado lugar)
    {
        var guardados = Guardados();
        guardados[clave] = LugarGuardadoDatos.De(lugar);

        // Si hay demasiados, se borran los más viejos
        if (guardados.Count > MaximoGuardados)
            foreach (var viejo in guardados.OrderBy(g => g.Value.Guardado).Take(guardados.Count - MaximoGuardados).ToList())
                guardados.Remove(viejo.Key);

        try
        {
            using var archivo = File.Create(RutaGuardados);
            JsonSerializer.Serialize(archivo, guardados, JsonPool.Default.DictionaryStringLugarGuardadoDatos);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RegistroErrores.RegistrarError(ex, "Guardar lugares encontrados");
        }
    }
}
