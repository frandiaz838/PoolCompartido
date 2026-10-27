namespace PoolCompartido;

public partial class PublicarViajePage : ContentPage
{
    private readonly ServicioViajes _servicio = ServicioViajes.Instancia;

    public PublicarViajePage()
    {
        InitializeComponent();

        dpFecha.MinimumDate = DateTime.Today;
        // Por defecto, la próxima hora en punto
        int proximaHora = Math.Min(DateTime.Now.Hour + 1, 23);
        tpHora.Time = new TimeSpan(proximaHora, 0, 0);
        pickerAsientos.ItemsSource = new[] { "1", "2", "3", "4" };
    }

    private async void OnConfirmarClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;
        var boton = (Button)sender!;
        boton.IsEnabled = false;   // evita publicar dos veces mientras espera al servidor

        try
        {
            // Si no eligió nada queda en 0 y el servicio lo rechaza
            int asientos = pickerAsientos.SelectedItem is string texto
                ? int.Parse(texto)   // puede lanzar FormatException
                : 0;

            DateTime fecha = ((DateTime?)dpFecha.Date) ?? DateTime.Today;
            TimeSpan hora = ((TimeSpan?)tpHora.Time) ?? TimeSpan.Zero;

            // HU14: vacío = sin costo. Puede lanzar FormatException o ArgumentOutOfRangeException
            decimal? costo = ServicioViajes.LeerCosto(entryCosto.Text);

            await _servicio.PublicarAsync(entryOrigen.Text, entryDestino.Text, fecha,
                                          FranjasHorarias.HoraATexto(hora), asientos, editorComentario.Text,
                                          entryPuntoPartida.Text, costo);

            await DisplayAlertAsync("Listo", "¡Viaje publicado! Ya lo pueden ver los demás.", "OK");
            await Navigation.PopAsync();
        }
        // ArgumentOutOfRangeException hereda de ArgumentException: va primero la más específica
        catch (ArgumentOutOfRangeException ex)
        {
            MostrarError(ex, ex.Message);
        }
        catch (ArgumentException ex)
        {
            MostrarError(ex, ex.Message);
        }
        catch (FormatException ex)
        {
            // Viene del costo (HU14), que trae su propio mensaje; los asientos salen de una lista fija
            MostrarError(ex, ex.Message);
        }
        catch (HorarioOcupadoException ex)
        {
            // Ya maneja o viaja en otro viaje a esa hora
            MostrarError(ex, ex.Message);
        }
        catch (SesionVencidaException ex)
        {
            // La app ya volvió a la pantalla de inicio de sesión
            RegistroErrores.RegistrarAviso(ex, "Publicar viaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Publicar viaje");
            lblError.Text = ex.Message;
            lblError.IsVisible = true;
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    // Ruta óptima: al salir de cada campo se busca el lugar en el mapa y se muestra debajo
    // si se encontró ("✓ Universidad Blas Pascal, Córdoba") o no. No frena la publicación:
    // Google Maps igual busca el texto, solo puede quedar peor el orden de las paradas.
    private void OnOrigenUnfocused(object? sender, FocusEventArgs e) => RevisarTodoEnMapa();

    private void RevisarTodoEnMapa()
    {
        RevisarEnMapa(entryOrigen, lblMapaOrigen, esOrigen: true);
        // Los otros dos se buscan cerca del origen: si cambió, se revisan de nuevo
        RevisarEnMapa(entryPuntoPartida, lblMapaPartida);
        RevisarEnMapa(entryDestino, lblMapaDestino);
    }

    private void OnPartidaUnfocused(object? sender, FocusEventArgs e) => RevisarEnMapa(entryPuntoPartida, lblMapaPartida);

    private void OnDestinoUnfocused(object? sender, FocusEventArgs e) => RevisarEnMapa(entryDestino, lblMapaDestino);

    /// <summary>Último texto buscado en cada campo, para no repetir búsquedas ni mostrar respuestas viejas.</summary>
    private readonly Dictionary<Label, string> _buscado = new();

    private async void RevisarEnMapa(Entry campo, Label resultado, bool esOrigen = false)
    {
        string texto = campo.Text?.Trim() ?? "";
        string origen = esOrigen ? "" : entryOrigen.Text?.Trim() ?? "";
        string clave = texto + "|" + origen;
        if (_buscado.TryGetValue(resultado, out var anterior) && anterior == clave)
            return;   // ya se buscó esto mismo
        _buscado[resultado] = clave;

        resultado.IsVisible = false;
        if (texto == "")
            return;

        LugarEncontrado? lugar;
        try
        {
            lugar = await ServicioEncuentros.UbicarLugarDelViajeAsync(texto, origen);
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            // Sin internet no se puede revisar: no se muestra nada
            RegistroErrores.RegistrarAviso(ex, "Revisar lugar en el mapa");
            _buscado.Remove(resultado);
            return;
        }

        // Mientras se buscaba, la persona pudo haber cambiado el texto: esta respuesta ya no sirve
        if (!_buscado.TryGetValue(resultado, out var actual) || actual != clave)
            return;

        if (lugar is not null)
        {
            string nombre = string.Join(", ", new[] { lugar.Nombre, lugar.Zona }.Where(p => !string.IsNullOrWhiteSpace(p)));
            resultado.Text = $"✓ En el mapa: {(nombre == "" ? "encontrado" : nombre)}";
            resultado.SetDynamicResource(Label.TextColorProperty, "ColorExito");
        }
        else
        {
            resultado.Text = "No lo encontramos en el mapa. Google Maps igual lo va a buscar, pero el orden " +
                             "de las paradas puede no ser el mejor. Probá con el nombre completo o calle y altura.";
            resultado.SetDynamicResource(Label.TextColorProperty, "ColorAviso");
        }
        resultado.IsVisible = true;
    }

    /// <summary>HU23: completa el origen con el barrio donde está el conductor (no con la dirección exacta).</summary>
    private async void OnUsarUbicacionClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;
        btnUbicacion.IsEnabled = false;
        string textoBoton = btnUbicacion.Text;
        btnUbicacion.Text = "Buscando...";

        try
        {
            UbicacionActual ubicacion = await ServicioUbicacion.ObtenerAsync();
            entryOrigen.Text = ubicacion.Barrio;
            RevisarTodoEnMapa();
        }
        catch (UbicacionNoDisponibleException ex)
        {
            MostrarError(ex, ex.Message);
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Usar mi ubicación al publicar");
            lblError.Text = "No pudimos buscar tu barrio: " + ex.Message;
            lblError.IsVisible = true;
        }
        finally
        {
            btnUbicacion.Text = textoBoton;
            btnUbicacion.IsEnabled = true;
        }
    }

    private void MostrarError(Exception ex, string mensaje)
    {
        RegistroErrores.RegistrarAviso(ex, "Publicar viaje");
        lblError.Text = mensaje;
        lblError.IsVisible = true;
    }
}
