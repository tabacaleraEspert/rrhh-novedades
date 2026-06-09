using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        if (!db.Usuarios.Any())
        {
            // Usuario admin inicial. Cambiar la contraseña en producción.
            db.Usuarios.Add(new Usuario
            {
                Nombre = "Administrador",
                Email = "desarrollador1@tabacaleraespert.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("espert"),
                Rol = Roles.Admin,
                Activo = true
            });
        }

        if (!db.Destinatarios.Any())
        {
            // Listado inicial de RRHH (placeholder — completar/ajustar desde Configuración).
            db.Destinatarios.Add(new DestinatarioParte { Nombre = "RRHH (ejemplo)", Telefono = "+5491100000000", Activo = false });
        }

        await db.SaveChangesAsync();
    }
}
