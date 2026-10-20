namespace PoolCompartido;

/// <summary>
/// Campo de texto que deja libre el espacio de la derecha para poner un ícono adentro
/// (por ejemplo, el ojo de "ver contraseña"), así el texto no queda debajo del ícono.
/// Hereda de Entry: se usa igual que un Entry común.
/// El espacio se agrega en MauiProgram.cs, en el control nativo de cada plataforma.
/// </summary>
public class EntryConIcono : Entry
{
    /// <summary>Espacio reservado a la derecha para el ícono.</summary>
    public const double EspacioIcono = 40;
}
