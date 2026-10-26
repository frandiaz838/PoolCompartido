namespace PoolCompartido;

/// <summary>Punto de encuentro de un pasajero, listo para mostrarle al conductor.</summary>
/// <param name="Texto">null si el pasajero todavía no lo cargó.</param>
public record PuntoPasajero(string Uid, string Nombre, string? Texto);

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
            .Select(p => new PuntoPasajero(p.Key, p.Value,
                puntos.TryGetValue(p.Key, out var datos) ? datos.Texto : null))
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
