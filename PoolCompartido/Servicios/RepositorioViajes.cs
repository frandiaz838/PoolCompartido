using System.Text.Json;

namespace PoolCompartido;

/// <summary>
/// Caché local de los viajes en un archivo JSON del dispositivo.
/// Se usa para mostrar los últimos viajes conocidos cuando no hay internet.
/// Usa "using" para que el archivo se cierre siempre (administración de recursos locales).
/// </summary>
internal class RepositorioViajes
{
    public string Ruta { get; }

    public RepositorioViajes(string ruta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        Ruta = ruta;
    }

    /// <summary>Lee los viajes guardados (id → datos). Si no hay archivo, devuelve vacío.</summary>
    /// <exception cref="IOException">No se pudo leer el archivo.</exception>
    /// <exception cref="JsonException">El archivo está dañado.</exception>
    public Dictionary<string, ViajeDatos> Cargar()
    {
        // Validación temprana: si nunca se guardó nada, no es un error
        if (!File.Exists(Ruta))
            return new Dictionary<string, ViajeDatos>();

        using (var flujo = new FileStream(Ruta, FileMode.Open, FileAccess.Read))
        {
            return JsonSerializer.Deserialize(flujo, JsonPool.Default.DictionaryStringViajeDatos)
                   ?? new Dictionary<string, ViajeDatos>();
        }
    }

    /// <summary>Guarda todos los viajes, reemplazando el archivo anterior.</summary>
    /// <exception cref="IOException">No se pudo escribir el archivo.</exception>
    /// <exception cref="UnauthorizedAccessException">No hay permiso para escribir.</exception>
    public void Guardar(Dictionary<string, ViajeDatos> viajes)
    {
        string? carpeta = Path.GetDirectoryName(Ruta);
        if (!string.IsNullOrEmpty(carpeta))
            Directory.CreateDirectory(carpeta);

        using (var flujo = new FileStream(Ruta, FileMode.Create, FileAccess.Write))
        {
            JsonSerializer.Serialize(flujo, viajes, JsonPool.Default.DictionaryStringViajeDatos);
        }
    }
}
