using Microsoft.Extensions.Caching.Memory;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Un renglón de distribución (sector, turno, sexo...): etiqueta + cantidad.</summary>
public record Distribucion(string Etiqueta, int Cantidad);

/// <summary>Un jefe con su gente a cargo (relationships de Humand).</summary>
public record JefeEquipo(
    string JefeId,
    string JefeNombre,
    string? JefeArea,
    IReadOnlyList<string> Reportes) // nombres, orden alfabético
{
    public int Cantidad => Reportes.Count;
}

public record CumpleEmpleado(int Dia, string ApellidoNombre, string? Area, int CumpleAnios);

public record DemografiaReporte(
    int Activos,
    IReadOnlyList<Distribucion> PorSector,     // desc por cantidad
    IReadOnlyList<Distribucion> PorTurno,      // Mañana/Tarde/Noche
    IReadOnlyList<Distribucion> PorSexo,       // vacío si Humand no trae el dato
    double? AntiguedadPromedioAnios,           // null si no hay hiringDate
    IReadOnlyList<Distribucion> AntiguedadBuckets,
    double? EdadPromedioAnios,                 // null si no hay birthdate
    IReadOnlyList<Distribucion> EdadBuckets,
    IReadOnlyList<CumpleEmpleado> CumplesDelMes,
    IReadOnlyList<JefeEquipo> Jefes,           // desc por cantidad de reportes
    int SinFechaIngreso,
    int SinFechaNacimiento);

public interface IDemografiaService
{
    /// <summary>Foto demográfica de la dotación activa, en vivo desde Humand (cache 10 min).</summary>
    Task<DemografiaReporte> ReporteAsync(CancellationToken ct = default);
}

/// <summary>
/// Sección Demografía (NUEVA, ago-2026): distribución de la dotación por sector, turno y
/// sexo, antigüedad, pirámide etaria, cumpleaños del mes y esquema de jefes. Todo sale del
/// `/users` de Humand en vivo (sin persistir): status, hiringDate, birthdate, segmentaciones,
/// relationships (jefe) y el campo custom Sexo si la organización lo cargó. Los bloques cuyo
/// dato Humand no trae quedan vacíos y la página no los muestra — nunca se inventa.
/// </summary>
public class DemografiaService(IHumandService humand, IMemoryCache cache, IReloj reloj) : IDemografiaService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<DemografiaReporte> ReporteAsync(CancellationToken ct = default)
    {
        var todos = (await cache.GetOrCreateAsync("demo:users", e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheTtl;
            return humand.ObtenerEmpleadosAsync(ct);
        }))!;

        return Calcular(todos, reloj.Hoy);
    }

    /// <summary>Puro y testeable: toda la sección se calcula acá.</summary>
    internal static DemografiaReporte Calcular(IReadOnlyList<EmpleadoHumand> todos, DateOnly hoy)
    {
        // Sin status (API vieja o mock parcial) se asume activo; DEACTIVATED/UNCLAIMED quedan fuera.
        var activos = todos.Where(u => u.Status is null or "ACTIVE").ToList();

        var porSector = activos
            .GroupBy(u => string.IsNullOrWhiteSpace(u.Area) ? "Sin sector" : u.Area!)
            .Select(g => new Distribucion(g.Key, g.Count()))
            .OrderByDescending(d => d.Cantidad).ThenBy(d => d.Etiqueta)
            .ToList();

        var porTurno = activos
            .GroupBy(u => TurnoDe(u))
            .Select(g => new Distribucion(g.Key, g.Count()))
            .OrderByDescending(d => d.Cantidad)
            .ToList();

        var porSexo = activos
            .Where(u => !string.IsNullOrWhiteSpace(u.Sexo))
            .GroupBy(u => u.Sexo!.Trim())
            .Select(g => new Distribucion(g.Key, g.Count()))
            .OrderByDescending(d => d.Cantidad)
            .ToList();

        var conIngreso = activos.Where(u => u.FechaIngreso is not null).ToList();
        double? antProm = conIngreso.Count == 0 ? null
            : Math.Round(conIngreso.Average(u => Anios(u.FechaIngreso!.Value, hoy)), 1);
        var antBuckets = Buckets(conIngreso.Select(u => Anios(u.FechaIngreso!.Value, hoy)),
            [(1, "< 1 año"), (3, "1-3 años"), (5, "3-5 años"), (10, "5-10 años"), (20, "10-20 años"), (int.MaxValue, "20+ años")]);

        var conNacimiento = activos.Where(u => u.FechaNacimiento is not null).ToList();
        double? edadProm = conNacimiento.Count == 0 ? null
            : Math.Round(conNacimiento.Average(u => Anios(u.FechaNacimiento!.Value, hoy)), 1);
        var edadBuckets = Buckets(conNacimiento.Select(u => Anios(u.FechaNacimiento!.Value, hoy)),
            [(25, "< 25"), (35, "25-34"), (45, "35-44"), (55, "45-54"), (int.MaxValue, "55+")]);

        var cumples = conNacimiento
            .Where(u => u.FechaNacimiento!.Value.Month == hoy.Month)
            .Select(u => new CumpleEmpleado(
                u.FechaNacimiento!.Value.Day,
                ApellidoNombre(u), u.Area,
                hoy.Year - u.FechaNacimiento!.Value.Year))
            .OrderBy(c => c.Dia).ThenBy(c => c.ApellidoNombre)
            .ToList();

        var porId = activos.ToDictionary(u => u.EmployeeInternalId, u => u);
        var jefes = activos
            .Where(u => !string.IsNullOrWhiteSpace(u.JefeId))
            .GroupBy(u => u.JefeId!)
            .Select(g =>
            {
                var jefe = porId.GetValueOrDefault(g.Key);
                return new JefeEquipo(
                    g.Key,
                    jefe is null ? g.Key : ApellidoNombre(jefe),
                    jefe?.Area,
                    g.Select(ApellidoNombre).OrderBy(n => n).ToList());
            })
            .OrderByDescending(j => j.Cantidad).ThenBy(j => j.JefeNombre)
            .ToList();

        return new DemografiaReporte(
            activos.Count, porSector, porTurno, porSexo,
            antProm, antBuckets, edadProm, edadBuckets,
            cumples, jefes,
            activos.Count - conIngreso.Count,
            activos.Count - conNacimiento.Count);
    }

    /// <summary>Años (con decimales) entre una fecha y hoy. internal para testear.</summary>
    internal static double Anios(DateOnly desde, DateOnly hoy) =>
        (hoy.DayNumber - desde.DayNumber) / 365.25;

    // Mismo criterio que la ingesta: la segmentación "Turno" con "noche" manda; sin ella,
    // no se puede inferir el horario acá (no hay fichajes), así que va Mañana/Tarde según
    // lo que diga el nombre del ítem, y "Sin dato" si no hay segmentación.
    internal static string TurnoDe(EmpleadoHumand u)
    {
        var s = u.SegTurno;
        if (string.IsNullOrWhiteSpace(s)) return "Sin segmentación";
        var lower = Normalizar(s);
        if (lower.Contains("noche")) return "Noche";
        if (lower.Contains("tarde")) return "Tarde";
        if (lower.Contains("manana") || lower.Contains("mañana")) return "Mañana";
        return s.Trim(); // ítem con nombre propio (ej. "Turno A"): se muestra tal cual
    }

    private static string Normalizar(string s) =>
        s.ToLowerInvariant().Replace("á", "a").Replace("é", "e").Replace("í", "i").Replace("ó", "o").Replace("ú", "u");

    /// <summary>Buckets por límite superior exclusivo. Solo devuelve buckets con gente.</summary>
    internal static IReadOnlyList<Distribucion> Buckets(
        IEnumerable<double> valores, (double LimiteExcl, string Etiqueta)[] rangos)
    {
        var counts = new int[rangos.Length];
        foreach (var v in valores)
            for (int i = 0; i < rangos.Length; i++)
                if (v < rangos[i].LimiteExcl) { counts[i]++; break; }
        return rangos.Select((r, i) => new Distribucion(r.Etiqueta, counts[i]))
            .Where(d => d.Cantidad > 0).ToList();
    }

    private static string ApellidoNombre(EmpleadoHumand u) =>
        string.IsNullOrWhiteSpace(u.Apellido) ? u.Nombre : $"{u.Apellido}, {u.Nombre}".Trim(' ', ',');
}
