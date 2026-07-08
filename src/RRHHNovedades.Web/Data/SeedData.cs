using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        // Usuarios iniciales (idempotente por email: agrega los que falten sin pisar los existentes).
        // El login es por PIN; el PIN inicial es 0000. Cambiarlo en producción desde Configuración.
        await SeedUsuarioAsync(db, "Administrador", "desarrollador1@tabacaleraespert.com", "0000", Roles.Admin);
        // Equipo de RRHH: ve el dashboard y las consultas, sin acceso a Configuración.
        await SeedUsuarioAsync(db, "RRHH", "rrhh@tabacaleraespert.com", "0000", Roles.RRHH);

        if (!db.Destinatarios.Any())
        {
            // Listado inicial de RRHH (placeholder — completar/ajustar desde Configuración).
            db.Destinatarios.Add(new DestinatarioParte { Nombre = "RRHH (ejemplo)", Telefono = "+5491100000000", Activo = false });
        }

        // Configuración del bot (fila única). Horarios editables desde la UI.
        if (!await db.ConfiguracionParte.AnyAsync())
            db.ConfiguracionParte.Add(new ConfiguracionParte { HoraParteManana = "07:00", HoraParteTarde = "14:00" });

        await db.SaveChangesAsync();
    }

    private static async Task SeedUsuarioAsync(AppDbContext db, string nombre, string email, string password, string rol)
    {
        if (await db.Usuarios.AnyAsync(u => u.Email == email)) return;
        db.Usuarios.Add(new Usuario
        {
            Nombre = nombre,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Rol = rol,
            Activo = true
        });
    }
}
