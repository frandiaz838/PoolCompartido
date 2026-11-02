using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;

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
            ConfigurarSubrayado();
            ConfigurarListas();

#if WINDOWS
            // La ventana de Windows usa el tema de la app (y no el del sistema) cada vez que se activa
            builder.ConfigureLifecycleEvents(eventos => eventos.AddWindows(windows =>
                windows.OnActivated((ventana, args) => ServicioApariencia.AjustarVentana(ventana))));
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        /// <summary>
        /// Android: la línea de abajo de los campos (texto, listas, fecha y hora) con el color de borde de la app,
        /// que tiene contraste suficiente (≥ 3:1), y violeta mientras se escribe en el campo.
        /// Se vuelve a pintar cada vez que cambia el color del texto (con AppThemeBinding), o sea también al cambiar de tema.
        /// </summary>
        private static void ConfigurarSubrayado()
        {
#if ANDROID
            EntryHandler.Mapper.AppendToMapping(nameof(Entry.TextColor), (handler, _) => Subrayar(handler.PlatformView));
            EditorHandler.Mapper.AppendToMapping(nameof(Editor.TextColor), (handler, _) => Subrayar(handler.PlatformView));
            PickerHandler.Mapper.AppendToMapping(nameof(Picker.TextColor), (handler, _) => Subrayar(handler.PlatformView));
            DatePickerHandler.Mapper.AppendToMapping(nameof(DatePicker.TextColor), (handler, _) => Subrayar(handler.PlatformView));
            TimePickerHandler.Mapper.AppendToMapping(nameof(TimePicker.TextColor), (handler, _) => Subrayar(handler.PlatformView));
#endif
        }

        /// <summary>
        /// Windows: el título de las listas (Picker) va como texto de ayuda adentro de la caja ("Elegí la cantidad"),
        /// no como un rótulo arriba (cada pantalla ya tiene el suyo). Y el interruptor (Switch) sin el hueco
        /// que Windows le deja a la derecha para un texto que no usamos, así queda alineado al borde.
        /// </summary>
        private static void ConfigurarListas()
        {
#if WINDOWS
            // Se aplica después del título y de su color, que son los que arman el rótulo de arriba
            static void TituloAdentro(IPickerHandler handler, IPicker lista)
            {
                handler.PlatformView.Header = null;
                handler.PlatformView.HeaderTemplate = null;
                handler.PlatformView.PlaceholderText = lista.Title ?? "";
            }
            PickerHandler.Mapper.AppendToMapping(nameof(Picker.Title), TituloAdentro);
            PickerHandler.Mapper.AppendToMapping(nameof(Picker.TitleColor), TituloAdentro);
            SwitchHandler.Mapper.AppendToMapping("SinHueco", (handler, _) => handler.PlatformView.MinWidth = 0);
#endif
        }

#if ANDROID
        private static void Subrayar(Android.Views.View vista)
        {
            var colorBorde = ColoresDelTema.Actual("ColorBordeControl");
            var colorPrimario = ColoresDelTema.Actual("ColorPrimario");

            int[][] estados = { new[] { Android.Resource.Attribute.StateFocused }, System.Array.Empty<int>() };
            int[] colores = { ParaAndroid(colorPrimario), ParaAndroid(colorBorde) };
            vista.BackgroundTintList = new Android.Content.Res.ColorStateList(estados, colores);
        }

        private static int ParaAndroid(Color color) => Android.Graphics.Color.ParseColor(color.ToArgbHex()).ToArgb();
#endif

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
