namespace RRHHNovedades.Web.Models;

public class Empleado
{
    public int Id { get; set; }

    /// <summary>Identificador del empleado en Humand (employeeInternalId). Clave de cruce.</summary>
    public string EmployeeInternalId { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Area { get; set; }

    /// <summary>Número de legajo (campo personalizado "Legajo" de Humand).</summary>
    public string? Legajo { get; set; }

    /// <summary>Turno principal del empleado. Puede inferirse del horario del día en la ingesta.</summary>
    public Turno Turno { get; set; } = Turno.Manana;

    public bool Activo { get; set; } = true;

    /// <summary>
    /// RRHH marca en Humand a los empleados "bloqueados" agregando el símbolo ⛔ (o 🚫) al nombre.
    /// Un bloqueado NUNCA sale en el parte de WhatsApp; en el resto del sistema se muestra u oculta
    /// según <see cref="ConfiguracionParte.MostrarBloqueados"/> (filtro global en AppDbContext).
    /// Se recalcula en cada sincronización de empleados.
    /// </summary>
    public bool Bloqueado { get; set; }

    /// <summary>¿El texto trae la marca de bloqueo que RRHH pone en Humand (⛔ / 🚫)?</summary>
    public static bool TieneMarcaBloqueo(string? texto) =>
        texto is not null && (texto.Contains('\u26D4') || texto.Contains("\U0001F6AB"));

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
    public string ApellidoNombre => string.IsNullOrWhiteSpace(Apellido) ? Nombre : $"{Apellido}, {Nombre}".Trim(' ', ',');
}
