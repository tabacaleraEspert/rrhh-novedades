using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Empleado> Empleados => Set<Empleado>();
    public DbSet<NovedadDiaria> Novedades => Set<NovedadDiaria>();
    public DbSet<DestinatarioParte> Destinatarios => Set<DestinatarioParte>();
    public DbSet<EnvioParte> EnviosParte => Set<EnvioParte>();
    public DbSet<ConfiguracionParte> ConfiguracionParte => Set<ConfiguracionParte>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Nombre).HasMaxLength(120);
            e.Property(u => u.Email).HasMaxLength(160);
            e.Property(u => u.Rol).HasMaxLength(20);
        });

        modelBuilder.Entity<Empleado>(e =>
        {
            e.HasIndex(x => x.EmployeeInternalId).IsUnique();
            e.Property(x => x.EmployeeInternalId).HasMaxLength(100);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Apellido).HasMaxLength(120);
            e.Property(x => x.Telefono).HasMaxLength(40);
            e.Property(x => x.Area).HasMaxLength(120);
        });

        modelBuilder.Entity<NovedadDiaria>(e =>
        {
            e.HasIndex(x => new { x.EmpleadoId, x.Fecha }).IsUnique(); // idempotencia
            e.Property(x => x.MotivoNovedad).HasMaxLength(200);
            e.HasOne(x => x.Empleado).WithMany().HasForeignKey(x => x.EmpleadoId);
        });

        modelBuilder.Entity<DestinatarioParte>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Telefono).HasMaxLength(40);
        });

        modelBuilder.Entity<EnvioParte>(e =>
        {
            e.Property(x => x.Telefono).HasMaxLength(40);
            e.Property(x => x.MessageSid).HasMaxLength(64);
            e.Property(x => x.Error).HasMaxLength(500);
        });

        modelBuilder.Entity<ConfiguracionParte>(e =>
        {
            e.Property(x => x.HoraParteManana).HasMaxLength(5);
            e.Property(x => x.HoraParteTarde).HasMaxLength(5);
        });
    }
}
