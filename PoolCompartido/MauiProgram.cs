using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;

namespace PoolCompartido
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            ConfigurarEntryConIcono();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        /// <summary>
        /// A los EntryConIcono les agrega espacio a la derecha en el control nativo de cada plataforma,
        /// para que el texto no quede debajo del ícono.
        /// </summary>
        private static void ConfigurarEntryConIcono()
        {
            EntryHandler.Mapper.AppendToMapping("EspacioParaIcono", (handler, entrada) =>
            {
                if (entrada is not EntryConIcono)
                    return;

#if WINDOWS
                // Se parte del relleno estándar de los campos de texto de Windows (el mismo que tiene
                // el campo de Mail), así los dos quedan alineados y solo cambia el lado derecho.
                var relleno = Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue("TextControlThemePadding", out var valor)
                              && valor is Microsoft.UI.Xaml.Thickness estandar
                    ? estandar
                    : new Microsoft.UI.Xaml.Thickness(10, 5, 6, 6);
                handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(
                    relleno.Left, relleno.Top, EntryConIcono.EspacioIcono, relleno.Bottom);
#elif ANDROID
                var vista = handler.PlatformView;
                float densidad = vista.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                vista.SetPadding(vista.PaddingLeft, vista.PaddingTop,
                                 (int)(EntryConIcono.EspacioIcono * densidad), vista.PaddingBottom);
#endif
            });
        }
    }
}
