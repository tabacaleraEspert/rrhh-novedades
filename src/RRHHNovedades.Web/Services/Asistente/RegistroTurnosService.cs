using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services.Asistente;

public interface IRegistroTurnosService
{
    /// <summary>Consultas del usuario en la ventana del rate limit. NULL si la cuenta falló:
    /// el caller debe RECHAZAR (falla cerrada; fallar abierto ya costó plata en Bachi).</summary>
    Task<int?> ConsultasRecientesAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>Abre el turno ANTES de llamar al modelo (la fila existe para que el rate limit la cuente).</summary>
    Task<int> AbrirAsync(int usuarioId, string pregunta, CancellationToken ct = default);

    Task RegistrarHerramientaAsync(int turnoId, string herramienta, string? argsJson, int duracionMs, CancellationToken ct = default);

    Task CerrarAsync(int turnoId, bool ok, string? error, string? respuesta, int vueltas, int duracionMs, UsoTokens uso, CancellationToken ct = default);

    /// <summary>Purga el texto libre de turnos viejos con UPDATE a NULL. NUNCA borra filas
    /// (el rate limit cuenta filas; un DELETE lo resetearía). Devuelve cuántos purgó.</summary>
    Task<int> PurgarAsync(DateOnly antesDe, CancellationToken ct = default);

    /// <summary>Costo en USD de un uso de tokens con la tarifa vigente.</summary>
    decimal CalcularCosto(UsoTokens uso);
}

public class RegistroTurnosService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<AsistenteOptions> options,
    ILogger<RegistroTurnosService> logger) : IRegistroTurnosService
{
    private readonly AsistenteOptions _opt = options.Value;

    public async Task<int?> ConsultasRecientesAsync(int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var desde = DateTime.UtcNow.AddMinutes(-_opt.RateLimitVentanaMinutos);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.AsistenteTurnos.CountAsync(t => t.UsuarioId == usuarioId && t.CreadoUtc >= desde, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rate limit del asistente: no se pudo contar (se rechaza la consulta)");
            return null;
        }
    }

    public async Task<int> AbrirAsync(int usuarioId, string pregunta, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var turno = new AsistenteTurno
        {
            UsuarioId = usuarioId,
            Pregunta = pregunta,
            Modelo = _opt.Modelo,
            CreadoUtc = DateTime.UtcNow,
        };
        db.AsistenteTurnos.Add(turno);
        await db.SaveChangesAsync(ct);
        return turno.Id;
    }

    public async Task RegistrarHerramientaAsync(int turnoId, string herramienta, string? argsJson, int duracionMs, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AsistenteHerramientasUso.Add(new AsistenteHerramientaUso
            {
                TurnoId = turnoId,
                Herramienta = herramienta,
                ArgsJson = argsJson is { Length: > 2000 } ? argsJson[..2000] : argsJson,
                DuracionMs = duracionMs,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // La auditoría de herramientas no corta el turno del usuario.
            logger.LogError(ex, "No se pudo registrar el uso de {Herramienta} del turno {TurnoId}", herramienta, turnoId);
        }
    }

    public async Task CerrarAsync(int turnoId, bool ok, string? error, string? respuesta, int vueltas, int duracionMs, UsoTokens uso, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var turno = await db.AsistenteTurnos.FirstOrDefaultAsync(t => t.Id == turnoId, ct);
            if (turno is null) return;

            turno.Ok = ok;
            turno.Error = error is { Length: > 500 } ? error[..500] : error;
            turno.Respuesta = respuesta;
            turno.Vueltas = vueltas;
            turno.DuracionMs = duracionMs;
            turno.TokensEntrada = uso.Entrada;
            turno.TokensSalida = uso.Salida;
            turno.TokensCacheados = uso.Cacheados;
            turno.CostoUsd = CalcularCosto(uso);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo cerrar el turno {TurnoId} del asistente", turnoId);
        }
    }

    /// <summary>Costo con la tarifa vigente de options. Los cacheados se descuentan de la entrada.</summary>
    public decimal CalcularCosto(UsoTokens uso)
    {
        var frescos = Math.Max(0, uso.Entrada - uso.Cacheados);
        return (frescos * _opt.PrecioInputPorMTok
              + uso.Cacheados * _opt.PrecioCachePorMTok
              + uso.Salida * _opt.PrecioOutputPorMTok) / 1_000_000m;
    }

    public async Task<int> PurgarAsync(DateOnly antesDe, CancellationToken ct = default)
    {
        var corte = antesDe.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // UPDATE, no DELETE (el rate limit cuenta filas). El volumen es chico: entidades tracked
        // alcanzan y funcionan también con el provider InMemory de los tests.
        var turnos = await db.AsistenteTurnos
            .Where(t => t.CreadoUtc < corte && (t.Pregunta != null || t.Respuesta != null))
            .ToListAsync(ct);
        foreach (var t in turnos)
        {
            t.Pregunta = null;
            t.Respuesta = null;
        }
        await db.SaveChangesAsync(ct);
        return turnos.Count;
    }
}
