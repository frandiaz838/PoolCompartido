namespace PoolCompartido;

/// <summary>
/// HU31: pantalla para calificar a las personas con las que viajé.
/// Se abre desde Mis viajes → "Ver viajes anteriores" → "Calificar".
/// </summary>
public partial class CalificarPage : ContentPage
{
    private readonly ServicioCalificaciones _servicio = ServicioCalificaciones.Instancia;
    private readonly Viaje _viaje;

    public CalificarPage(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        InitializeComponent();

        // HU26: cartel de avisos y campanita
        Notificaciones.Preparar(this);

        _viaje = viaje;
        lblRuta.Text = viaje.Ruta;
        lblDetalle.Text = viaje.Detalle;

        foreach (var persona in _servicio.PorCalificar(viaje))
            panelPersonas.Children.Add(ArmarTarjeta(persona));
        lblListo.IsVisible = panelPersonas.Children.Count == 0;
    }

    /// <summary>Tarjeta de una persona: nombre, 5 estrellas, comentario opcional y botón para enviar.</summary>
    private View ArmarTarjeta(PersonaACalificar persona)
    {
        int elegidas = 0;
        var estrellas = new List<Button>();
        var lblElegidas = new Label { FontSize = 13, VerticalOptions = LayoutOptions.Center };
        lblElegidas.SetDynamicResource(StyleProperty, "TextoSecundario");

        var filaEstrellas = new HorizontalStackLayout { Spacing = 2 };
        for (int i = 1; i <= ServicioCalificaciones.EstrellasMaximas; i++)
        {
            int valor = i;
            var estrella = new Button
            {
                Text = "☆",
                FontSize = 26,
                Padding = 0,
                WidthRequest = 44,
                HeightRequest = 44,
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0
            };
            estrella.SetDynamicResource(Button.TextColorProperty, "ColorAviso");
            SemanticProperties.SetDescription(estrella, valor == 1 ? "1 estrella" : $"{valor} estrellas");
            estrella.Clicked += (sender, e) =>
            {
                elegidas = valor;
                // Llenas hasta la elegida; el número va al lado, así no depende solo del dibujo
                for (int j = 0; j < estrellas.Count; j++)
                    estrellas[j].Text = j < valor ? "★" : "☆";
                lblElegidas.Text = valor == 1 ? "1 de 5" : $"{valor} de 5";
            };
            estrellas.Add(estrella);
            filaEstrellas.Children.Add(estrella);
        }
        filaEstrellas.Children.Add(lblElegidas);

        var editorComentario = new Editor
        {
            Placeholder = "Comentario (opcional). Ej: muy puntual",
            MaxLength = ServicioCalificaciones.ComentarioMaximo,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MinimumHeightRequest = 60
        };

        var lblError = new Label { IsVisible = false, FontSize = 13 };
        lblError.SetDynamicResource(Label.TextColorProperty, "ColorPeligro");

        var btnEnviar = new Button { Text = "Enviar calificación", HorizontalOptions = LayoutOptions.Start };
        btnEnviar.SetDynamicResource(StyleProperty, "BotonPrincipal");

        var contenido = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = persona.Descripcion, FontSize = 16, FontAttributes = FontAttributes.Bold },
                filaEstrellas,
                editorComentario,
                lblError,
                btnEnviar
            }
        };
        var tarjeta = new Border { Content = contenido };
        tarjeta.SetDynamicResource(StyleProperty, "TarjetaViaje");

        btnEnviar.Clicked += async (sender, e) =>
        {
            lblError.IsVisible = false;
            btnEnviar.IsEnabled = false;   // evita calificar dos veces mientras espera al servidor
            try
            {
                await _servicio.CalificarAsync(_viaje, persona, elegidas, editorComentario.Text);

                // Listo: la tarjeta queda como resumen y ya no se puede volver a enviar
                var hecho = new Label
                {
                    Text = $"✓ Calificaste a {persona.Nombre} con {Calificacion.Dibujar(elegidas)} ({elegidas} de 5).",
                    FontAttributes = FontAttributes.Bold
                };
                hecho.SetDynamicResource(Label.TextColorProperty, "ColorExito");
                tarjeta.Content = hecho;
                lblListo.IsVisible = _viaje.PendientesDeCalificar == 0;
            }
            // ArgumentOutOfRangeException hereda de ArgumentException: va primero la más específica
            catch (ArgumentOutOfRangeException ex)
            {
                MostrarError(lblError, ex, ex.Message);
            }
            catch (ArgumentException ex)
            {
                MostrarError(lblError, ex, ex.Message);
            }
            catch (YaCalificasteException ex)
            {
                // Se calificó desde otro dispositivo: la tarjeta ya no sirve
                MostrarError(lblError, ex, ex.Message);
                btnEnviar.IsVisible = false;
            }
            catch (ViajeException ex)
            {
                MostrarError(lblError, ex, ex.Message);
            }
            catch (SesionVencidaException ex)
            {
                // La app ya volvió a la pantalla de inicio de sesión
                RegistroErrores.RegistrarAviso(ex, "Calificar");
            }
            catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
            {
                RegistroErrores.RegistrarError(ex, "Calificar");
                lblError.Text = ex.Message;
                lblError.IsVisible = true;
            }
            finally
            {
                btnEnviar.IsEnabled = true;
            }
        };

        return tarjeta;
    }

    private static void MostrarError(Label lblError, Exception ex, string mensaje)
    {
        RegistroErrores.RegistrarAviso(ex, "Calificar");
        lblError.Text = mensaje;
        lblError.IsVisible = true;
    }
}
