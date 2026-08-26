namespace RRHHNovedades.Web.Options;

public class AsistenciaOptions
{
    public const string SectionName = "Asistencia";

    /// <summary>Zona horaria para cortes y comparaciones. Default Argentina.</summary>
    public string TimeZone { get; set; } = "America/Argentina/Buenos_Aires";

    /// <summary>Hora de envío del parte del turno mañana (HH:mm). Parametrizable.</summary>
    public string HoraParteManana { get; set; } = "07:00";

    /// <summary>Hora de envío del parte del turno tarde (HH:mm). Parametrizable.</summary>
    public string HoraParteTarde { get; set; } = "14:00";

    /// <summary>Hora de envío del parte del turno noche (HH:mm). Reporta la jornada del día anterior.</summary>
    public string HoraParteNoche { get; set; } = "06:00";

    /// <summary>Hora límite para separar turno mañana de turno tarde según el inicio de jornada.</summary>
    public string CorteTurnoTarde { get; set; } = "13:00";

    /// <summary>Tolerancia de tardanza en minutos (informativo; el LATE lo marca Humand).</summary>
    public int ToleranciaTardanzaMin { get; set; } = 10;

    /// <summary>
    /// Horarios extra de sincronización automática con Humand (HH:mm), además de la que ocurre
    /// antes de cada parte. Configurable; default 2 veces por día.
    /// </summary>
    public List<string> AutoSyncHoras { get; set; } = ["10:30", "16:30"];

    /// <summary>
    /// Feriados (yyyy-MM-dd) para presentismo. Se SUMAN a los que marque Humand (`holidays`),
    /// que hoy la empresa no tiene cargados. Mantener por año en appsettings.
    /// </summary>
    public List<string> Feriados { get; set; } = [];

    /// <summary>Hora (HH:mm) del re-sync retroactivo diario. Ver <see cref="ResyncRetroDias"/>.</summary>
    public string HoraResyncRetro { get; set; } = "05:00";

    /// <summary>
    /// Cuántos días hacia atrás re-sincroniza el re-sync retroactivo diario (0 = deshabilitado).
    /// Los certificados médicos, licencias y francos suelen cargarse/corregirse en Humand días
    /// después de la falta (a veces recién al revisar la planilla para la liquidación mensual);
    /// sin re-mirar el pasado, esos días quedan congelados como injustificados. 30 días cubre
    /// el ciclo completo de liquidación (26→25).
    /// </summary>
    public int ResyncRetroDias { get; set; } = 30;
}
