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

    /// <summary>Última vez que se sincronizó/recalculó desde Humand.</summary>
    public DateTime ActualizadoUtc { get; set; }
}
