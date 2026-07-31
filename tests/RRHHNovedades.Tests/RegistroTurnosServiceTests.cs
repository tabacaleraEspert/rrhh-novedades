using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services.Asistente;
using Xunit;

namespace RRHHNovedades.Tests;

/// <summary>Registro/auditoría de turnos del asistente: rate limit por filas, costo calculado
/// al cerrar y purga con UPDATE a NULL (nunca DELETE: borraría el contador del rate limit).</summary>
public class RegistroTurnosServiceTests
{
    private sealed class InMemoryFactory(string dbName) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            var opt = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName).Options;
            return new AppDbContext(opt);
        }
    }

    private static async Task<(RegistroTurnosService svc, InMemoryFactory factory)> SetupAsync(string db)
    {
        var factory = new InMemoryFactory(db);
        await using var ctx = factory.CreateDbContext();
        ctx.Usuarios.Add(new Usuario { Id = 1, Nombre = "Ana", Email = "a@a", PasswordHash = "x", Rol = "RRHH" });
        await ctx.SaveChangesAsync();
        var svc = new RegistroTurnosService(factory,
            Microsoft.Extensions.Options.Options.Create(new AsistenteOptions()),
            NullLogger<RegistroTurnosService>.Instance);
        return (svc, factory);
    }

    [Fact]
    public async Task Abrir_y_cerrar_guarda_tokens_y_costo_calculado()
    {
        var (svc, factory) = await SetupAsync(nameof(Abrir_y_cerrar_guarda_tokens_y_costo_calculado));

        var id = await svc.AbrirAsync(1, "¿quién faltó?");
        await svc.CerrarAsync(id, ok: true, null, "Nadie.", vueltas: 2, duracionMs: 1500,
            new UsoTokens(Entrada: 10_000, Salida: 500, Cacheados: 8_000));

        await using var db = factory.CreateDbContext();
        var t = await db.AsistenteTurnos.SingleAsync();
        Assert.True(t.Ok);
        Assert.Equal("Nadie.", t.Respuesta);
        Assert.Equal(10_000, t.TokensEntrada);
        Assert.Equal(8_000, t.TokensCacheados);
        // (2000 frescos × 1,75 + 8000 cacheados × 0,175 + 500 salida × 14) / 1M
        Assert.Equal((2000 * 1.75m + 8000 * 0.175m + 500 * 14m) / 1_000_000m, t.CostoUsd);
    }

    [Fact]
    public async Task Rate_limit_cuenta_solo_la_ventana_del_usuario()
    {
        var (svc, factory) = await SetupAsync(nameof(Rate_limit_cuenta_solo_la_ventana_del_usuario));

        await using (var db = factory.CreateDbContext())
        {
            db.AsistenteTurnos.AddRange(
                new AsistenteTurno { UsuarioId = 1, CreadoUtc = DateTime.UtcNow.AddMinutes(-1), Modelo = "m" },
                new AsistenteTurno { UsuarioId = 1, CreadoUtc = DateTime.UtcNow.AddMinutes(-30), Modelo = "m" }); // fuera de ventana
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await svc.ConsultasRecientesAsync(1));
    }

    [Fact]
    public async Task Purga_es_update_a_null_y_no_borra_filas()
    {
        var (svc, factory) = await SetupAsync(nameof(Purga_es_update_a_null_y_no_borra_filas));

        await using (var db = factory.CreateDbContext())
        {
            db.AsistenteTurnos.AddRange(
                new AsistenteTurno { UsuarioId = 1, CreadoUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), Pregunta = "vieja", Respuesta = "r", Modelo = "m" },
                new AsistenteTurno { UsuarioId = 1, CreadoUtc = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), Pregunta = "reciente", Modelo = "m" });
            await db.SaveChangesAsync();
        }

        var purgados = await svc.PurgarAsync(new DateOnly(2026, 6, 1));

        Assert.Equal(1, purgados);
        await using var check = factory.CreateDbContext();
        Assert.Equal(2, await check.AsistenteTurnos.CountAsync()); // las filas quedan
        var vieja = await check.AsistenteTurnos.SingleAsync(t => t.CreadoUtc.Year == 2026 && t.CreadoUtc.Month == 1);
        Assert.Null(vieja.Pregunta);
        Assert.Null(vieja.Respuesta);
        Assert.Equal("reciente", (await check.AsistenteTurnos.SingleAsync(t => t.CreadoUtc.Month == 7)).Pregunta);
    }
}
