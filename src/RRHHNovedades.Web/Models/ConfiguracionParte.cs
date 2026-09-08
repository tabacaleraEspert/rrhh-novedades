namespace RRHHNovedades.Web.Models;

/// <summary>
/// Configuración editable (fila única, Id=1): horarios de los 3 partes diarios y visibilidad de bloqueados.
/// Editable desde Configuración sin redeploy; el scheduler la lee en cada tick.
/// </summary>
public class ConfiguracionParte
{
    public int Id { get; set; }

    /// <summary>Hora de envío del parte de la mañana (HH:mm).</summary>
    public string HoraParteManana { get; set; } = "07:00";

    /// <summary>Hora de envío del parte de la tarde (HH:mm).</summary>
    public string HoraParteTarde { get; set; } = "14:00";

    /// <summary>Hora de envío del parte del turno noche (HH:mm). Reporta la jornada del día ANTERIOR.</summary>
    public string HoraParteNoche { get; set; } = "06:00";

    /// <summary>
    /// Mostrar los empleados bloqueados (⛔ en Humand) en el dashboard, planillas, listados y asistente.
    /// No afecta al parte de WhatsApp: los bloqueados nunca se envían.
    /// </summary>
    public bool MostrarBloqueados { get; set; }
}
