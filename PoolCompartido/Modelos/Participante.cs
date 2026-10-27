namespace PoolCompartido;

/// <summary>
/// HU29: una persona del viaje, como se dibuja en la tarjeta (un círculo con sus iniciales).
/// </summary>
/// <param name="Color">Número de color (0, 1, 2…): la pantalla lo convierte en un color de la paleta.</param>
public record Participante(string Nombre, string InicialesTexto, int Color, bool EsConductor)
{
    /// <summary>Texto para lectores de pantalla: "Francis, conductor".</summary>
    public string Descripcion => EsConductor ? $"{Nombre}, conductor" : $"{Nombre}, pasajero";

    /// <summary>"Francisco Díaz" → "FD"; "pablogandini3" → "P".</summary>
    public static string Iniciales(string? nombre)
    {
        var partes = (nombre ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0) return "?";
        if (partes.Length == 1) return partes[0][..1].ToUpperInvariant();
        return (partes[0][..1] + partes[^1][..1]).ToUpperInvariant();
    }

    /// <summary>
    /// Siempre el mismo color para la misma persona (en todos los dispositivos).
    /// No se usa GetHashCode porque cambia cada vez que se abre la app.
    /// </summary>
    public static int ColorPara(string? uid, int cantidadColores)
    {
        if (string.IsNullOrEmpty(uid) || cantidadColores <= 0) return 0;
        int suma = 0;
        foreach (char c in uid)
            suma = (suma * 31 + c) % 10007;
        return suma % cantidadColores;
    }
}
