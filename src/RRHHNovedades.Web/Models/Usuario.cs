namespace RRHHNovedades.Web.Models;

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty; // "Admin" o "Operador"
    public bool Activo { get; set; } = true;
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Operador = "Operador";
}
