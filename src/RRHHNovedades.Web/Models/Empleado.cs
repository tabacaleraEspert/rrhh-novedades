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

    /// <summary>Turno principal del empleado. Puede inferirse del horario del día en la ingesta.</summary>
    public Turno Turno { get; set; } = Turno.Manana;

    public bool Activo { get; set; } = true;

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();
    public string ApellidoNombre => string.IsNullOrWhiteSpace(Apellido) ? Nombre : $"{Apellido}, {Nombre}".Trim(' ', ',');
}
