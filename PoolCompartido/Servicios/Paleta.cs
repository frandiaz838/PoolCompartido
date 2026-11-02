namespace PoolCompartido;

/// <summary>
/// HU19: colores de los estados de un viaje (éxito, aviso, peligro), según el tema y si está
/// activada la paleta para daltonismo. Los colores están en texto (#RRGGBB) para que esta clase
/// no dependa de la pantalla y se pueda probar sola.
/// La paleta para daltonismo usa la de Okabe-Ito (azul, naranja, bermellón): no combina
/// rojo con verde, que es lo que más cuesta distinguir.
/// Todos los colores de texto tienen contraste de al menos 4,5 contra el fondo y las tarjetas (norma WCAG AA),
/// y los bordes al menos 3 (contorno de los botones de peligro).
/// </summary>
/// <param name="AvisoFondo">Fondo suave para etiquetas de aviso (por ejemplo "💺 1 lugar libre"), HU29.</param>
/// <param name="SobrePeligro">Texto encima del color de peligro (el número de la campanita, "Borrar mi cuenta"):
/// blanco en el tema claro y casi negro en el oscuro, donde el rojo es más claro.</param>
public record Paleta(string Exito, string ExitoFondo, string Peligro, string PeligroBorde, string Aviso, string AvisoFondo,
                     string SobrePeligro)
{
    public static Paleta Para(bool daltonico, bool oscuro) => (daltonico, oscuro) switch
    {
        (false, false) => new("#1B7F48", "#EDF8F1", "#C62828", "#D35454", "#A85A0A", "#FFF6E8", "#FFFFFF"),
        (false, true)  => new("#3DBE7A", "#1F3A2B", "#F07178", "#B5545A", "#E8A04A", "#3A2A14", "#1C1B1F"),
        (true, false)  => new("#0072B2", "#EAF3FB", "#B84E00", "#C96A2C", "#8A6D00", "#FFFAE0", "#FFFFFF"),
        (true, true)   => new("#56B4E9", "#1B3346", "#F0864A", "#B5622F", "#F0E442", "#36330F", "#1C1B1F")
    };
}
