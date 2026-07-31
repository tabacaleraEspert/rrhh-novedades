namespace RRHHNovedades.Web.Models;

/// <summary>
/// Auditoría de un turno del asistente IA (una pregunta → una respuesta). El rate limit
/// cuenta FILAS de esta tabla, por eso la purga pone Pregunta/Respuesta en NULL con UPDATE
/// y nunca borra filas (un DELETE resetearía el límite).
/// </summary>
public class AsistenteTurno
{
    public int Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    public DateTime CreadoUtc { get; set; }

    /// <summary>Texto libre; NULL después de la purga.</summary>
    public string? Pregunta { get; set; }
    public string? Respuesta { get; set; }

    public string Modelo { get; set; } = string.Empty;
    public int Vueltas { get; set; }
    public int DuracionMs { get; set; }

    public int TokensEntrada { get; set; }
    public int TokensSalida { get; set; }
    public int TokensCacheados { get; set; }

    /// <summary>Calculado al cerrar el turno con la tarifa vigente (sobrevive a cambios de precio).</summary>
    public decimal CostoUsd { get; set; }

    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>Una ejecución de herramienta dentro de un turno (FK real, sin heurísticas por ventana).</summary>
public class AsistenteHerramientaUso
{
    public int Id { get; set; }

    public int TurnoId { get; set; }
    public AsistenteTurno Turno { get; set; } = null!;

    public string Herramienta { get; set; } = string.Empty;
    public string? ArgsJson { get; set; }
    public int DuracionMs { get; set; }
}
