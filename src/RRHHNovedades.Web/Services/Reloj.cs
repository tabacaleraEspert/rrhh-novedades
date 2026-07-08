using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

/// <summary>
/// Reloj de la aplicación: TODA fecha/hora "de negocio" se resuelve acá en la zona horaria
/// configurada (default Argentina/Buenos_Aires), nunca con la hora local del server.
/// En Azure el host corre en UTC; usar <c>DateTime.Today</c>/<c>Now</c> daría el día/hora equivocados.
/// El almacenamiento sigue siendo en UTC (auditoría); esto es solo para comparar y mostrar.
/// </summary>
public interface IReloj
{
    /// <summary>Instante actual en hora Argentina.</summary>
    DateTimeOffset Ahora { get; }

    /// <summary>Fecha calendario actual en Argentina.</summary>
    DateOnly Hoy { get; }

    /// <summary>Hora del día actual en Argentina.</summary>
    TimeOnly HoraActual { get; }

    /// <summary>Convierte un instante UTC (ej. <c>ActualizadoUtc</c>) a hora Argentina para mostrar.</summary>
    DateTime EnLocal(DateTime utc);
}

public sealed class RelojArgentino : IReloj
{
    private readonly TimeZoneInfo _tz;

    public RelojArgentino(IOptions<AsistenciaOptions> opt) =>
        _tz = ResolverTimeZone(opt.Value.TimeZone);

    public DateTimeOffset Ahora => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz);
    public DateOnly Hoy => DateOnly.FromDateTime(Ahora.DateTime);
    public TimeOnly HoraActual => TimeOnly.FromDateTime(Ahora.DateTime);

    public DateTime EnLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _tz);

    // El id IANA ("America/Argentina/Buenos_Aires") funciona en Linux/macOS; en Windows el id
    // es "Argentina Standard Time". Probamos ambos para que ande en cualquier host.
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
