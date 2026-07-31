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
    public DbSet<SsoTicketUsado> SsoTicketsUsados => Set<SsoTicketUsado>();
    public DbSet<LicenciaManual> LicenciasManuales => Set<LicenciaManual>();
    public DbSet<AsistenteTurno> AsistenteTurnos => Set<AsistenteTurno>();
    public DbSet<AsistenteHerramientaUso> AsistenteHerramientasUso => Set<AsistenteHerramientaUso>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Nombre).HasMaxLength(120);
            e.Property(u => u.Email).HasMaxLength(160);
            e.Property(u => u.Rol).HasMaxLength(20);
            e.Property(u => u.Dni).HasMaxLength(20);
            e.HasIndex(u => u.Dni).IsUnique(); // Postgres: los NULL no chocan entre sí (NULLS DISTINCT)
        });

        modelBuilder.Entity<Empleado>(e =>
        {
            e.HasIndex(x => x.EmployeeInternalId).IsUnique();
            e.Property(x => x.EmployeeInternalId).HasMaxLength(100);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Apellido).HasMaxLength(120);
            e.Property(x => x.Telefono).HasMaxLength(40);
            e.Property(x => x.Area).HasMaxLength(120);
            e.Property(x => x.Legajo).HasMaxLength(20);
        });

        modelBuilder.Entity<NovedadDiaria>(e =>
        {
            e.HasIndex(x => new { x.EmpleadoId, x.Fecha }).IsUnique(); // idempotencia
            e.HasIndex(x => x.Fecha); // consultas por rango del asistente (antes: seq scan)
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
            e.Property(x => x.HoraParteNoche).HasMaxLength(5);
        });

        modelBuilder.Entity<LicenciaManual>(e =>
        {
            e.HasIndex(x => x.EmpleadoId);
            e.Property(x => x.Motivo).HasMaxLength(100);
            e.Property(x => x.CreadaPor).HasMaxLength(120);
            e.HasOne(x => x.Empleado).WithMany().HasForeignKey(x => x.EmpleadoId);
        });

        modelBuilder.Entity<AsistenteTurno>(e =>
        {
            e.HasIndex(x => x.UsuarioId);
            e.HasIndex(x => x.CreadoUtc); // el rate limit consulta por ventana de tiempo
            e.Property(x => x.Modelo).HasMaxLength(60);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId);
        });

        modelBuilder.Entity<AsistenteHerramientaUso>(e =>
        {
            e.HasIndex(x => x.TurnoId);
            e.Property(x => x.Herramienta).HasMaxLength(60);
            e.Property(x => x.ArgsJson).HasMaxLength(2000);
            e.HasOne(x => x.Turno).WithMany().HasForeignKey(x => x.TurnoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SsoTicketUsado>(e =>
        {
            e.HasKey(x => x.Jti); // PK = quemado atómico: insert duplicado falla y el ticket se rechaza
            e.Property(x => x.Jti).HasMaxLength(64);
        });
    }
}
