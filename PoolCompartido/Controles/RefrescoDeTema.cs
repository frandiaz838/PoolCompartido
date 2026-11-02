namespace PoolCompartido;

/// <summary>
/// Vuelve a aplicar los estilos de todas las pantallas abiertas cuando cambia el tema o la paleta para daltonismo.
/// MAUI tiene errores conocidos al actualizar en vivo los colores que vienen de un estilo (AppThemeBinding o
/// DynamicResource dentro de un Style): al abrir la app se ven bien, pero al cambiar el tema algunos controles
/// quedaban con los colores anteriores. Aplicar el estilo de nuevo hace que se calculen con el tema actual,
/// igual que cuando la pantalla se crea.
/// Es solo visual: no toca datos, eventos ni servicios.
/// </summary>
internal static class RefrescoDeTema
{
    /// <summary>Recorre las ventanas, las pestañas y las pantallas abiertas encima, y reaplica los estilos.</summary>
    public static void Aplicar()
    {
        if (Application.Current is null) return;

        var vistos = new HashSet<Element>();
        foreach (var ventana in Application.Current.Windows)
        {
            if (ventana.Page is not Page raiz) continue;
            Recorrer(raiz, vistos);

            if (raiz is Shell shell)
            {
                // Las pestañas que ya se abrieron alguna vez y las pantallas abiertas encima
                foreach (var item in shell.Items)
                    foreach (var seccion in item.Items)
                    {
                        foreach (var contenido in seccion.Items)
                            if (((IShellContentController)contenido).Page is Page pestana)
                                Recorrer(pestana, vistos);
                        foreach (var pagina in seccion.Navigation.NavigationStack)
                            if (pagina is not null)
                                Recorrer(pagina, vistos);
                    }
            }
        }
    }

    private static void Recorrer(Element elemento, HashSet<Element> vistos)
    {
        if (!vistos.Add(elemento)) return;

        if (elemento is VisualElement visual)
            Reaplicar(visual);

        // La barra de arriba (título, flecha, campanita) es parte de la página pero no es un hijo visual
        if (elemento is Page pagina && Shell.GetTitleView(pagina) is View barra)
            Recorrer(barra, vistos);

        foreach (var hijo in ((IVisualTreeElement)elemento).GetVisualChildren())
            if (hijo is Element e)
                Recorrer(e, vistos);
    }

    /// <summary>
    /// Saca y vuelve a poner el estilo. Si el control usa solo el estilo implícito (el de su tipo, por ejemplo
    /// el de todos los Label), se le pone ese mismo estilo como explícito, así la próxima vez también se puede reaplicar.
    /// </summary>
    private static void Reaplicar(VisualElement visual)
    {
        Style? estilo = visual.Style ?? EstiloImplicito(visual.GetType());
        if (estilo is null) return;

        visual.Style = null;
        visual.Style = estilo;
    }

    /// <summary>El estilo implícito de un tipo (en Styles.xaml), buscando también en los tipos padre si corresponde.</summary>
    private static Style? EstiloImplicito(Type tipo)
    {
        var recursos = Application.Current?.Resources;
        if (recursos is null) return null;

        for (Type? actual = tipo; actual is not null && actual != typeof(object); actual = actual.BaseType)
        {
            if (actual.FullName is string clave
                && recursos.TryGetValue(clave, out var valor) && valor is Style estilo
                && (actual == tipo || estilo.ApplyToDerivedTypes))
                return estilo;
        }
        return null;
    }
}
