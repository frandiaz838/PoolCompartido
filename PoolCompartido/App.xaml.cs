namespace PoolCompartido
{
    public partial class App : Application
    {
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
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
