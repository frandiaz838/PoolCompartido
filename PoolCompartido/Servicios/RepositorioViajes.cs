using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoolCompartido;

/// <summary>
/// Guarda y lee los viajes en un archivo JSON del dispositivo.
/// Usa "using" para que el archivo se cierre siempre (administración de recursos locales).
/// </summary>
public class RepositorioViajes
{
    public string Ruta { get; }

    public RepositorioViajes(string ruta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        Ruta = ruta;
    }

    /// <summary>Lee los viajes guardados. Si todavía no hay archivo, devuelve una lista vacía.</summary>
    /// <exception cref="IOException">No se pudo leer el archivo.</exception>
    /// <exception cref="JsonException">El archivo está dañado.</exception>
    public List<Viaje> Cargar()
    {
        // Validación temprana: la primera vez que se abre la app no hay archivo, y no es un error
        if (!File.Exists(Ruta))
            return new List<Viaje>();

        using (var flujo = new FileStream(Ruta, FileMode.Open, FileAccess.Read))
        {
            var datos = JsonSerializer.Deserialize(flujo, ViajesJsonContext.Default.ListViajeGuardado)
                        ?? new List<ViajeGuardado>();
            return datos.Select(d => d.AViaje()).ToList();
        }
    }

    /// <summary>Guarda todos los viajes, reemplazando el archivo anterior.</summary>
    /// <exception cref="IOException">No se pudo escribir el archivo.</exception>
    /// <exception cref="UnauthorizedAccessException">No hay permiso para escribir.</exception>
    public void Guardar(IEnumerable<Viaje> viajes)
    {
        var datos = viajes.Select(ViajeGuardado.Desde).ToList();

        string? carpeta = Path.GetDirectoryName(Ruta);
        if (!string.IsNullOrEmpty(carpeta))
            Directory.CreateDirectory(carpeta);

        using (var flujo = new FileStream(Ruta, FileMode.Create, FileAccess.Write))
        {
            JsonSerializer.Serialize(flujo, datos, ViajesJsonContext.Default.ListViajeGuardado);
        }
    }
}

/// <summary>Cómo queda guardado un viaje en el archivo (solo datos, sin eventos).</summary>
public class ViajeGuardado
{
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string Hora { get; set; } = "";
    public string Conductor { get; set; } = "";
    public string Comentario { get; set; } = "";
    public int AsientosDisponibles { get; set; }
    public List<string> Pasajeros { get; set; } = new();
    public bool Cancelado { get; set; }

    public static ViajeGuardado Desde(Viaje viaje) => new()
    {
        Origen = viaje.Origen,
        Destino = viaje.Destino,
        Fecha = viaje.Fecha,
        Hora = viaje.Hora,
        Conductor = viaje.Conductor,
        Comentario = viaje.Comentario,
        AsientosDisponibles = viaje.AsientosDisponibles,
        Pasajeros = viaje.Pasajeros.ToList(),
        Cancelado = viaje.Cancelado
    };

    public Viaje AViaje()
    {
        var viaje = new Viaje
        {
            Origen = Origen,
            Destino = Destino,
            Fecha = Fecha,
            Hora = Hora,
            Conductor = Conductor,
            Comentario = Comentario,
            AsientosDisponibles = AsientosDisponibles
        };
        foreach (var pasajero in Pasajeros)
            viaje.AgregarPasajero(pasajero);
        if (Cancelado)
            viaje.Cancelar();
        return viaje;
    }
}

/// <summary>Le dice a .NET cómo convertir los viajes a JSON (necesario para que funcione igual en Android).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<ViajeGuardado>))]
internal partial class ViajesJsonContext : JsonSerializerContext
{
}
