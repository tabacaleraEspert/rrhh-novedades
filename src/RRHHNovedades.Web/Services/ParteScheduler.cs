using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Dispara los 2 partes diarios (mañana y tarde) y las sincronizaciones automáticas extra,
/// en los horarios configurados (TZ Argentina). Antes de cada parte sincroniza el día desde Humand.
/// </summary>
public class ParteScheduler(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AsistenciaOptions> asistencia,
    ILogger<ParteScheduler> logger) : BackgroundService
{
    // Clave de disparo por día: "parte-Manana", "parte-Tarde", "sync-10:30", ...
    private readonly HashSet<(DateOnly Dia, string Clave)> _disparados = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tz = ResolverTimeZone(asistencia.CurrentValue.TimeZone);
        logger.LogInformation("ParteScheduler activo (TZ {Tz})", tz.Id);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(tz, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error en el tick del scheduler");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task TickAsync(TimeZoneInfo tz, CancellationToken ct)
    {
        var opt = asistencia.CurrentValue;
        var ahora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var hoy = DateOnly.FromDateTime(ahora.DateTime);
        var horaActual = TimeOnly.FromDateTime(ahora.DateTime);

        _disparados.RemoveWhere(d => d.Dia < hoy);

        // Partes (sincronizan + envían)
        foreach (var (turno, horaTxt) in new[]
                 {
                     (Turno.Manana, opt.HoraParteManana),
                     (Turno.Tarde, opt.HoraParteTarde)
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

    private static TimeZoneInfo ResolverTimeZone(string id)
    {
        foreach (var candidate in new[] { id, "Argentina Standard Time", "America/Argentina/Buenos_Aires" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
