using System.Collections.ObjectModel;

namespace PoolCompartido;

public partial class MisViajesPage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;
    private readonly ObservableCollection<Viaje> _misViajes = new();

    public MisViajesPage()
    {
        InitializeComponent();

        // HU26: cartel de avisos y campanita
        Notificaciones.Preparar(this);

        listaMisViajes.ItemsSource = _misViajes;

        // Esta pantalla también se suscribe a la tabla de eventos:
        // cada vez que se publica, alguien se anota o se cancela, la lista se redibuja sola.
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_PUBLICADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_RESERVADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.ASIENTO_LIBERADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_CANCELADO, _ => Redibujar());
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_INICIADO, _ => Redibujar());     // HU15: pasa arriba
        _servicio.Eventos.Registrar(ViajeEvento.VIAJE_FINALIZADO, _ => Redibujar());   // HU15: va al historial

        // Pasó el tiempo: un viaje salió (pasa arriba, "En curso") o terminó (se oculta)
        _servicio.EstadosCambiados += (sender, e) => Redibujar();

        // HU19: cambió el tema o la paleta: las tarjetas se vuelven a dibujar con los colores nuevos
        ServicioApariencia.Eventos.Registrar(EventoApariencia.APARIENCIA_CAMBIADA, _ => Redibujar());

        Redibujar();
    }

    // Cada vez que se vuelve a esta pantalla, se redibuja (por si cambiaron los datos)
    protected override void OnAppearing()
    {
        base.OnAppearing();
        var usuario = ServicioAutenticacion.Instancia.UsuarioActual;
        lblSaludo.Text = usuario is null ? "Mis viajes" : $"Hola, {usuario.Nombre}";
        Redibujar();
        // HU26: se llegó tocando un aviso: se muestra ese viaje
        Notificaciones.MostrarViajePendiente(listaMisViajes, _misViajes);
    }

    private async void OnPerfilClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new PerfilPage());
    }

    /// <summary>
    /// HU32: llamar o escribir por WhatsApp a alguien del viaje (por ejemplo, al llegar al punto de encuentro).
    /// Solo aparecen los que cargaron su teléfono en Mi perfil.
    /// </summary>
    private async void OnContactarClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje }) return;

        IReadOnlyList<(string Nombre, string Numero, bool EsConductor)> personas;
        try
        {
            personas = await ServicioContactos.Instancia.ObtenerDelViajeAsync(viaje);
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Ver teléfonos del viaje");
            return;
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Ver teléfonos del viaje");
            await DisplayAlertAsync("No se pudieron cargar los teléfonos", ex.Message, "OK");
            return;
        }

        if (personas.Count == 0)
        {
            await DisplayAlertAsync("Contactar",
                "Todavía nadie de este viaje cargó su teléfono. Mientras tanto podés usar el chat del viaje." +
                (ServicioContactos.Instancia.MiTelefono is null ? "\n\nVos podés cargar el tuyo en Mi perfil." : ""), "OK");
            return;
        }

        // Dos opciones por persona: llamar y WhatsApp
        var opciones = new List<(string Texto, Uri Destino)>();
        foreach (var (nombre, numero, esConductor) in personas)
        {
            string quien = esConductor ? $"{nombre} (conductor)" : nombre;
            opciones.Add(($"📞 Llamar a {quien}", ServicioContactos.UrlLlamar(numero)));
            opciones.Add(($"💬 WhatsApp a {quien}", ServicioContactos.UrlWhatsApp(numero, $"Hola! Te escribo por el viaje {viaje.Ruta} de Pool Compartido.")));
        }

        string? elegida = await DisplayActionSheetAsync("¿A quién querés contactar?", "Cancelar", null,
                                                        opciones.Select(o => o.Texto).ToArray());
        var opcion = opciones.FirstOrDefault(o => o.Texto == elegida);
        if (opcion.Destino is null) return;

        try
        {
            if (!await Launcher.Default.OpenAsync(opcion.Destino))
                throw new FeatureNotSupportedException();
        }
        catch (FeatureNotSupportedException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Llamar o abrir WhatsApp");
            await DisplayAlertAsync("No se pudo abrir",
                "Este dispositivo no puede llamar ni abrir WhatsApp. Probá desde el celular.", "OK");
        }
    }

    /// <summary>HU25: abre el chat grupal del viaje.</summary>
    private async void OnChatClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje }) return;
        await Navigation.PushAsync(new ChatPage(viaje));
    }

    /// <summary>HU22: abre la ruta del viaje en Google Maps.</summary>
    private async void OnVerRutaClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        // HU24: si participás del viaje, la ruta pasa por los puntos de encuentro, en el orden más corto
        await PuntoDeEncuentro.AbrirRutaAsync(this, viaje, boton);
    }

    /// <summary>HU5: el conductor cancela su viaje.</summary>
    private async void OnCancelarViajeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        string detalle = viaje.Pasajeros.Count == 0
            ? "No hay pasajeros anotados."
            : $"Se va a avisar a {viaje.Pasajeros.Count} pasajero(s): {string.Join(", ", viaje.Pasajeros)}.";

        bool confirmar = await DisplayAlertAsync("Cancelar viaje",
            $"¿Seguro que querés cancelar el viaje {viaje.Ruta} del {viaje.Fecha:dd/MM}? {detalle}",
            "Sí, cancelar", "No");
        if (!confirmar) return;

        boton.IsEnabled = false;
        try
        {
            await _servicio.CancelarViajeAsync(viaje);
            await ServicioChat.Instancia.AvisarAlGrupoAsync(viaje, "canceló el viaje");   // HU25
            await DisplayAlertAsync("Viaje cancelado", "Listo, el viaje quedó cancelado y los pasajeros van a recibir el aviso.", "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Cancelar viaje");
            await DisplayAlertAsync("No se pudo cancelar", ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Cancelar viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Cancelar viaje");
            await DisplayAlertAsync("No se pudo cancelar", ex.Message, "OK");
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>HU4: el pasajero se sale del viaje y libera su asiento.</summary>
    private async void OnSalirmeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        bool confirmar = await DisplayAlertAsync("Salirme del viaje",
            $"¿Seguro que querés salirte del viaje de {viaje.Conductor} {viaje.Ruta} del {viaje.Fecha:dd/MM}? Tu lugar va a quedar libre.",
            "Sí, salirme", "No");
        if (!confirmar) return;

        boton.IsEnabled = false;   // evita que se toque dos veces mientras espera al servidor
        try
        {
            // HU25: el aviso al grupo va antes, porque después de salirse ya no puede escribir en el chat
            await ServicioChat.Instancia.AvisarAlGrupoAsync(viaje, "se bajó del viaje");
            await _servicio.SalirseAsync(viaje);
            await PuntoDeEncuentro.BorrarSinFallarAsync(viaje);   // HU24: el conductor ya no necesita su dirección
            await ServicioContactos.Instancia.QuitarDeViajeSinFallarAsync(viaje);   // HU32: ni su teléfono
            await DisplayAlertAsync("Listo", $"Ya no estás anotado en el viaje de {viaje.Conductor}.", "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Salirse de un viaje");
            await DisplayAlertAsync("No se pudo salir del viaje", ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Salirse de un viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Salirse de un viaje");
            await DisplayAlertAsync("No se pudo salir del viaje", ex.Message, "OK");
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>HU24: el pasajero carga o cambia su punto de encuentro.</summary>
    private async void OnMiPuntoClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        boton.IsEnabled = false;
        try
        {
            await PuntoDeEncuentro.PedirYGuardarAsync(this, viaje);
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>HU24: el conductor elige a qué pasajero va a buscar y lo abre en el mapa.</summary>
    private async void OnPuntosDeEncuentroClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        boton.IsEnabled = false;
        try
        {
            await PuntoDeEncuentro.MostrarAlConductorAsync(this, viaje);
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    // ───── HU15: check-in y ciclo de vida ─────

    private async void OnIniciarViajeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        await EjecutarAsync(boton, "Iniciar viaje", "No se pudo iniciar", () => _servicio.IniciarViajeAsync(viaje));
    }

    private async void OnFinalizarViajeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        string faltan = viaje.FaltanSubir == 0
            ? ""
            : $" Todavía no confirmaste el PIN de {viaje.FaltanSubir} pasajero(s).";
        bool confirmar = await DisplayAlertAsync("Finalizar viaje",
            $"¿Llegaron a {viaje.Destino}?{faltan} El viaje va a pasar al historial.",
            "Sí, finalizar", "No");
        if (!confirmar) return;

        await EjecutarAsync(boton, "Finalizar viaje", "No se pudo finalizar", () => _servicio.FinalizarViajeAsync(viaje));
    }

    /// <summary>HU15: el conductor escribe el PIN que le dice el pasajero que sube.</summary>
    private async void OnConfirmarPinClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        string? pin = await DisplayPromptAsync("Confirmar PIN",
            "Pedile el PIN al pasajero que sube y escribilo acá.",
            accept: "Confirmar", cancel: "Cancelar",
            placeholder: "4 números", maxLength: 4, keyboard: Keyboard.Numeric);
        if (pin is null) return;   // tocó Cancelar

        await EjecutarAsync(boton, "Confirmar PIN", "No se pudo confirmar", async () =>
        {
            string nombre = await _servicio.ConfirmarPinAsync(viaje, pin);
            await DisplayAlertAsync("A bordo", $"✓ {nombre} subió al viaje.", "OK");
        });
    }

    /// <summary>HU15: el pasajero ve su PIN para decírselo al conductor cuando sube.</summary>
    private async void OnMiPinClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: Viaje viaje } boton) return;

        await EjecutarAsync(boton, "Ver mi PIN", "No se pudo mostrar el PIN", async () =>
        {
            string pin = await _servicio.ObtenerMiPinAsync(viaje);
            await DisplayAlertAsync("Tu PIN", $"{pin}\n\nCuando subas al auto de {viaje.Conductor}, decíselo para confirmar que estás a bordo.", "OK");
        });
    }

    /// <summary>
    /// HU15: corre una acción del check-in con el manejo de errores de siempre
    /// (de la excepción más específica a la más general) y el botón deshabilitado mientras espera.
    /// </summary>
    private async Task EjecutarAsync(Button boton, string donde, string tituloError, Func<Task> accion)
    {
        boton.IsEnabled = false;   // evita que se toque dos veces mientras espera al servidor
        try
        {
            await accion();
        }
        catch (PinInvalidoException ex)
        {
            RegistroErrores.RegistrarAviso(ex, donde);
            await DisplayAlertAsync("PIN incorrecto", ex.Message, "OK");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, donde);
            await DisplayAlertAsync(tituloError, ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, donde);
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, donde);
            await DisplayAlertAsync(tituloError, ex.Message, "OK");
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    /// <summary>HU15: mostrar u ocultar los viajes que ya terminaron.</summary>
    private void OnVerAnterioresToggled(object? sender, ToggledEventArgs e) => Redibujar();

    /// <summary>
    /// Equivalente al evento Paint: vuelve a armar la lista.
    /// El viaje en curso va primero. Los terminados solo se ven con "Ver viajes anteriores" (HU15), al final.
    /// </summary>
    private void Redibujar()
    {
        var visibles = _servicio.MisViajes()
            .Where(v => swAnteriores.IsToggled || !v.Terminado)
            .OrderBy(v => v.Estado == EstadoViaje.EnCurso ? 0 : v.Terminado ? 2 : 1)
            .ThenBy(v => v.Salida);

        _misViajes.Clear();
        foreach (var viaje in visibles)
            _misViajes.Add(viaje);
    }
}
