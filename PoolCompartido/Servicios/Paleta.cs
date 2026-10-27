namespace PoolCompartido;

/// <summary>
/// HU19: colores de los estados de un viaje (éxito, aviso, peligro), según el tema y si está
/// activada la paleta para daltonismo. Los colores están en texto (#RRGGBB) para que esta clase
/// no dependa de la pantalla y se pueda probar sola.
/// La paleta para daltonismo usa la de Okabe-Ito (azul, naranja, bermellón): no combina
/// rojo con verde, que es lo que más cuesta distinguir.
/// Todos los colores de texto tienen contraste de al menos 4,5 contra el fondo de las tarjetas (norma WCAG AA).
/// </summary>
public record Paleta(string Exito, string ExitoFondo, string Peligro, string PeligroBorde, string Aviso)
{
    public static Paleta Para(bool daltonico, bool oscuro) => (daltonico, oscuro) switch
    {
        (false, false) => new("#1B7F48", "#E3F4EA", "#C62828", "#F1B5B5", "#A85A0A"),
        (false, true)  => new("#3DBE7A", "#1F3A2B", "#F07178", "#6B2F33", "#E8A04A"),
        (true, false)  => new("#0072B2", "#DCEBF7", "#B84E00", "#F3C7A6", "#8A6D00"),
        (true, true)   => new("#56B4E9", "#1B3346", "#F0864A", "#6B3A1E", "#F0E442")
    };
}
