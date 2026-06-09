using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Dispara los 2 partes diarios (mañana y tarde) en los horarios configurados (TZ Argentina).
/// En cada disparo sincroniza el día desde Humand y envía el parte al listado de destinatarios.
/// </summary>
public class ParteScheduler(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<AsistenciaOptions> asistencia,
    ILogger<ParteScheduler> logger) : BackgroundService
{
    private readonly HashSet<(DateOnly, Turno)> _disparados = [];

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

        // Limpiar disparos de días anteriores.
        _disparados.RemoveWhere(d => d.Item1 < hoy);

        foreach (var (turno, horaTxt) in new[]
                 {
                     (Turno.Manana, opt.HoraParteManana),
                     (Turno.Tarde, opt.HoraParteTarde)
                 })
        {
            if (!TimeOnly.TryParse(horaTxt, out var hora)) continue;
            if (horaActual < hora) continue;
            if (_disparados.Contains((hoy, turno))) continue;

            _disparados.Add((hoy, turno));
            logger.LogInformation("Disparando parte {Turno} del {Hoy}", turno, hoy);
            await EjecutarParteAsync(hoy, turno, ct);
        }
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
