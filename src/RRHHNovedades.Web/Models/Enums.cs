namespace RRHHNovedades.Web.Models;

/// <summary>Estado de la jornada de un empleado en un día (los 4 buckets del parte + franco + pendiente).</summary>
public enum EstadoJornada
{
    Presente,             // Fichó en hora
    Tarde,                // Fichó pasada la tolerancia
    AusenteInjustificado, // No fichó y no hay permiso
    AusenteJustificado,   // No fichó pero hay permiso/licencia/vacaciones que cubre el día
    FrancoNoLaborable,    // No corresponde trabajar (sin turno asignado / no es día hábil)
    Pendiente             // El turno todavía no empezó (no evaluable aún). NUEVO al final: no renumerar (se persiste como int).
}

public enum Turno
{
    Manana,
    Tarde
}
