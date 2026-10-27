namespace PoolCompartido;

/// <summary>
/// HU24: los diálogos del punto de encuentro. Los usan "Viajes disponibles" (después de anotarse)
/// y "Mis viajes" (para cambiarlo o, si sos el conductor, para ver dónde buscar a cada uno).
/// Toda la lógica está en ServicioEncuentros; acá solo se pregunta, se muestra y se atrapan los errores.
/// </summary>
internal static class PuntoDeEncuentro
{
    private static ServicioEncuentros Servicio => ServicioEncuentros.Instancia;

    private const string OpcionUbicacion = "Usar mi ubicación actual";
    private const string OpcionEscribir = "Escribir una dirección";

    /// <summary>
    /// Le pregunta al pasajero cómo indicar dónde lo pasan a buscar: con su ubicación (HU23)
    /// o escribiendo una dirección. "Después" no guarda nada.
    /// </summary>
    public static async Task PedirYGuardarAsync(Page pagina, Viaje viaje)
    {
        string? opcion = await pagina.DisplayActionSheetAsync(
            $"¿Dónde te pasa a buscar {viaje.Conductor}? Lo ven solo el conductor y los pasajeros de este viaje.",
            "Después", null, OpcionUbicacion, OpcionEscribir);

        if (opcion == OpcionUbicacion)
        {
            // Si la ubicación falla, se le ofrece escribirla a mano
            if (await GuardarUbicacionAsync(pagina, viaje)) return;
        }
        else if (opcion != OpcionEscribir)
        {
            return;   // eligió "Después" o cerró el cartel
        }

        await EscribirYGuardarAsync(pagina, viaje);
    }

    /// <summary>HU23: guarda la ubicación actual como punto de encuentro. Devuelve false si no se pudo.</summary>
    private static async Task<bool> GuardarUbicacionAsync(Page pagina, Viaje viaje)
    {
        try
        {
            UbicacionActual ubicacion = await ServicioUbicacion.ObtenerAsync();
            await Servicio.GuardarMiUbicacionAsync(viaje, ubicacion);
            await pagina.DisplayAlertAsync("Listo",
                $"Guardamos tu ubicación (en {ubicacion.Barrio}). Ya aparece como parada en la ruta del viaje.", "OK");
            return true;
        }
        catch (UbicacionNoDisponibleException ex)
        {
            // Errores esperables (sin permiso, GPS apagado...): aviso, no falla del sistema
            RegistroErrores.RegistrarAviso(ex, "Usar mi ubicación");
            await pagina.DisplayAlertAsync("No se pudo usar tu ubicación", $"{ex.Message}\n\nEscribí la dirección a mano.", "OK");
            return false;
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Usar mi ubicación");
            await pagina.DisplayAlertAsync("Punto de encuentro", ex.Message, "OK");
            return true;   // no tiene sentido escribirla: no está anotado
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Usar mi ubicación");
            return true;
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Usar mi ubicación");
            await pagina.DisplayAlertAsync("No se pudo usar tu ubicación", ex.Message, "OK");
            return false;
        }
    }

    /// <summary>El pasajero escribe la dirección (si ya tenía una, se la muestra para corregirla).</summary>
    private static async Task EscribirYGuardarAsync(Page pagina, Viaje viaje)
    {
        // Si ya lo había cargado, se muestra para que lo pueda corregir
        string? actual = null;
        try
        {
            actual = await Servicio.ObtenerMiPuntoAsync(viaje);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            // No es grave: se puede escribir igual, y si no hay internet va a fallar al guardar
            RegistroErrores.RegistrarError(ex, "Leer punto de encuentro");
        }

        string? texto = await pagina.DisplayPromptAsync("Punto de encuentro",
            $"Escribí dónde te pasa a buscar {viaje.Conductor}: calle y altura, o una esquina. " +
            "Si queda en otra localidad, agregala al final. Lo ven solo el conductor y los pasajeros de este viaje.",
            accept: "Guardar", cancel: "Después",
            placeholder: "Ej: Av. Goycoechea 250, Villa Allende",
            maxLength: ServicioEncuentros.LargoMaximo,
            initialValue: actual ?? "");

        if (texto is null) return;   // eligió "Después"

        try
        {
            LugarEncontrado? lugar = await Servicio.GuardarMiPuntoAsync(viaje, texto);

            // Se le dice dónde lo ubicamos, así puede darse cuenta si quedó en otra ciudad
            string mensaje = lugar?.Zona is string zona
                ? $"Guardamos tu punto de encuentro y lo ubicamos en {zona}. Ya aparece como parada en la ruta del viaje."
                : "Guardamos tu punto de encuentro, pero no lo encontramos en el mapa. " +
                  "Revisá cómo está escrito o agregale la localidad (por ejemplo: \"Av. Goycoechea 250, Villa Allende\").";
            await pagina.DisplayAlertAsync("Listo", mensaje, "OK");
        }
        catch (ArgumentException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Guardar punto de encuentro");
            await pagina.DisplayAlertAsync("Punto de encuentro", ex.Message, "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Guardar punto de encuentro");
            await pagina.DisplayAlertAsync("Punto de encuentro", ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Guardar punto de encuentro");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Guardar punto de encuentro");
            await pagina.DisplayAlertAsync("No se pudo guardar", ex.Message, "OK");
        }
    }

    /// <summary>Al conductor: la lista de pasajeros con su punto; al elegir uno, se abre en Google Maps.</summary>
    public static async Task MostrarAlConductorAsync(Page pagina, Viaje viaje)
    {
        IReadOnlyList<PuntoPasajero> puntos;
        try
        {
            puntos = await Servicio.ObtenerPuntosDeMiViajeAsync(viaje);
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Ver puntos de encuentro");
            await pagina.DisplayAlertAsync("Puntos de encuentro", ex.Message, "OK");
            return;
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Ver puntos de encuentro");
            return;
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Ver puntos de encuentro");
            await pagina.DisplayAlertAsync("No se pudieron cargar", ex.Message, "OK");
            return;
        }

        if (puntos.Count == 0)
        {
            await pagina.DisplayAlertAsync("Puntos de encuentro", "Todavía no hay pasajeros anotados.", "OK");
            return;
        }

        string[] opciones = puntos
            .Select(p => p.Texto is null ? $"{p.Nombre}: todavía no lo cargó" : $"{p.Nombre}: {p.Texto}")
            .ToArray();

        string? elegido = await pagina.DisplayActionSheetAsync("¿A quién vas a buscar?", "Cerrar", null, opciones);
        int indice = Array.IndexOf(opciones, elegido);
        if (indice < 0 || puntos[indice].Texto is not string lugar) return;
        var punto = puntos[indice];

        try
        {
            if (punto.MostrarCoordenadas && punto.Lugar is Coordenada exacto)
                await ServicioMapas.AbrirCoordenadasAsync(exacto.Latitud, exacto.Longitud);   // HU23: compartió su ubicación exacta
            else
                // Con la localidad del punto, no la del origen: el pasajero puede vivir en otra localidad
                await ServicioMapas.AbrirLugarAsync(lugar, punto.Zona);
        }
        catch (ArgumentException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Abrir punto de encuentro");
            await pagina.DisplayAlertAsync("No se puede mostrar", ex.Message, "OK");
        }
        catch (MapaNoDisponibleException ex)
        {
            RegistroErrores.RegistrarError(ex, "Abrir punto de encuentro");
            await pagina.DisplayAlertAsync("Mapa no disponible", ex.Message, "OK");
        }
    }

    /// <summary>
    /// HU24: abre la ruta del viaje en Google Maps. Si el usuario participa del viaje,
    /// la ruta pasa por los puntos de encuentro de los pasajeros. Si no se pueden leer
    /// (por ejemplo, sin internet), se abre igual la ruta simple en vez de fallar.
    /// </summary>
    public static async Task AbrirRutaAsync(Page pagina, Viaje viaje, Button? boton = null)
    {
        // Ordenar las paradas puede tardar unos segundos: se avisa en el botón
        string? textoBoton = boton?.Text;
        if (boton is not null)
        {
            boton.IsEnabled = false;
            boton.Text = "Armando ruta...";
        }

        IReadOnlyList<PuntoPasajero> paradas = Array.Empty<PuntoPasajero>();
        try
        {
            // Ruta óptima: las paradas en el orden más corto
            paradas = await Servicio.ObtenerParadasOrdenadasAsync(viaje);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException or SesionVencidaException)
        {
            RegistroErrores.RegistrarError(ex, "Leer paradas de la ruta");
        }

        try
        {
            await ServicioMapas.AbrirRutaAsync(viaje, paradas);
        }
        catch (ArgumentException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Ver ruta");
            await pagina.DisplayAlertAsync("No se puede mostrar la ruta", ex.Message, "OK");
        }
        catch (MapaNoDisponibleException ex)
        {
            RegistroErrores.RegistrarError(ex, "Ver ruta");
            await pagina.DisplayAlertAsync("Mapa no disponible", ex.Message, "OK");
        }
        finally
        {
            if (boton is not null)
            {
                boton.Text = textoBoton;
                boton.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// Borra el punto del usuario al salirse del viaje. Si falla no se le muestra nada:
    /// ya se salió, y el conductor solo ve los puntos de quienes siguen anotados.
    /// </summary>
    public static async Task BorrarSinFallarAsync(Viaje viaje)
    {
        try
        {
            await Servicio.BorrarMiPuntoAsync(viaje);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException or SesionVencidaException)
        {
            RegistroErrores.RegistrarError(ex, "Borrar punto de encuentro");
        }
    }
}
