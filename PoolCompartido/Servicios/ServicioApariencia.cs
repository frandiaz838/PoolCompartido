namespace PoolCompartido;

/// <summary>HU18: tema elegido por el usuario.</summary>
public enum Tema
{
    Sistema,
    Claro,
    Oscuro
}

/// <summary>HU19: eventos de apariencia (tercer uso del despachador genérico).</summary>
public enum EventoApariencia
{
    APARIENCIA_CAMBIADA
}

/// <summary>Cómo se ve la app: el tema y si está activa la paleta para daltonismo.</summary>
public record Apariencia(Tema Tema, bool Daltonico);

/// <summary>
/// HU18 + HU19: tema claro / oscuro / del sistema y paleta para daltonismo.
/// La elección se guarda en el dispositivo (Preferences) y se aplica al abrir la app.
/// Los colores de estado son recursos con nombre (ColorExito, ColorAviso, ColorPeligro...)
/// que las pantallas usan con DynamicResource: al cambiarlos acá, toda la app se redibuja sola.
/// Además se dispara APARIENCIA_CAMBIADA con un Despachador&lt;EventoApariencia, Apariencia&gt;,
/// la tercera instancia del despachador genérico (las otras son la de viajes y la de sesión).
/// </summary>
public static class ServicioApariencia
{
    private const string ClaveTema = "tema";
    private const string ClaveDaltonico = "daltonico";

    public static Despachador<EventoApariencia, Apariencia> Eventos { get; private set; } = new();

    public static Apariencia Actual { get; private set; } = new(Tema.Sistema, false);

    /// <summary>Si en este momento la app se ve oscura (por elección o porque el sistema está en oscuro).</summary>
    public static bool EsOscuro => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>Se llama al abrir la app: lee lo guardado y lo aplica.</summary>
    public static void Inicializar()
    {
        string guardado = Preferences.Default.Get(ClaveTema, nameof(Tema.Sistema));
        Tema tema = Enum.TryParse(guardado, out Tema t) && Enum.IsDefined(t) ? t : Tema.Sistema;
        Actual = new Apariencia(tema, Preferences.Default.Get(ClaveDaltonico, false));

        Aplicar(disparar: false);

        // Evento estándar de MAUI: si el tema es "Según el sistema" y el celular pasa a oscuro, se acomodan los colores
        if (Application.Current is not null)
            Application.Current.RequestedThemeChanged += (sender, e) => AplicarColores();
    }

    /// <summary>HU18: cambia el tema y lo recuerda.</summary>
    /// <exception cref="ArgumentOutOfRangeException">El tema no existe.</exception>
    public static void CambiarTema(Tema tema)
    {
        if (!Enum.IsDefined(tema))
            throw new ArgumentOutOfRangeException(nameof(tema), "Ese tema no existe.");
        if (tema == Actual.Tema) return;

        Actual = Actual with { Tema = tema };
        Preferences.Default.Set(ClaveTema, tema.ToString());
        Aplicar();
    }

    /// <summary>HU19: activa o desactiva la paleta para daltonismo y lo recuerda.</summary>
    public static void CambiarDaltonico(bool activo)
    {
        if (activo == Actual.Daltonico) return;

        Actual = Actual with { Daltonico = activo };
        Preferences.Default.Set(ClaveDaltonico, activo);
        Aplicar();
    }

    /// <summary>Al cerrar sesión: las pantallas viejas dejan de estar suscriptas.</summary>
    public static void Reiniciar() => Eventos = new Despachador<EventoApariencia, Apariencia>();

    private static void Aplicar(bool disparar = true)
    {
        if (Application.Current is null) return;

        Application.Current.UserAppTheme = Actual.Tema switch
        {
            Tema.Claro => AppTheme.Light,
            Tema.Oscuro => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
        AplicarColores();

        if (disparar)
            Eventos.Disparar(EventoApariencia.APARIENCIA_CAMBIADA, Actual);
    }

    /// <summary>Reemplaza los colores de estado: todo lo que los usa con DynamicResource se actualiza solo.</summary>
    private static void AplicarColores()
    {
        if (Application.Current is null) return;

        var paleta = Paleta.Para(Actual.Daltonico, EsOscuro);
        var recursos = Application.Current.Resources;
        recursos["ColorExito"] = Color.FromArgb(paleta.Exito);
        recursos["ColorExitoFondo"] = Color.FromArgb(paleta.ExitoFondo);
        recursos["ColorPeligro"] = Color.FromArgb(paleta.Peligro);
        recursos["ColorPeligroBorde"] = Color.FromArgb(paleta.PeligroBorde);
        recursos["ColorAviso"] = Color.FromArgb(paleta.Aviso);
        recursos["ColorAvisoFondo"] = Color.FromArgb(paleta.AvisoFondo);   // HU29

        AjustarBarraDeEstado();
    }

    /// <summary>En Android, la barra de arriba (hora, batería) acompaña el tema.</summary>
    public static void AjustarBarraDeEstado()
    {
#if ANDROID
        if (Platform.CurrentActivity?.Window is Android.Views.Window ventana)
            AjustarBarraDeEstado(ventana);
#endif
    }

#if ANDROID
    public static void AjustarBarraDeEstado(Android.Views.Window ventana)
    {
        bool oscuro = EsOscuro;
        ventana.SetStatusBarColor(Android.Graphics.Color.ParseColor(oscuro ? "#1F1F1F" : "#FFFFFF"));
        // Íconos oscuros sobre la barra clara, y al revés
        AndroidX.Core.View.WindowCompat.GetInsetsController(ventana, ventana.DecorView).AppearanceLightStatusBars = !oscuro;
    }
#endif
}
