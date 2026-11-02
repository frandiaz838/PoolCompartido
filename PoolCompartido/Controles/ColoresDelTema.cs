namespace PoolCompartido;

/// <summary>
/// Colores del tema (tokens de Resources/Styles/Colors.xaml) para los controles que se arman en el código.
/// Es lo mismo que AppThemeBinding en el XAML: cada token tiene su versión "Claro" y "Oscuro",
/// y MAUI cambia el color del control solo cuando cambia el tema.
/// </summary>
internal static class ColoresDelTema
{
    /// <summary>El color de un token para el tema claro u oscuro (por ejemplo "ColorTexto" → ColorTextoOscuro).</summary>
    public static Color Buscar(string token, bool oscuro) =>
        Application.Current?.Resources.TryGetValue(token + (oscuro ? "Oscuro" : "Claro"), out var valor) == true && valor is Color color
            ? color
            : Colors.Transparent;

    /// <summary>El color del token en el tema que se está viendo ahora.</summary>
    public static Color Actual(string token) =>
        Buscar(token, Application.Current?.RequestedTheme == AppTheme.Dark);

    /// <summary>Aplica un token a una propiedad de color (TextColor, BackgroundColor...).</summary>
    public static void Aplicar(BindableObject control, BindableProperty propiedad, string token) =>
        control.SetAppThemeColor(propiedad, Buscar(token, oscuro: false), Buscar(token, oscuro: true));

    /// <summary>Aplica un token a una propiedad de pincel (por ejemplo el borde de un Border).</summary>
    public static void AplicarPincel(BindableObject control, BindableProperty propiedad, string token) =>
        control.SetAppTheme<Brush>(propiedad,
            new SolidColorBrush(Buscar(token, oscuro: false)), new SolidColorBrush(Buscar(token, oscuro: true)));
}
