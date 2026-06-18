using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Dispara los 2 partes diarios (mañana y tarde) y las sincronizaciones automáticas extra,
/// en los horarios configurados (TZ Argentina). Antes de cada parte sincroniza el día desde Humand.
/// Los horarios de los partes se leen de la DB (configurables desde Configuración sin redeploy).
/// </summary>
public class ParteScheduler(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    IOptionsMonitor<AsistenciaOptions> asistencia,
    IReloj reloj,
    ILogger<ParteScheduler> logger) : BackgroundService
{
    // Clave de disparo por día: "parte-Manana", "parte-Tarde", "sync-10:30", ...
    private readonly HashSet<(DateOnly Dia, string Clave)> _disparados = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ParteScheduler activo (TZ {Tz})", asistencia.CurrentValue.TimeZone);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error en el tick del scheduler");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var opt = asistencia.CurrentValue;
        var hoy = reloj.Hoy;
        var horaActual = reloj.HoraActual;

        _disparados.RemoveWhere(d => d.Dia < hoy);

        // Horarios de los partes: configurables desde la UI (tabla ConfiguracionParte). Si por algo
        // no se puede leer, caemos a los de appsettings.
        var (horaManana, horaTarde) = await LeerHorariosParteAsync(opt, ct);

        // Partes (sincronizan + envían)
        foreach (var (turno, horaTxt) in new[]
                 {
                     (Turno.Manana, horaManana),
                     (Turno.Tarde, horaTarde)
                 })
        {
            if (!Vence(horaTxt, horaActual, hoy, $"parte-{turno}")) continue;
            logger.LogInformation("Disparando parte {Turno} del {Hoy}", turno, hoy);
            await EjecutarParteAsync(hoy, turno, ct);
        }

        // Sincronizaciones automáticas extra (solo refrescan datos, no envían)
        foreach (var horaTxt in opt.AutoSyncHoras ?? [])
        {
            if (!Vence(horaTxt, horaActual, hoy, $"sync-{horaTxt}")) continue;
            logger.LogInformation("Auto-sync de las {Hora} del {Hoy}", horaTxt, hoy);
            await EjecutarSyncAsync(hoy, ct);
        }
    }

    private async Task<(string manana, string tarde)> LeerHorariosParteAsync(AsistenciaOptions opt, CancellationToken ct)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var cfg = await db.ConfiguracionParte.AsNoTracking().FirstOrDefaultAsync(ct);
            if (cfg is not null) return (cfg.HoraParteManana, cfg.HoraParteTarde);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo leer ConfiguracionParte; uso los horarios de appsettings");
        }
        return (opt.HoraParteManana, opt.HoraParteTarde);
    }

    /// <summary>True si la hora configurada ya pasó hoy y todavía no se disparó (lo marca como disparado).</summary>
    private bool Vence(string horaTxt, TimeOnly horaActual, DateOnly hoy, string clave)
    {
        if (!TimeOnly.TryParse(horaTxt, out var hora)) return false;
        if (horaActual < hora) return false;
        if (!_disparados.Add((hoy, clave))) return false;
        return true;
    }

    private async Task EjecutarParteAsync(DateOnly fecha, Turno turno, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ingesta = scope.ServiceProvider.GetRequiredService<IIngestaService>();
        var parte = scope.ServiceProvider.GetRequiredService<IParteService>();

        // Guard persistente: si el parte de hoy ya salió (registrado en EnviosParte), no reenviar.
        // El HashSet en memoria se borra en cada reinicio; sin esto, todo deploy/crash re-spamearía
        // a RRHH (en Container Apps cada revisión reinicia el contenedor).
        if (await parte.YaSeEnvioAsync(fecha, turno, ct))
        {
            logger.LogInformation("Parte {Turno} {Fecha} ya estaba enviado; se omite (evita reenvío por reinicio).", turno, fecha);
            return;
        }

        await ingesta.SincronizarEmpleadosAsync(ct);
        await ingesta.SincronizarDiaAsync(fecha, ct);
        await parte.EnviarParteAsync(fecha, turno, ct);
    }

    private async Task EjecutarSyncAsync(DateOnly fecha, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ingesta = scope.ServiceProvider.GetRequiredService<IIngestaService>();

        await ingesta.SincronizarEmpleadosAsync(ct);
        await ingesta.SincronizarDiaAsync(fecha, ct);
    }
}
