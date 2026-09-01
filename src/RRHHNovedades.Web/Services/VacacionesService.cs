using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

/// <summary>Nivel de alerta por saldo de vacaciones acumulado.</summary>
public enum SaldoSemaforo { Ok, Advertencia, Riesgo }

/// <summary>Saldo de vacaciones de un empleado, cruzado con la base local.</summary>
public record SaldoVacaciones(
    string EmployeeInternalId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    double Dias,
    SaldoSemaforo Semaforo);

/// <summary>Solicitud de licencia/vacaciones, cruzada con la base local.</summary>
public record SolicitudVacaciones(
    string EmployeeInternalId,
    string? Legajo,
    string ApellidoNombre,
    string? Area,
    string Politica,
    DateOnly Desde,
    DateOnly Hasta,
    string Estado,   // APPROVED | IN_PROGRESS
    double Dias,
    bool EnCurso);   // hoy cae dentro del rango

public record VacacionesReporte(
    IReadOnlyList<SaldoVacaciones> Saldos,           // orden: más días primero
    IReadOnlyList<SolicitudVacaciones> Solicitudes); // orden: por fecha de inicio

public interface IVacacionesService
{
    /// <summary>Saldos de vacaciones + solicitudes del rango, en vivo desde Humand (cache corto).</summary>
    Task<VacacionesReporte> ReporteAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default);
}

/// <summary>
/// Sección Vacaciones (NUEVA, ago-2026): saldos por empleado (`/time-off/balances`) y
/// solicitudes (`/time-off/requests`), directo de Humand SIN persistir — a diferencia de
/// las novedades diarias, el saldo es un stock que Humand ya mantiene; guardarlo solo
/// crearía otra copia que envejece. Cache en memoria 10 min para no gastar rate limit.
/// El semáforo de saldo alto usa los umbrales de <see cref="FeaturesOptions"/>.
/// </summary>
public class VacacionesService(
    IHumandService humand,
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache,
    IOptions<FeaturesOptions> features,
    IReloj reloj) : IVacacionesService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<VacacionesReporte> ReporteAsync(DateOnly desde, DateOnly hasta, string? area = null, CancellationToken ct = default)
    {
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        var saldos = (await cache.GetOrCreateAsync("vac:saldos", e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return humand.ObtenerSaldosTimeOffAsync(ct);
        }))!;
        var solicitudes = (await cache.GetOrCreateAsync($"vac:sol:{desde:yyyyMMdd}:{hasta:yyyyMMdd}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return humand.ObtenerSolicitudesTimeOffAsync(desde, hasta, ct);
        }))!;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var empleados = (await db.Empleados.AsNoTracking().ToListAsync(ct))
            .ToDictionary(x => x.EmployeeInternalId, x => x);

        var opt = features.Value;
        var hoy = reloj.Hoy;

        // Saldos: solo la política de vacaciones (las demás políticas son licencias puntuales
        // sin "stock" acumulable que interese acá), sumando por empleado si viniera partida.
        var filasSaldo = saldos
            .Where(s => s.Politica.Contains("vacacion", StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.EmployeeInternalId)
            .Select(g =>
            {
                var e = empleados.GetValueOrDefault(g.Key);
                var dias = g.Sum(x => x.Saldo);
                return new SaldoVacaciones(
                    g.Key, e?.Legajo,
                    e?.ApellidoNombre ?? g.Key,
                    e?.Area,
                    dias,
                    Semaforo(dias, opt.SaldoVacacionesAdvertencia, opt.SaldoVacacionesRiesgo));
            })
            .Where(s => area == null || s.Area == area)
            .OrderByDescending(s => s.Dias)
            .ToList();

        var filasSol = solicitudes
            .Select(s =>
            {
                var e = empleados.GetValueOrDefault(s.EmployeeInternalId);
                return new SolicitudVacaciones(
                    s.EmployeeInternalId, e?.Legajo,
                    e?.ApellidoNombre ?? s.EmployeeInternalId,
                    e?.Area,
                    s.Politica, s.Desde, s.Hasta, s.Estado, s.Dias,
                    s.Desde <= hoy && hoy <= s.Hasta);
            })
            .Where(s => area == null || s.Area == area)
            .OrderBy(s => s.Desde).ThenBy(s => s.ApellidoNombre)
            .ToList();

        return new VacacionesReporte(filasSaldo, filasSol);
    }

    /// <summary>Umbrales inclusivos hacia arriba: dias &gt;= riesgo ⇒ rojo, &gt;= advertencia ⇒ naranja. internal para testear.</summary>
    internal static SaldoSemaforo Semaforo(double dias, int advertencia, int riesgo) =>
        dias >= riesgo ? SaldoSemaforo.Riesgo
        : dias >= advertencia ? SaldoSemaforo.Advertencia
        : SaldoSemaforo.Ok;
}
