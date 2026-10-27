namespace PoolCompartido;

/// <summary>Punto de encuentro de un pasajero, listo para mostrarle al conductor.</summary>
/// <param name="Texto">null si el pasajero todavía no lo cargó.</param>
/// <param name="Latitud">HU23: coordenadas del punto (exactas si usó su ubicación).</param>
/// <param name="Aproximado">true si las coordenadas se calcularon a partir de la dirección escrita.</param>
/// <param name="Zona">Localidad y provincia de la dirección escrita ("Villa Allende, Córdoba"), para Google Maps.</param>
public record PuntoPasajero(string Uid, string Nombre, string? Texto, double? Latitud = null, double? Longitud = null,
                            bool Aproximado = false, string? Zona = null)
{
    public Coordenada? Lugar => Latitud is double lat && Longitud is double lon ? new Coordenada(lat, lon) : null;

    /// <summary>Si en el mapa conviene usar las coordenadas (solo si son exactas, de "Usar mi ubicación").</summary>
    public bool MostrarCoordenadas => Lugar is not null && !Aproximado;
}

/// <summary>
/// HU24: punto de encuentro. Cada pasajero indica dónde lo pasan a buscar.
/// Se guarda en Firebase aparte del viaje (/encuentros/{viajeId}/{uid}) y las reglas
/// solo dejan leerlo a los que participan de ese viaje (el conductor y los pasajeros),
/// para armar la ruta con todas las paradas: la dirección no queda a la vista del resto de los usuarios.
/// </summary>
public class ServicioEncuentros
{
    /// <summary>Largo máximo del texto del punto de encuentro.</summary>
    public const int LargoMaximo = 100;

    public static ServicioEncuentros Instancia { get; } = new();

    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());

    private ServicioEncuentros() { }

    private static Usuario UsuarioRequerido =>
        ServicioAutenticacion.Instancia.UsuarioActual ?? throw new SesionVencidaException();

    /// <summary>
    /// El pasajero guarda (o cambia) dónde lo pasan a buscar.
    /// Devuelve dónde se ubicó la dirección en el mapa, o null si no se encontró (se guarda igual).
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ArgumentException">El texto está vacío o es demasiado largo.</exception>
    /// <exception cref="NoAnotadoException">No está anotado en ese viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<LugarEncontrado?> GuardarMiPuntoAsync(Viaje viaje, string? texto)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        string limpio = ValidarTexto(texto, "Escribí dónde te pasan a buscar.");
        var usuario = UsuarioRequerido;

        if (!viaje.PasajerosPorUid.ContainsKey(usuario.Uid))
            throw new NoAnotadoException();

        // Ruta óptima: se ubica la dirección ahora, así después la ruta se arma rápido.
        // Se busca cerca del viaje, pero NO solo en la localidad de origen: el pasajero puede vivir
        // en otra localidad del camino (antes se le agregaba el barrio de origen y Maps no la encontraba).
        // Si no se encuentra, se guarda igual (esa parada va al final de la ruta).
        LugarEncontrado? zonaViaje = null, lugar = null;
        try
        {
            zonaViaje = await UbicarViajeAsync(viaje);
            lugar = await Geocodificador.BuscarLugarAsync(limpio, zonaViaje?.Punto, zonaViaje?.Provincia);
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            RegistroErrores.RegistrarAviso(ex, "Ubicar punto de encuentro en el mapa");
        }

        await _remoto.GuardarEncuentroAsync(viaje.Id, usuario.Uid, new PuntoEncuentroDatos
        {
            Texto = limpio,
            Lat = lugar?.Punto.Latitud,
            Lon = lugar?.Punto.Longitud,
            Aproximado = lugar is null ? null : true,
            // Si no se encontró, al menos la provincia del viaje, para que Maps no la busque en todo el país
            Zona = lugar?.Zona ?? zonaViaje?.Provincia
        });
        return lugar;
    }

    /// <summary>
    /// Dónde queda el viaje en el mapa (localidad y provincia), para buscar las direcciones cerca.
    /// Se prueba con el origen, después con el punto de partida y por último con el destino.
    /// Las búsquedas quedan guardadas, así que solo la primera vez hace pedidos a internet.
    /// </summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    /// <exception cref="ServidorException">OpenStreetMap respondió con un error.</exception>
    public static async Task<LugarEncontrado?> UbicarViajeAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        return await Geocodificador.BuscarLugarAsync(viaje.Origen)
               ?? (viaje.TienePuntoPartida ? await Geocodificador.BuscarLugarAsync(viaje.PuntoPartida) : null)
               ?? await Geocodificador.BuscarLugarAsync(viaje.Destino);
    }

    /// <summary>
    /// HU23: el pasajero comparte su ubicación actual como punto de encuentro.
    /// Se guardan las coordenadas exactas (privadas) y un texto con el barrio.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje o la ubicación.</exception>
    /// <exception cref="NoAnotadoException">No está anotado en ese viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task GuardarMiUbicacionAsync(Viaje viaje, UbicacionActual ubicacion)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        ArgumentNullException.ThrowIfNull(ubicacion);
        var usuario = UsuarioRequerido;

        if (!viaje.PasajerosPorUid.ContainsKey(usuario.Uid))
            throw new NoAnotadoException();

        await _remoto.GuardarEncuentroAsync(viaje.Id, usuario.Uid, new PuntoEncuentroDatos
        {
            Texto = $"Mi ubicación (en {ubicacion.Barrio})",
            Lat = ubicacion.Latitud,
            Lon = ubicacion.Longitud
        });
    }

    /// <summary>El punto que cargó el usuario actual en ese viaje, o null si todavía no lo cargó.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<string?> ObtenerMiPuntoAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        var datos = await _remoto.ObtenerEncuentroAsync(viaje.Id, UsuarioRequerido.Uid);
        return datos?.Texto;
    }

    /// <summary>
    /// Para el conductor: los pasajeros de su viaje, cada uno con su punto de encuentro
    /// (o null si no lo cargó).
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="NoEsConductorException">El viaje no es del usuario actual.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<IReadOnlyList<PuntoPasajero>> ObtenerPuntosDeMiViajeAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        if (viaje.ConductorUid != UsuarioRequerido.Uid)
            throw new NoEsConductorException("Solo el conductor del viaje puede ver los puntos de encuentro.");

        var puntos = await _remoto.ObtenerEncuentrosAsync(viaje.Id);

        return viaje.PasajerosPorUid
            .Select(p => puntos.TryGetValue(p.Key, out var datos)
                ? new PuntoPasajero(p.Key, p.Value, datos.Texto, datos.Lat, datos.Lon, datos.Aproximado == true, datos.Zona)
                : new PuntoPasajero(p.Key, p.Value, null))
            .OrderBy(p => p.Nombre)
            .ToList();
    }

    /// <summary>
    /// HU24: las paradas del viaje (los puntos que cargaron los pasajeros), para armar la ruta.
    /// Las ven el conductor y los pasajeros de ese viaje, así los que viven cerca se pueden juntar.
    /// Quien no participa del viaje no recibe ninguna (las reglas tampoco se lo permitirían).
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<IReadOnlyList<PuntoPasajero>> ObtenerParadasAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        if (!viaje.EsDelUsuarioActual && !viaje.EstoyAnotado)
            return Array.Empty<PuntoPasajero>();

        var puntos = await _remoto.ObtenerEncuentrosAsync(viaje.Id);

        // Solo los que siguen anotados y cargaron su punto, en el orden en que se anotaron
        return viaje.PasajerosPorUid
            .Where(p => puntos.ContainsKey(p.Key))
            .Select(p => new PuntoPasajero(p.Key, p.Value, puntos[p.Key].Texto, puntos[p.Key].Lat, puntos[p.Key].Lon,
                                           puntos[p.Key].Aproximado == true, puntos[p.Key].Zona))
            .ToList();
    }

    /// <summary>
    /// Si el origen o el destino se ubican a más de esta distancia de las paradas,
    /// seguramente OpenStreetMap encontró otro lugar con el mismo nombre: no se usa.
    /// </summary>
    private const double DistanciaMaximaCreible = 100_000;   // 100 km

    /// <summary>
    /// HU24 (ruta óptima): las paradas ordenadas para hacer el camino más corto,
    /// desde el punto de partida hasta el destino, sin ir y volver.
    /// Las paradas que no se pudieron ubicar en el mapa van al final.
    /// Nunca falla por el orden: si algo no anda, devuelve las paradas en el orden en que se anotaron.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet para leer las paradas.</exception>
    public async Task<IReadOnlyList<PuntoPasajero>> ObtenerParadasOrdenadasAsync(Viaje viaje)
    {
        var paradas = (await ObtenerParadasAsync(viaje)).ToList();

        try
        {
            // Puntos escritos antes de este cambio (sin coordenadas o sin zona): se ubican ahora
            if (paradas.Any(FaltaUbicar))
            {
                var zonaViaje = await UbicarViajeAsync(viaje);
                for (int i = 0; i < paradas.Count; i++)
                {
                    if (!FaltaUbicar(paradas[i])) continue;
                    var lugar = await Geocodificador.BuscarLugarAsync(paradas[i].Texto, zonaViaje?.Punto, zonaViaje?.Provincia);
                    paradas[i] = paradas[i] with
                    {
                        Latitud = paradas[i].Latitud ?? lugar?.Punto.Latitud,
                        Longitud = paradas[i].Longitud ?? lugar?.Punto.Longitud,
                        Aproximado = paradas[i].Lugar is null ? lugar is not null : paradas[i].Aproximado,
                        Zona = lugar?.Zona ?? zonaViaje?.Provincia
                    };
                }
            }

            if (paradas.Count < 2)
                return paradas;

            var conLugar = paradas.Where(p => p.Lugar is not null).ToList();
            var sinLugar = paradas.Where(p => p.Lugar is null).ToList();
            if (conLugar.Count < 2)
                return conLugar.Concat(sinLugar).ToList();

            // Origen y destino, preferentemente cerca de las paradas
            Coordenada referencia = conLugar[0].Lugar!.Value;
            Coordenada? inicio = Creible(await Geocodificador.BuscarAsync(
                viaje.TienePuntoPartida ? viaje.PuntoPartida : viaje.Origen,
                viaje.TienePuntoPartida ? viaje.Origen : null, referencia), referencia);
            Coordenada? fin = Creible(await Geocodificador.BuscarAsync(viaje.Destino, null, referencia), referencia);

            var orden = await OptimizadorDeRuta.OrdenarAsync(inicio, fin, conLugar.Select(p => p.Lugar!.Value).ToList());
            return orden.Select(i => conLugar[i]).Concat(sinLugar).ToList();
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            RegistroErrores.RegistrarAviso(ex, "Ruta óptima: se usa el orden en que se anotaron");
            return paradas;
        }
    }

    /// <summary>Una dirección escrita que todavía no tiene coordenadas o zona.</summary>
    private static bool FaltaUbicar(PuntoPasajero p) =>
        p.Texto is not null && (p.Lugar is null || (p.Aproximado && p.Zona is null));

    private static Coordenada? Creible(Coordenada? lugar, Coordenada referencia) =>
        lugar is Coordenada c && OptimizadorDeRuta.DistanciaRecta(c, referencia) <= DistanciaMaximaCreible ? c : null;

    /// <summary>
    /// Al salirse del viaje se borra el punto de encuentro, así el conductor no ve
    /// la dirección de alguien que ya no viaja con él.
    /// </summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task BorrarMiPuntoAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        await _remoto.BorrarEncuentroAsync(viaje.Id, UsuarioRequerido.Uid);
    }

    /// <summary>Valida el texto de un punto (de encuentro o de partida) y lo devuelve sin espacios de más.</summary>
    /// <exception cref="ArgumentException">Vacío o de más de 100 caracteres.</exception>
    public static string ValidarTexto(string? texto, string mensajeSiFalta)
    {
        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException(mensajeSiFalta);

        string limpio = texto.Trim();
        if (limpio.Length > LargoMaximo)
            throw new ArgumentException($"El punto puede tener hasta {LargoMaximo} caracteres.");

        return limpio;
    }
}
