namespace PoolCompartido
{
    public partial class App : Application
    {
        /// <summary>Cada cuántos segundos se buscan cambios en Firebase.</summary>
        private static readonly TimeSpan IntervaloSincronizacion = TimeSpan.FromSeconds(5);

        private IDispatcherTimer? _timerSincronizacion;

        public static App Actual => (App)Current!;

        public App()
        {
            InitializeComponent();

            // HU18 + HU19: tema y paleta que eligió el usuario la última vez
            ServicioApariencia.Inicializar();

            // Carpeta de datos propia de la app en cada dispositivo (Windows, Android...)
            Configuracion.CarpetaDatos = FileSystem.AppDataDirectory;

            // Controlador global: excepciones que nadie capturó con un catch.
            // No evitan que la app se cierre, pero dejan registrado qué pasó.
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    RegistroErrores.RegistrarError(ex, "Error no controlado");
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                RegistroErrores.RegistrarError(e.Exception, "Tarea en segundo plano");
                e.SetObserved();
            };

            // Cuando se cierra la sesión (a mano o porque venció), se vuelve al login
            ServicioAutenticacion.Instancia.Eventos.Registrar(EventoSesion.SESION_CERRADA, _ => IrAlLogin());
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new LoginPage());
        }

        /// <summary>Después de iniciar sesión: carga los viajes, muestra la app y arranca la sincronización.</summary>
        public async Task EntrarAsync()
        {
            await ServicioViajes.Instancia.CargarAsync();

            // HU26: el centro de notificaciones escucha todos los eventos de viajes de este usuario
            if (ServicioAutenticacion.UidActual is string uid)
                ServicioNotificaciones.Instancia.Conectar(ServicioViajes.Instancia.Eventos, uid);

            CambiarPagina(new AppShell());
            IniciarSincronizacion();
        }

        private void IrAlLogin()
        {
            _timerSincronizacion?.Stop();
            _timerSincronizacion = null;
            ServicioViajes.Instancia.Reiniciar();
            ServicioChat.Instancia.Reiniciar();   // HU25
            ServicioCalificaciones.Instancia.Reiniciar();   // HU31
            ServicioApariencia.Reiniciar();       // HU19
            ServicioNotificaciones.Instancia.Reiniciar();   // HU26
            CambiarPagina(new LoginPage());
        }

        private void CambiarPagina(Page pagina)
        {
            if (Windows.Count > 0)
                Windows[0].Page = pagina;
        }

        /// <summary>
        /// Timer: cada pocos segundos dispara el evento Tick y se buscan cambios en Firebase.
        /// Si otra persona publicó, se anotó o canceló, se disparan los eventos de los viajes.
        /// </summary>
        private void IniciarSincronizacion()
        {
            _timerSincronizacion?.Stop();
            _timerSincronizacion = Dispatcher.CreateTimer();
            _timerSincronizacion.Interval = IntervaloSincronizacion;
            _timerSincronizacion.Tick += async (sender, e) =>
            {
                await SincronizarAsync();
                await RevisarNoShowAsync();   // HU16: solo hace algo en los viajes que manejo yo
                // Aunque no haya internet, el tiempo pasa: los viajes cierran, salen y terminan
                ServicioViajes.Instancia.ActualizarEstados();
                // HU25: mensajes nuevos en los chats de mis viajes
                await ServicioChat.Instancia.RevisarNuevosAsync();
                // HU31: promedios de las tarjetas y calificaciones nuevas que me hicieron
                await ServicioCalificaciones.Instancia.RevisarAsync();
            };
            _timerSincronizacion.Start();
        }

        private static async Task SincronizarAsync()
        {
            try
            {
                await ServicioViajes.Instancia.SincronizarAsync();
            }
            catch (SesionVencidaException ex)
            {
                // La sesión ya se cerró y la app volvió al login
                RegistroErrores.RegistrarAviso(ex, "Sincronizar viajes");
            }
            catch (Exception ex) when (ex is PermisoDenegadoException or ServidorException)
            {
                RegistroErrores.RegistrarError(ex, "Sincronizar viajes");
            }
        }

        /// <summary>HU16: saca a los pasajeros que no subieron a tiempo. Si falla, se reintenta en el próximo tick.</summary>
        private static async Task RevisarNoShowAsync()
        {
            try
            {
                await ServicioViajes.Instancia.RevisarNoShowAsync();
            }
            catch (ViajeException ex)
            {
                // Por ejemplo, un conflicto porque justo alguien hizo check-in
                RegistroErrores.RegistrarAviso(ex, "Revisar no-show");
            }
            catch (SesionVencidaException ex)
            {
                RegistroErrores.RegistrarAviso(ex, "Revisar no-show");
            }
            catch (SinConexionException ex)
            {
                RegistroErrores.RegistrarAviso(ex, "Revisar no-show");
            }
            catch (Exception ex) when (ex is PermisoDenegadoException or ServidorException)
            {
                RegistroErrores.RegistrarError(ex, "Revisar no-show");
            }
        }
    }
}
