using Microsoft.Extensions.Diagnostics.HealthChecks;
using RRHHNovedades.Web.Services.Asistente;

namespace RRHHNovedades.Web.HealthChecks;

/// <summary>
/// Los .md de Conocimiento/ tienen que llegar al contenedor (CopyToOutputDirectory): si faltan,
/// el asistente responde peor EN SILENCIO. Degraded (no Unhealthy): la app sigue sirviendo.
/// </summary>
public sealed class ConocimientoHealthCheck(PromptBuilder prompts) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var faltantes = prompts.ConocimientoFaltante();
        return Task.FromResult(faltantes.Count == 0
            ? HealthCheckResult.Healthy("conocimiento completo")
            : HealthCheckResult.Degraded($"faltan: {string.Join(", ", faltantes)}"));
    }
}
