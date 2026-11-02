using System.Globalization;

namespace PoolCompartido;

/// <summary>
/// HU29: convierte el número de color de un Participante (0, 1, 2…) en el color de su avatar.
/// Los colores son los tokens ColorAvatar0 a ColorAvatar7 de Colors.xaml:
/// todos tienen contraste de al menos 5:1 con el texto blanco de las iniciales.
/// </summary>
public class ColorAvatarConverter : IValueConverter
{
    private const int CantidadColores = 8;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int numero = value is int i ? Math.Abs(i) % CantidadColores : 0;
        return Application.Current?.Resources.TryGetValue($"ColorAvatar{numero}", out var color) == true ? color : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
