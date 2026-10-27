using System.Globalization;

namespace PoolCompartido;

/// <summary>
/// HU29: convierte el número de color de un Participante (0, 1, 2…) en el color de su avatar.
/// Todos tienen contraste de al menos 5:1 con el texto blanco de las iniciales.
/// </summary>
public class ColorAvatarConverter : IValueConverter
{
    private static readonly Color[] Paleta =
    {
        Color.FromArgb("#5B45A8"), Color.FromArgb("#0E6E7A"), Color.FromArgb("#A63D63"), Color.FromArgb("#2F62A8"),
        Color.FromArgb("#8A5A12"), Color.FromArgb("#2E6E3E"), Color.FromArgb("#7A4FB8"), Color.FromArgb("#B04A2E")
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Paleta[value is int i ? Math.Abs(i) % Paleta.Length : 0];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
