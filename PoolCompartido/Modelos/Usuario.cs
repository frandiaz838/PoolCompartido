namespace PoolCompartido;

/// <summary>Usuario que inició sesión (HU17).</summary>
/// <param name="Uid">Identificador único que le da Firebase.</param>
/// <param name="Nombre">Nombre que ven los demás.</param>
/// <param name="Email">Mail con el que se registró.</param>
public record Usuario(string Uid, string Nombre, string Email);
