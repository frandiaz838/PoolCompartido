namespace PoolCompartido;

/// <summary>Punto de encuentro de un pasajero, listo para mostrarle al conductor.</summary>
/// <param name="Texto">null si el pasajero todavía no lo cargó.</param>
/// <param name="Latitud">HU23: coordenadas exactas, si el pasajero usó su ubicación.</param>
public record PuntoPasajero(string Uid, string Nombre, string? Texto, double? Latitud = null, double? Longitud = null);

/// <summary>
/// HU24: punto de encuentro privado. Cada pasajero indica dónde lo pasan a buscar.
/// Se guarda en Firebase aparte del viaje (/encuentros/{viajeId}/{uid}) y las reglas
/// solo dejan leerlo al conductor de ese viaje y al pasajero que lo cargó:
/// la dirección nunca queda a la vista de todos.
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

    /// <summary>El pasajero guarda (o cambia) dónde lo pasan a buscar.</summary>
    /// <exception cref="ArgumentNullException">No se indicó el viaje.</exception>
    /// <exception cref="ArgumentException">El texto está vacío o es demasiado largo.</exception>
    /// <exception cref="NoAnotadoException">No está anotado en ese viaje.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task GuardarMiPuntoAsync(Viaje viaje, string? texto)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        string limpio = ValidarTexto(texto, "Escribí dónde te pasan a buscar.");
        var usuario = UsuarioRequerido;

        if (!viaje.PasajerosPorUid.ContainsKey(usuario.Uid))
            throw new NoAnotadoException();

        await _remoto.GuardarEncuentroAsync(viaje.Id, usuario.Uid, new PuntoEncuentroDatos { Texto = limpio });
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
                ? new PuntoPasajero(p.Key, p.Value, datos.Texto, datos.Lat, datos.Lon)
                : new PuntoPasajero(p.Key, p.Value, null))
            .OrderBy(p => p.Nombre)
            .ToList();
    }

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
