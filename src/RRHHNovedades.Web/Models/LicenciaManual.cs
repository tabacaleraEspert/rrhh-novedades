namespace RRHHNovedades.Web.Models;

/// <summary>
/// Licencia cargada a mano por RRHH (no existe en Humand): "reserva de puesto", acuerdos, etc.
/// Mientras rige, las ausencias injustificadas (y pendientes futuros) del empleado se justifican
/// con este motivo, tanto en la ingesta diaria como retroactivamente al crearla.
/// </summary>
public class LicenciaManual
{
    public int Id { get; set; }

    public int EmpleadoId { get; set; }
    public Empleado Empleado { get; set; } = null!;

    public DateOnly Desde { get; set; }

    /// <summary>Fin inclusive; null = sin fecha de fin (rige hasta que se elimine).</summary>
    public DateOnly? Hasta { get; set; }

    /// <summary>Motivo libre (ej. "Reserva de puesto"). Los ya usados se ofrecen como opciones.</summary>
    public string Motivo { get; set; } = string.Empty;

    /// <summary>Usuario que la cargó (nombre del login).</summary>
    public string CreadaPor { get; set; } = string.Empty;

    public DateTime CreadaUtc { get; set; }
}
