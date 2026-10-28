namespace PoolCompartido;

/// <summary>
/// HU32: teléfono de contacto (opcional), para llamar o escribir por WhatsApp a quien viaja con vos
/// (por ejemplo, el conductor llegó y el pasajero no aparece).
/// Privacidad: el número se guarda en /telefonos/{uid}, que solo lee el dueño, y se copia
/// a /contactos/{viajeId}/{uid} solo en los viajes donde participa. Las reglas de Firebase
/// solo dejan leer /contactos/{viajeId} al conductor y a los pasajeros de ese viaje
/// (lo mismo que los puntos de encuentro, HU24). Nunca queda a la vista de todos.
/// </summary>
public class ServicioContactos
{
    public static ServicioContactos Instancia { get; } = new();

    private readonly RepositorioFirebase _remoto = new(() => ServicioAutenticacion.Instancia.ObtenerTokenAsync());

    private ServicioContactos() { }

    /// <summary>Mi teléfono, ya normalizado ("+5493511234567"), o null si no lo cargué.</summary>
    public string? MiTelefono { get; private set; }

    private static string UidRequerido => ServicioAutenticacion.UidActual ?? throw new SesionVencidaException();

    /// <summary>
    /// Deja el número en formato internacional argentino para celular (el que necesitan el teléfono y WhatsApp):
    /// "351 123-4567" → "+5493511234567"; "0351 15 123 4567" → "+5493511234567"; "+54 9 351 1234567" se respeta.
    /// Devuelve null si está vacío.
    /// </summary>
    /// <exception cref="FormatException">No parece un número de teléfono.</exception>
    public static string? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        string limpio = texto.Trim();
        if (limpio.Any(c => !char.IsDigit(c) && " +-().".IndexOf(c) < 0))
            throw new FormatException("El teléfono solo puede tener números (y espacios, guiones o +).");

        bool internacional = limpio.StartsWith('+');
        string digitos = new string(limpio.Where(char.IsDigit).ToArray());

        if (!internacional)
        {
            // Argentina: sin el 0 de larga distancia y sin el 15 del celular
            if (digitos.StartsWith('0')) digitos = digitos[1..];
            // "351 15 1234567", "11 15 12345678", "3543 15 123456": código de área + 15 + número → se saca el 15
            if (digitos.Length == 12)
            {
                foreach (int area in new[] { 3, 2, 4 })
                {
                    if (digitos.Substring(area, 2) == "15")
                    {
                        digitos = digitos[..area] + digitos[(area + 2)..];
                        break;
                    }
                }
            }
            if (digitos.Length == 10) digitos = "549" + digitos;
            else if (digitos.StartsWith("54") && !digitos.StartsWith("549") && digitos.Length == 12) digitos = "549" + digitos[2..];
        }

        if (digitos.Length is < 8 or > 15)
            throw new FormatException("Ese número no parece válido. Escribilo con código de área, por ejemplo 351 123 4567.");
        return "+" + digitos;
    }

    /// <summary>"+5493511234567" → "+54 9 351 123-4567" (solo para mostrar).</summary>
    public static string Mostrar(string numero) =>
        numero.StartsWith("+549") && numero.Length == 14
            ? $"+54 9 {numero[4..7]} {numero[7..10]}-{numero[10..]}"
            : numero;

    /// <summary>Al iniciar sesión: trae mi teléfono y lo deja compartido en mis viajes activos.</summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task CargarAsync(IEnumerable<Viaje> viajes)
    {
        MiTelefono = await _remoto.ObtenerTelefonoAsync(UidRequerido);
        if (MiTelefono is not null)
            foreach (var viaje in MisViajesActivos(viajes))
                await _remoto.GuardarContactoAsync(viaje.Id, UidRequerido, MiTelefono);
    }

    /// <summary>Guarda (o borra, si viene vacío) mi teléfono y lo actualiza en todos mis viajes activos.</summary>
    /// <exception cref="FormatException">No parece un número de teléfono.</exception>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<string?> GuardarAsync(string? texto, IEnumerable<Viaje> viajes)
    {
        string? numero = Normalizar(texto);   // validación temprana
        string uid = UidRequerido;

        if (numero is null) await _remoto.BorrarTelefonoAsync(uid);
        else await _remoto.GuardarTelefonoAsync(uid, numero);
        MiTelefono = numero;

        foreach (var viaje in MisViajesActivos(viajes))
        {
            if (numero is null) await _remoto.BorrarContactoAsync(viaje.Id, uid);
            else await _remoto.GuardarContactoAsync(viaje.Id, uid, numero);
        }
        return numero;
    }

    /// <summary>Al anotarse o publicar: comparte mi teléfono en ese viaje. Si falla no se muestra nada (es opcional).</summary>
    public async Task CompartirEnViajeSinFallarAsync(Viaje viaje)
    {
        if (MiTelefono is null) return;
        try
        {
            await _remoto.GuardarContactoAsync(viaje.Id, UidRequerido, MiTelefono);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException or SesionVencidaException)
        {
            RegistroErrores.RegistrarAviso(ex, "Compartir teléfono en el viaje");
        }
    }

    /// <summary>Al salirse de un viaje: deja de compartir el teléfono ahí.</summary>
    public async Task QuitarDeViajeSinFallarAsync(Viaje viaje)
    {
        try
        {
            await _remoto.BorrarContactoAsync(viaje.Id, UidRequerido);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException or SesionVencidaException)
        {
            RegistroErrores.RegistrarAviso(ex, "Dejar de compartir teléfono");
        }
    }

    /// <summary>
    /// Las personas del viaje (menos yo) que cargaron su teléfono: el conductor primero.
    /// Solo para quien participa del viaje (las reglas tampoco dejarían leerlo).
    /// </summary>
    /// <exception cref="SinConexionException">No hay internet.</exception>
    public async Task<IReadOnlyList<(string Nombre, string Numero, bool EsConductor)>> ObtenerDelViajeAsync(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        if (!viaje.EsDelUsuarioActual && !viaje.EstoyAnotado)
            return Array.Empty<(string, string, bool)>();

        string yo = UidRequerido;
        var numeros = await _remoto.ObtenerContactosAsync(viaje.Id);
        var personas = new[] { (Uid: viaje.ConductorUid, Nombre: viaje.Conductor, EsConductor: true) }
            .Concat(viaje.PasajerosPorUid.Select(p => (Uid: p.Key, Nombre: p.Value, EsConductor: false)));

        return personas
            .Where(p => p.Uid != yo && numeros.ContainsKey(p.Uid))
            .Select(p => (p.Nombre, numeros[p.Uid], p.EsConductor))
            .ToList();
    }

    /// <summary>Dirección para llamar ("tel:+549...").</summary>
    public static Uri UrlLlamar(string numero) => new($"tel:{numero}");

    /// <summary>Dirección para abrir el chat de WhatsApp con esa persona (wa.me usa el número sin el +).</summary>
    public static Uri UrlWhatsApp(string numero, string? mensaje = null) =>
        new($"https://wa.me/{numero.TrimStart('+')}" + (mensaje is null ? "" : "?text=" + Uri.EscapeDataString(mensaje)));

    private static IEnumerable<Viaje> MisViajesActivos(IEnumerable<Viaje> viajes) =>
        viajes.Where(v => (v.EsDelUsuarioActual || v.EstoyAnotado) && !v.Terminado && !v.Cancelado);

    /// <summary>Al cerrar sesión.</summary>
    public void Reiniciar() => MiTelefono = null;
}
