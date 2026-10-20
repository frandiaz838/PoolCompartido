namespace PoolCompartido;

/// <summary>Excepción base del inicio de sesión (HU17).</summary>
public class AutenticacionException : Exception
{
    public AutenticacionException(string mensaje) : base(mensaje) { }
}

/// <summary>Mail o contraseña incorrectos.</summary>
public class CredencialesInvalidasException : AutenticacionException
{
    public CredencialesInvalidasException()
        : base("Mail o contraseña incorrectos.") { }
}

/// <summary>Ya hay una cuenta registrada con ese mail.</summary>
public class UsuarioExistenteException : AutenticacionException
{
    public UsuarioExistenteException()
        : base("Ya existe una cuenta con ese mail. Probá iniciar sesión.") { }
}

/// <summary>La sesión guardada ya no es válida y hay que volver a ingresar.</summary>
public class SesionVencidaException : AutenticacionException
{
    public SesionVencidaException()
        : base("Tu sesión venció. Volvé a iniciar sesión.") { }
}
