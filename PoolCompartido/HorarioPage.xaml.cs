namespace PoolCompartido;

/// <summary>HU30: alta, edición y baja de un horario habitual. La lógica está en ServicioHorarios.</summary>
public partial class HorarioPage : ContentPage
{
    private readonly HorarioHabitual? _original;
    private readonly HashSet<DayOfWeek> _dias = new();
    private readonly Dictionary<DayOfWeek, Button> _botones = new();

    /// <param name="horario">El horario a editar, o null para uno nuevo.</param>
    public HorarioPage(HorarioHabitual? horario)
    {
        InitializeComponent();
        Notificaciones.Preparar(this);   // HU26

        _original = horario;
        Title = horario is null ? "Nuevo horario" : "Editar horario";
        btnBorrar.IsVisible = horario is not null;

        if (horario is not null)
        {
            _dias.UnionWith(horario.Dias);
            tpHora.Time = horario.Hora;
            entryOrigen.Text = horario.Origen;
            entryDestino.Text = horario.Destino;
        }
        else
        {
            tpHora.Time = new TimeSpan(8, 0, 0);
        }

        ArmarDias();
    }

    /// <summary>Un botón por día (Lun a Dom); el elegido se ve "lleno" y con ✓ (no depende solo del color).</summary>
    private void ArmarDias()
    {
        foreach (var dia in HorarioHabitual.SemanaDeLunes)
        {
            var boton = new Button { Margin = new Thickness(0, 0, 8, 8), MinimumWidthRequest = 64 };
            boton.Clicked += (s, e) =>
            {
                if (!_dias.Remove(dia)) _dias.Add(dia);
                PintarDia(dia);
            };
            SemanticProperties.SetDescription(boton, HorarioHabitual.NombreDia(dia));
            _botones[dia] = boton;
            panelDias.Children.Add(boton);
            PintarDia(dia);
        }
    }

    private void PintarDia(DayOfWeek dia)
    {
        var boton = _botones[dia];
        bool elegido = _dias.Contains(dia);
        boton.Text = elegido ? $"✓ {HorarioHabitual.DiaCorto(dia)}" : HorarioHabitual.DiaCorto(dia);
        if (Application.Current?.Resources.TryGetValue(elegido ? "BotonPrincipal" : "BotonSecundario", out var estilo) == true)
            boton.Style = (Style)estilo;
    }

    private async void OnGuardarClicked(object? sender, EventArgs e)
    {
        lblError.IsVisible = false;
        btnGuardar.IsEnabled = false;
        try
        {
            TimeSpan hora = ((TimeSpan?)tpHora.Time) ?? TimeSpan.Zero;
            await ServicioHorarios.Instancia.GuardarAsync(_original?.Id, _dias, hora, entryOrigen.Text, entryDestino.Text);
            await Navigation.PopAsync();
        }
        // ArgumentOutOfRangeException hereda de ArgumentException: va primero la más específica
        catch (ArgumentOutOfRangeException ex)
        {
            MostrarError(ex);
        }
        catch (ArgumentException ex)
        {
            MostrarError(ex);
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Guardar horario");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Guardar horario");
            lblError.Text = ex.Message;
            lblError.IsVisible = true;
        }
        finally
        {
            btnGuardar.IsEnabled = true;
        }
    }

    private async void OnBorrarClicked(object? sender, EventArgs e)
    {
        if (_original is null) return;
        bool borrar = await DisplayAlertAsync("Borrar horario",
            $"¿Borrar tu horario {_original.Resumen} ({_original.Ruta})?", "Borrar", "Cancelar");
        if (!borrar) return;

        try
        {
            await ServicioHorarios.Instancia.BorrarAsync(_original);
            await Navigation.PopAsync();
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Borrar horario");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Borrar horario");
            lblError.Text = ex.Message;
            lblError.IsVisible = true;
        }
    }

    private void MostrarError(Exception ex)
    {
        RegistroErrores.RegistrarAviso(ex, "Guardar horario");
        lblError.Text = ex.Message;
        lblError.IsVisible = true;
    }
}
