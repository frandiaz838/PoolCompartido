using System.Globalization;

namespace PoolCompartido;

/// <summary>HU28: una fecha de la rutina que no se publicó, y por qué.</summary>
public record FechaSalteada(DateTime Fecha, string Motivo);

/// <summary>HU28: lo que pasó al publicar una rutina fija, para mostrar el resumen al final.</summary>
public record ResultadoSerie(IReadOnlyList<Viaje> Publicados, IReadOnlyList<FechaSalteada> Salteadas)
{
    /// <summary>
    /// "Se publicaron 14 viajes. Se salteó el 03/11 porque ya tenés otro viaje a esa hora."
    /// Las fechas con el mismo motivo se juntan: "Se saltearon el 03/11 y el 05/11 porque...".
    /// </summary>
    public string Resumen
    {
        get
        {
            string resumen = Publicados.Count switch
            {
                0 => "No se publicó ningún viaje.",
                1 => "Se publicó 1 viaje.",
                _ => $"Se publicaron {Publicados.Count} viajes."
            };

            foreach (var grupo in Salteadas.GroupBy(s => s.Motivo))
            {
                var fechas = grupo.Select(s => "el " + s.Fecha.ToString("dd/MM", CultureInfo.InvariantCulture)).ToList();
                string lista = fechas.Count == 1
                    ? fechas[0]
                    : string.Join(", ", fechas.Take(fechas.Count - 1)) + " y " + fechas[^1];
                string verbo = fechas.Count == 1 ? "Se salteó" : "Se saltearon";
                resumen += $" {verbo} {lista} porque {grupo.Key}.";
            }
            return resumen;
        }
    }
}
