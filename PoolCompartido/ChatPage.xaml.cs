using System.Collections.ObjectModel;

namespace PoolCompartido;

/// <summary>
/// HU25: pantalla del chat grupal de un viaje.
/// Mientras está abierta, un timer trae los mensajes nuevos cada 2 segundos y la lista se redibuja sola.
/// </summary>
public partial class ChatPage : ContentPage
{
    private static readonly TimeSpan IntervaloActualizacion = TimeSpan.FromSeconds(2);

    private readonly ServicioChat _chat = ServicioChat.Instancia;
    private readonly Viaje _viaje;
    private readonly ObservableCollection<MensajeChat> _mensajes = new();
    private IDispatcherTimer? _timer;
    private bool _trayendo;

    public ChatPage(Viaje viaje)
    {
        ArgumentNullException.ThrowIfNull(viaje);
        InitializeComponent();

        // HU26: cartel de avisos y campanita (los mensajes de este chat no avisan: ya se ven acá)
        Notificaciones.Preparar(this);

        _viaje = viaje;
        listaMensajes.ItemsSource = _mensajes;
        lblRuta.Text = viaje.Ruta;
        lblIntegrantes.Text = viaje.Pasajeros.Count == 0
            ? $"{viaje.Detalle}"
            : $"{viaje.Conductor} (conductor), {string.Join(", ", viaje.Pasajeros)}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _chat.ChatAbierto = _viaje.Id;   // con el chat abierto no hace falta el aviso

        await TraerNuevosAsync();

        // Timer: cada 2 segundos busca mensajes nuevos (evento Tick)
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = IntervaloActualizacion;
        _timer.Tick += async (sender, e) => await TraerNuevosAsync();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
        _timer = null;
        if (_chat.ChatAbierto == _viaje.Id)
            _chat.ChatAbierto = null;
    }

    /// <summary>Trae los mensajes que todavía no están en pantalla y los marca como leídos.</summary>
    private async Task TraerNuevosAsync()
    {
        if (_trayendo) return;
        _trayendo = true;
        try
        {
            string? ultima = _mensajes.Count > 0 ? _mensajes[^1].Clave : null;
            var nuevos = await _chat.ObtenerAsync(_viaje, ultima);
            foreach (var mensaje in nuevos)
                _mensajes.Add(mensaje);

            _chat.MarcarLeido(_viaje, _mensajes.Count > 0 ? _mensajes[^1].Clave : null);
            ActualizarEstado(null);
        }
        catch (PermisoDenegadoException ex)
        {
            // Ya no está en el viaje (se bajó o lo sacaron): las reglas no lo dejan leer más
            RegistroErrores.RegistrarAviso(ex, "Leer chat");
            _timer?.Stop();
            ActualizarEstado("Ya no participás de este viaje, así que no podés ver el chat.", permiteEscribir: false);
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Leer chat");
            _timer?.Stop();
        }
        catch (Exception ex) when (ex is SinConexionException or ServidorException)
        {
            RegistroErrores.RegistrarAviso(ex, "Leer chat");
            ActualizarEstado("Sin conexión: los mensajes nuevos van a aparecer cuando vuelva internet.");
        }
        finally
        {
            _trayendo = false;
        }
    }

    /// <summary>Muestra un aviso abajo (o lo oculta) y habilita o no la escritura.</summary>
    private void ActualizarEstado(string? aviso, bool permiteEscribir = true)
    {
        if (!_viaje.PuedeChatear)
        {
            aviso ??= "Este viaje ya terminó: el chat queda solo para leer.";
            permiteEscribir = false;
        }

        lblAviso.Text = aviso ?? "";
        lblAviso.IsVisible = aviso is not null;
        entryMensaje.IsEnabled = permiteEscribir;
        btnEnviar.IsEnabled = permiteEscribir;
    }

    private async void OnEnviarClicked(object? sender, EventArgs e)
    {
        if (!btnEnviar.IsEnabled) return;
        btnEnviar.IsEnabled = false;   // evita mandar dos veces el mismo mensaje
        try
        {
            await _chat.EnviarAsync(_viaje, entryMensaje.Text);
            entryMensaje.Text = "";
            await TraerNuevosAsync();   // así el mensaje aparece enseguida
        }
        catch (ArgumentException ex)
        {
            // Mensaje vacío o demasiado largo: no hace falta un cartel
            RegistroErrores.RegistrarAviso(ex, "Enviar mensaje");
        }
        catch (ViajeException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Enviar mensaje");
            await DisplayAlertAsync("No se pudo enviar", ex.Message, "OK");
        }
        catch (SesionVencidaException ex)
        {
            RegistroErrores.RegistrarAviso(ex, "Enviar mensaje");
        }
        catch (Exception ex) when (ex is SinConexionException or PermisoDenegadoException or ServidorException)
        {
            RegistroErrores.RegistrarError(ex, "Enviar mensaje");
            await DisplayAlertAsync("No se pudo enviar", ex.Message, "OK");
        }
        finally
        {
            btnEnviar.IsEnabled = entryMensaje.IsEnabled;
        }
    }
}
