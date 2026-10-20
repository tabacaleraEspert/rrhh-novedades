namespace RRHHNovedades.Web.Models;

/// <summary>
/// Foto del estado de un empleado en un día. Idempotente por (EmpleadoId + Fecha):
/// la ingesta hace upsert, así un re-sync puede pasar una ausencia de injustificada a justificada.
/// </summary>
public class NovedadDiaria
{
    public int Id { get; set; }

    public int EmpleadoId { get; set; }
    public Empleado Empleado { get; set; } = null!;

    public DateOnly Fecha { get; set; }
    public Turno Turno { get; set; }

    public EstadoJornada Estado { get; set; }

    public int MinutosTardanza { get; set; }

    public TimeOnly? HoraEntrada { get; set; }
    public TimeOnly? HoraSalida { get; set; }

    /// <summary>Motivo del permiso/licencia cuando aplica (ej. "Vacaciones", "Certificado médico").</summary>
    public string? MotivoNovedad { get; set; }

    /// <summary>El día era feriado según Humand (array `holidays` del day-summary). Para presentismo.</summary>
    public bool EsFeriado { get; set; }

    /// <summary>Justificada por una <see cref="LicenciaManual"/> (no por un permiso de Humand).</summary>
    public bool EsManual { get; set; }

    /// <summary>
    /// Horas de refrigerio del día según las políticas de marcaje de Humand (categoría
    /// "Refrigerio" de categorizedHours del day-summary). 0 si no tuvo.
    /// </summary>
    public double HorasRefrigerio { get; set; }

    /// <summary>Horas extra al 50 % del día (categoría "Horas extras al 50" de Humand). 0 si no tuvo.</summary>
    public double HorasExtras50 { get; set; }

    /// <summary>Horas extra al 100 % del día (categoría "Horas extras al 100" de Humand). 0 si no tuvo.</summary>
    public double HorasExtras100 { get; set; }

    /// <summary>Última vez que se sincronizó/recalculó desde Humand.</summary>
    public DateTime ActualizadoUtc { get; set; }
}
