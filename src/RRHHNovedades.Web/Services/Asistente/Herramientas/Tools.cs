using System.Globalization;
using System.Text;
using System.Text.Json;
using RRHHNovedades.Web.Models;

namespace RRHHNovedades.Web.Services.Asistente.Herramientas;

// Las 9 herramientas del asistente. Todas reutilizan los servicios del tablero (los números
// del chat tienen que coincidir con los de la pantalla — ese es el punto) y devuelven JSON
// compactado. Fechas de entrada: yyyy-MM-dd (se le indica al modelo en cada schema).

/// <summary>Nombre/legajo → candidatos con Id. Siempre el primer paso cuando nombran a una persona.</summary>
public sealed class BuscarEmpleadoTool(ILicenciaManualService licencias) : IAsistenteTool
{
    public string Nombre => "buscar_empleado";
    public string Descripcion =>
        "Busca empleados por nombre, apellido o legajo y devuelve sus datos (id, legajo, área). " +
        "Llamala SIEMPRE antes que cualquier herramienta que pida empleadoId cuando el usuario nombra a una persona. " +
        "Si devuelve varios candidatos, preguntale al usuario cuál es antes de seguir.";
    public string Etiqueta => "buscando a la persona…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "texto":{"type":"string","description":"Nombre, apellido o legajo (parcial o completo)"}
        },"required":["texto"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var texto = Normalizar(Args.Texto(args, "texto"));
        var todos = await licencias.EmpleadosActivosAsync(ct);
        var candidatos = todos
            .Where(e => Normalizar(e.ApellidoNombre).Contains(texto) || (e.Legajo is not null && e.Legajo.Contains(texto, StringComparison.OrdinalIgnoreCase)))
            .Take(10)
            .ToList();
        return Compactador.Lista(candidatos, notaVacio: "SIN_RESULTADOS: ningún empleado coincide. Probá con menos letras o verificá la ortografía.");
    }

    // Sin tildes y en minúsculas: "Pérez" y "perez" tienen que matchear.
    internal static string Normalizar(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

/// <summary>Día por día de una persona: el "¿qué pasó con Pérez el 15/07?".</summary>
public sealed class HistorialEmpleadoTool(IConsultaAsistenteService consultas) : IAsistenteTool
{
    public string Nombre => "get_historial_empleado";
    public string Descripcion =>
        "Historial día por día de UN empleado en un rango: estado (presente/tarde/ausente/franco), motivo de licencia, " +
        "horarios de entrada/salida y minutos de tardanza. Usala para '¿qué pasó con X tal día/semana/mes?'.";
    public string Etiqueta => "revisando el historial de la persona…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "empleadoId":{"type":"integer","description":"Id devuelto por buscar_empleado"},
          "desde":{"type":"string","description":"Fecha inicial yyyy-MM-dd"},
          "hasta":{"type":"string","description":"Fecha final yyyy-MM-dd (inclusive)"}
        },"required":["empleadoId","desde","hasta"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var h = await consultas.HistorialAsync(Args.Entero(args, "empleadoId"), Args.Fecha(args, "desde"), Args.Fecha(args, "hasta"), ct);
        if (h is null) return "ERROR: no existe un empleado con ese id. Usá buscar_empleado primero.";
        if (h.Dias.Count == 0)
            return $"{Compactador.Objeto(new { h.EmpleadoId, h.ApellidoNombre, h.Legajo, h.Area })}\n{Compactador.NotaVacio}";
        return Compactador.Objeto(new { h.EmpleadoId, h.ApellidoNombre, h.Legajo, h.Area }) + "\n" + Compactador.Lista(h.Dias);
    }
}

/// <summary>Ausentismo agregado por día/semana/mes/persona (mismos números que la pantalla Ausentismo).</summary>
public sealed class AusentismoTool(IAusentismoService ausentismo, IConsultaAsistenteService consultas) : IAsistenteTool
{
    public string Nombre => "get_ausentismo";
    public string Descripcion =>
        "Ausencias de un rango de fechas agrupadas por 'dia', 'semana' (lunes-domingo), 'mes' o 'persona' (ranking de quién faltó más), " +
        "separando justificadas de injustificadas, con tasa de ausentismo sobre jornadas esperadas. Opcionalmente filtra por área. " +
        "Usala para '¿cuántas ausencias hubo?', '¿quién faltó más?', 'ausentismo de julio'.";
    public string Etiqueta => "revisando el ausentismo del período…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "desde":{"type":"string","description":"Fecha inicial yyyy-MM-dd"},
          "hasta":{"type":"string","description":"Fecha final yyyy-MM-dd (inclusive)"},
          "agrupar":{"type":"string","enum":["dia","semana","mes","persona"],"description":"Cómo agrupar el resultado"},
          "area":{"type":"string","description":"Área exacta para filtrar (opcional)"}
        },"required":["desde","hasta","agrupar"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var desde = Args.Fecha(args, "desde");
        var hasta = Args.Fecha(args, "hasta");
        var area = Args.TextoOpcional(args, "area");
        var agrupar = Args.Texto(args, "agrupar");

        // Sin default silencioso: un valor desconocido es un error, no "dia" (lección Bachi).
        if (agrupar == "persona")
            return Compactador.Lista(await consultas.AusenciasPorPersonaAsync(desde, hasta, area, ct));

        var r = await ausentismo.ReporteAsync(desde, hasta, area, ct);
        return agrupar switch
        {
            "dia" => Compactador.Lista(r.PorDia, notaVacio: null),
            "semana" => Compactador.Lista(r.PorSemana, notaVacio: null),
            "mes" => Compactador.Lista(r.PorMes, notaVacio: null),
            _ => throw new ArgumentException($"agrupar debe ser dia|semana|mes|persona (recibí '{agrupar}')."),
        };
    }
}

/// <summary>Tardanzas por persona (no existe en ninguna pantalla: es exclusiva del asistente).</summary>
public sealed class TardanzasTool(IConsultaAsistenteService consultas) : IAsistenteTool
{
    public string Nombre => "get_tardanzas";
    public string Descripcion =>
        "Tardanzas de un rango agrupadas por persona: días llegados tarde, minutos totales y última fecha. " +
        "Opcionalmente filtra por área o por un empleado. Usala para 'tardanzas del mes', '¿quién llega tarde?'.";
    public string Etiqueta => "sumando las tardanzas…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "desde":{"type":"string","description":"Fecha inicial yyyy-MM-dd"},
          "hasta":{"type":"string","description":"Fecha final yyyy-MM-dd (inclusive)"},
          "area":{"type":"string","description":"Área exacta para filtrar (opcional)"},
          "empleadoId":{"type":"integer","description":"Limitar a un empleado (opcional)"}
        },"required":["desde","hasta"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default) =>
        Compactador.Lista(await consultas.TardanzasAsync(
            Args.Fecha(args, "desde"), Args.Fecha(args, "hasta"),
            Args.TextoOpcional(args, "area"), Args.EnteroOpcional(args, "empleadoId"), ct));
}

/// <summary>Licencias manuales de RRHH + licencias futuras ya programadas.</summary>
public sealed class LicenciasTool(ILicenciaManualService manuales, IConsultaAsistenteService consultas, IReloj reloj) : IAsistenteTool
{
    public string Nombre => "get_licencias";
    public string Descripcion =>
        "Licencias: las MANUALES cargadas por RRHH (con rango desde/hasta; hasta vacío = sigue vigente) y las PROGRAMADAS " +
        "(días futuros ya justificados en el sistema, de Humand o manuales). Opcionalmente de un solo empleado. " +
        "Usala para 'licencias activas', '¿quién está de licencia?', '¿cuándo vuelve X?'. " +
        "Ojo: el futuro solo existe si se sincronizó; una persona sin días futuros cargados puede igual tener licencia en Humand.";
    public string Etiqueta => "buscando licencias…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "empleadoId":{"type":"integer","description":"Limitar a un empleado (opcional)"},
          "incluirPasadas":{"type":"boolean","description":"Incluir licencias manuales ya terminadas (default false)"}
        }}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var empleadoId = Args.EnteroOpcional(args, "empleadoId");
        var incluirPasadas = Args.Booleano(args, "incluirPasadas", false);
        var hoy = reloj.Hoy;

        var todas = await manuales.ListarAsync(ct);
        var filtradas = todas
            .Where(l => empleadoId == null || l.EmpleadoId == empleadoId)
            .Where(l => incluirPasadas || l.Hasta == null || l.Hasta >= hoy)
            .ToList();

        var programadas = await consultas.LicenciasProgramadasAsync(hoy, empleadoId, ct);

        return "MANUALES (cargadas por RRHH):\n" +
               Compactador.Lista(filtradas, notaVacio: "SIN_RESULTADOS: no hay licencias manuales que cumplan el filtro.") +
               "\nPROGRAMADAS (días futuros ya justificados, hasta donde se sincronizó):\n" +
               Compactador.Lista(programadas, notaVacio: "SIN_RESULTADOS: no hay días futuros justificados sincronizados.");
    }
}

/// <summary>Planilla de liquidación (período 26→25, base 30 días).</summary>
public sealed class PresentismoTool(IPresentismoService presentismo) : IAsistenteTool
{
    public string Nombre => "get_presentismo";
    public string Descripcion =>
        "Planilla de presentismo/liquidación de un mes: días trabajados, feriados, injustificadas, días por tipo de licencia, " +
        "horas nocturnas, PPP y días liquidados por persona. El período es SIEMPRE del 26 del mes anterior al 25 (ej.: mes=8 ⇒ 26/07 al 25/08). " +
        "Usala para 'presentismo de agosto', 'días a liquidar', 'a quién se le descuenta el presentismo'.";
    public string Etiqueta => "armando la planilla de presentismo…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "anio":{"type":"integer"},
          "mes":{"type":"integer","description":"1-12; el período resultante es 26 del mes anterior al 25 de este mes"},
          "area":{"type":"string","description":"Área exacta para filtrar (opcional)"},
          "empleadoId":{"type":"integer","description":"Limitar a un empleado (opcional)"}
        },"required":["anio","mes"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var mes = Args.Entero(args, "mes");
        if (mes is < 1 or > 12) throw new ArgumentException("mes debe estar entre 1 y 12.");
        var area = Args.TextoOpcional(args, "area");
        var empleadoId = Args.EnteroOpcional(args, "empleadoId");

        var r = await presentismo.ReporteMensualAsync(Args.Entero(args, "anio"), mes, ct);
        var filas = r.Filas
            .Where(f => (area == null || f.Area == area) && (empleadoId == null || f.EmpleadoId == empleadoId))
            .ToList();
        return Compactador.Lista(filas);
    }
}

/// <summary>Horas nocturnas (banda 21-06) del período de liquidación.</summary>
public sealed class NocturnidadTool(INocturnidadService nocturnidad) : IAsistenteTool
{
    public string Nombre => "get_nocturnidad";
    public string Descripcion =>
        "Horas nocturnas (banda 21:00-06:00) del período de liquidación 26→25. Sin empleadoId: total por persona. " +
        "Con empleadoId: el detalle noche por noche. Usala para 'horas nocturnas de X', 'nocturnidad de agosto'.";
    public string Etiqueta => "calculando horas nocturnas…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "anio":{"type":"integer"},
          "mes":{"type":"integer","description":"1-12; período 26 del mes anterior al 25"},
          "empleadoId":{"type":"integer","description":"Detalle noche por noche de un empleado (opcional)"}
        },"required":["anio","mes"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var anio = Args.Entero(args, "anio");
        var mes = Args.Entero(args, "mes");
        if (mes is < 1 or > 12) throw new ArgumentException("mes debe estar entre 1 y 12.");

        if (Args.EnteroOpcional(args, "empleadoId") is int empleadoId)
            return Compactador.Lista(await nocturnidad.DetalleMensualAsync(empleadoId, anio, mes, ct));
        return Compactador.Lista(await nocturnidad.ReporteMensualAsync(anio, mes, ct));
    }
}

/// <summary>Foto de un día: conteo por estado + nombres de las excepciones.</summary>
public sealed class ResumenDiaTool(IConsultaAsistenteService consultas) : IAsistenteTool
{
    public string Nombre => "get_resumen_dia";
    public string Descripcion =>
        "Resumen de UN día: cuántos presentes/tardes/ausentes/francos/pendientes hubo y los nombres de tardes y ausentes. " +
        "Opcionalmente de un solo turno (Manana|Tarde|Noche). Usala para '¿qué pasó ayer?', '¿quiénes faltaron hoy?'.";
    public string Etiqueta => "mirando el día…";
    public string SchemaJson => """
        {"type":"object","properties":{
          "fecha":{"type":"string","description":"Fecha yyyy-MM-dd"},
          "turno":{"type":"string","enum":["Manana","Tarde","Noche"],"description":"Limitar a un turno (opcional)"}
        },"required":["fecha"]}
        """;

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        Turno? turno = null;
        if (Args.TextoOpcional(args, "turno") is string t)
        {
            if (!Enum.TryParse<Turno>(t, ignoreCase: true, out var parsed))
                throw new ArgumentException($"turno debe ser Manana|Tarde|Noche (recibí '{t}').");
            turno = parsed;
        }

        var r = await consultas.ResumenDiaAsync(Args.Fecha(args, "fecha"), turno, ct);
        if (r.PorEstado.Count == 0)
            return $"{Compactador.NotaVacio} (fecha consultada: {r.Fecha:yyyy-MM-dd})";
        return Compactador.Objeto(r);
    }
}

/// <summary>Qué períodos tienen datos. La herramienta anti "mentir por omisión".</summary>
public sealed class CoberturaDatosTool(IConsultaAsistenteService consultas) : IAsistenteTool
{
    public string Nombre => "get_cobertura_datos";
    public string Descripcion =>
        "Qué fechas tienen datos sincronizados: primera y última fecha, cantidad de días y huecos sin datos. " +
        "Llamala SIEMPRE antes de afirmar que en un período 'no hubo' ausencias/tardanzas, o cuando una consulta de rango devuelve vacío.";
    public string Etiqueta => "verificando qué períodos tienen datos…";
    public string SchemaJson => """{"type":"object","properties":{}}""";

    public async Task<string> EjecutarAsync(JsonElement args, CancellationToken ct = default)
    {
        var c = await consultas.CoberturaAsync(ct);
        if (c.PrimeraFecha is null)
            return "SIN_DATOS: la base no tiene ningún día sincronizado.";
        return Compactador.Objeto(new
        {
            c.PrimeraFecha,
            c.UltimaFecha,
            c.DiasConDatos,
            Huecos = c.Huecos.Select(h => new { h.Desde, h.Hasta }).ToList(),
        });
    }
}
