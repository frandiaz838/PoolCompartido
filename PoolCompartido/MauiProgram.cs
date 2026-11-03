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
            ConfigurarCursor();

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
        /// Android: cada campo (texto, listas, fecha y hora) es una caja con fondo de superficie, esquinas redondeadas
        /// y un borde del color de los controles (contraste ≥ 3:1), que se pone azul mientras se escribe en el campo.
        /// Se vuelve a pintar cuando cambia el fondo o el color del texto (o sea, también al cambiar de tema).
        /// (Antes se teñía el fondo nativo para la línea de abajo, pero en algunos celulares eso pintaba la caja entera de gris.)
        /// </summary>
        private static void ConfigurarSubrayado()
        {
#if ANDROID
            foreach (var clave in new[] { "Background", "TextColor" })
            {
                EntryHandler.Mapper.AppendToMapping(clave, (handler, campo) => PintarCampo(handler.PlatformView, campo is EntryConIcono));
                EditorHandler.Mapper.AppendToMapping(clave, (handler, _) => PintarCampo(handler.PlatformView, false));
                PickerHandler.Mapper.AppendToMapping(clave, (handler, _) => PintarCampo(handler.PlatformView, false));
                DatePickerHandler.Mapper.AppendToMapping(clave, (handler, _) => PintarCampo(handler.PlatformView, false));
                TimePickerHandler.Mapper.AppendToMapping(clave, (handler, _) => PintarCampo(handler.PlatformView, false));
            }
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

        /// <summary>
        /// Windows: el mouse se pone en forma de manito arriba de lo que se puede tocar: botones, botones con imagen
        /// y todo lo que tiene un TapGestureRecognizer (chips de días, tarjetas de horario, etc.).
        /// En los botones deshabilitados vuelve a la flecha. WinUI guarda el cursor en ProtectedCursor,
        /// que no es público, por eso se usa reflexión. Solo cambia la apariencia: no toca ningún evento.
        /// </summary>
        private static void ConfigurarCursor()
        {
#if WINDOWS
            ButtonHandler.Mapper.AppendToMapping(nameof(IView.IsEnabled), (handler, boton) => PonerManito(handler.PlatformView, boton.IsEnabled));
            ImageButtonHandler.Mapper.AppendToMapping(nameof(IView.IsEnabled), (handler, boton) => PonerManito(handler.PlatformView, boton.IsEnabled));
            ViewHandler.ViewMapper.AppendToMapping("ManitoSiSeToca", (handler, vista) =>
            {
                if (vista is View control && control.GestureRecognizers.Any(g => g is TapGestureRecognizer)
                    && handler.PlatformView is Microsoft.UI.Xaml.UIElement elemento)
                    PonerManito(elemento, true);
            });
#endif
        }

#if WINDOWS
        private static readonly System.Reflection.PropertyInfo? PropiedadCursor = typeof(Microsoft.UI.Xaml.UIElement)
            .GetProperty("ProtectedCursor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static void PonerManito(Microsoft.UI.Xaml.UIElement elemento, bool habilitado)
        {
            var forma = habilitado ? Microsoft.UI.Input.InputSystemCursorShape.Hand : Microsoft.UI.Input.InputSystemCursorShape.Arrow;
            PropiedadCursor?.SetValue(elemento, Microsoft.UI.Input.InputSystemCursor.Create(forma));
        }
#endif

#if ANDROID
        private static void PintarCampo(Android.Views.View vista, bool conIcono)
        {
            float densidad = vista.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            int Dp(double valor) => (int)Math.Round(valor * densidad);

            var fondo = new Android.Graphics.Drawables.GradientDrawable();
            fondo.SetColor(ParaAndroid(ColoresDelTema.Actual("ColorSuperficie")));
            fondo.SetCornerRadius(Dp(10));
            int[][] estados = { new[] { Android.Resource.Attribute.StateFocused }, System.Array.Empty<int>() };
            int[] bordes = { ParaAndroid(ColoresDelTema.Actual("ColorPrimario")), ParaAndroid(ColoresDelTema.Actual("ColorBordeControl")) };
            fondo.SetStroke(Math.Max(1, Dp(1)), new Android.Content.Res.ColorStateList(estados, bordes));
            vista.Background = fondo;

            // El texto no pegado al borde; el EntryConIcono deja lugar a la derecha para el ojo de la contraseña
            vista.SetPadding(Dp(12), Dp(8), conIcono ? Dp(EntryConIcono.EspacioIcono) : Dp(12), Dp(8));
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
