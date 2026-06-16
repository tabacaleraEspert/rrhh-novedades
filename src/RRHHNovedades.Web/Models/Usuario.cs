namespace RRHHNovedades.Web.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty; // "Admin" o "RRHH"
    public bool Activo { get; set; } = true;
}

public static class Roles
{
    /// <summary>Acceso total, incluida la Configuración y los endpoints operativos.</summary>
    public const string Admin = "Admin";
    /// <summary>Equipo de RRHH: dashboard, empleados y consultas. No accede a Configuración.</summary>
    public const string RRHH = "RRHH";
}
