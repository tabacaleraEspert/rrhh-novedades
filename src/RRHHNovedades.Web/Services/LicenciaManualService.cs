using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services;

/// <summary>Fila para listar licencias manuales con los datos del empleado resueltos.</summary>
public record LicenciaManualVista(
    int Id, int EmpleadoId, string? Legajo, string ApellidoNombre, string? Area,
    DateOnly Desde, DateOnly? Hasta, string Motivo, string CreadaPor);

/// <summary>Empleado para el selector del alta (búsqueda por nombre/legajo).</summary>
public record EmpleadoOpcion(int Id, string? Legajo, string ApellidoNombre, string? Area)
{
    public override string ToString() => Legajo is null ? ApellidoNombre : $"{ApellidoNombre} ({Legajo})";
}

public interface ILicenciaManualService
{
    Task<IReadOnlyList<LicenciaManualVista>> ListarAsync(CancellationToken ct = default);
    Task<IReadOnlyList<EmpleadoOpcion>> EmpleadosActivosAsync(CancellationToken ct = default);

    /// <summary>Motivos ya usados, para ofrecerlos como opciones repetibles en el alta.</summary>
    Task<IReadOnlyList<string>> MotivosAsync(CancellationToken ct = default);

    /// <summary>Crea la licencia y justifica retroactivamente las novedades ya ingestadas del período.
    /// Devuelve cuántos días ya cargados se justificaron.</summary>
    Task<int> CrearAsync(int empleadoId, DateOnly desde, DateOnly? hasta, string motivo, string creadaPor, CancellationToken ct = default);

    /// <summary>Elimina la licencia y revierte los días que ELLA justificó (pasado → injustificada,
    /// futuro → pendiente). Devuelve cuántos días se revirtieron.</summary>
    Task<int> EliminarAsync(int licenciaId, CancellationToken ct = default);
}

/// <summary>
/// Licencias manuales de RRHH (ej. "reserva de puesto"): justifican las ausencias que Humand
/// marca injustificadas, sin tocar Humand. Actúan en dos momentos:
///   1. Al CREAR: se justifican retroactivamente las novedades ya guardadas del período.
///   2. En cada INGESTA: <see cref="IngestaService"/> aplica las licencias vigentes del día.
/// Los días tocados quedan marcados <c>EsManual</c>, así el borrado revierte exactamente eso.
/// Nota liquidación: en Presentismo el motivo aparece como columna de licencia propia; si es
/// sin goce de sueldo, incluir "sin goce" en el nombre del motivo para que descuente.
/// </summary>
public class LicenciaManualService(IDbContextFactory<AppDbContext> dbFactory, IReloj reloj) : ILicenciaManualService
{
    public async Task<IReadOnlyList<LicenciaManualVista>> ListarAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LicenciasManuales
            .Include(l => l.Empleado)
            .OrderByDescending(l => l.Desde)
            .Select(l => new LicenciaManualVista(
                l.Id, l.EmpleadoId, l.Empleado.Legajo, l.Empleado.ApellidoNombre, l.Empleado.Area,
                l.Desde, l.Hasta, l.Motivo, l.CreadaPor))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmpleadoOpcion>> EmpleadosActivosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Empleados.Where(e => e.Activo)
            .OrderBy(e => e.Apellido).ThenBy(e => e.Nombre)
            .Select(e => new EmpleadoOpcion(e.Id, e.Legajo, e.ApellidoNombre, e.Area))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> MotivosAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LicenciasManuales
            .Select(l => l.Motivo).Distinct().OrderBy(m => m)
            .ToListAsync(ct);
    }

    public async Task<int> CrearAsync(int empleadoId, DateOnly desde, DateOnly? hasta, string motivo, string creadaPor, CancellationToken ct = default)
    {
        motivo = motivo.Trim();
        if (motivo.Length == 0) throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));
        if (hasta < desde) throw new ArgumentException("hasta < desde", nameof(hasta));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.LicenciasManuales.Add(new LicenciaManual
        {
            EmpleadoId = empleadoId,
            Desde = desde,
            Hasta = hasta,
            Motivo = motivo,
            CreadaPor = creadaPor,
            CreadaUtc = DateTime.UtcNow
        });

        // Retroactivo sobre lo ya ingestado: injustificadas y pendientes del período pasan a
        // justificadas con este motivo. (Presente/Tarde/Franco/Feriado no se tocan: si vino, vino.)
        var afectadas = await db.Novedades
            .Where(n => n.EmpleadoId == empleadoId && n.Fecha >= desde && (hasta == null || n.Fecha <= hasta)
                && (n.Estado == EstadoJornada.AusenteInjustificado || n.Estado == EstadoJornada.Pendiente))
            .ToListAsync(ct);
        foreach (var n in afectadas)
        {
            n.Estado = EstadoJornada.AusenteJustificado;
            n.MotivoNovedad = motivo;
            n.EsManual = true;
            n.ActualizadoUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return afectadas.Count;
    }

    public async Task<int> EliminarAsync(int licenciaId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lic = await db.LicenciasManuales.FirstOrDefaultAsync(l => l.Id == licenciaId, ct);
        if (lic is null) return 0;

        // Revierte SOLO los días que justificó una licencia manual (EsManual). Si hubiera otra
        // licencia manual solapada del mismo empleado, el próximo sync del día la re-aplica.
        var hoy = reloj.Hoy;
        var afectadas = await db.Novedades
            .Where(n => n.EmpleadoId == lic.EmpleadoId && n.Fecha >= lic.Desde
                && (lic.Hasta == null || n.Fecha <= lic.Hasta) && n.EsManual)
            .ToListAsync(ct);
        foreach (var n in afectadas)
        {
            n.Estado = n.Fecha > hoy ? EstadoJornada.Pendiente : EstadoJornada.AusenteInjustificado;
            n.MotivoNovedad = null;
            n.EsManual = false;
            n.ActualizadoUtc = DateTime.UtcNow;
        }

        db.LicenciasManuales.Remove(lic);
        await db.SaveChangesAsync(ct);
        return afectadas.Count;
    }
}
