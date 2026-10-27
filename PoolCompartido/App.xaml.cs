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
            CambiarPagina(new AppShell());
            IniciarSincronizacion();
        }

        private void IrAlLogin()
        {
            _timerSincronizacion?.Stop();
            _timerSincronizacion = null;
            ServicioViajes.Instancia.Reiniciar();
            ServicioChat.Instancia.Reiniciar();   // HU25
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
                // Aunque no haya internet, el tiempo pasa: los viajes cierran, salen y terminan
                ServicioViajes.Instancia.ActualizarEstados();
                // HU25: mensajes nuevos en los chats de mis viajes
                await ServicioChat.Instancia.RevisarNuevosAsync();
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
    }
}
