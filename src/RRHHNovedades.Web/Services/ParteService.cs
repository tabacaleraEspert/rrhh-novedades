using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;

namespace RRHHNovedades.Web.Services;

public record ParteContenido(
    string Encabezado,
    string Cuerpo,
    IReadOnlyDictionary<string, string> Variables)
{
    /// <summary>Texto completo para preview y envío sin template (dev / sandbox).</summary>
    public string Completo => $"{Encabezado}\n\n{Cuerpo}";
}

public record ParteEnvioResultado(int Enviados, int Fallidos, ParteContenido Contenido);

public interface IParteService
{
    Task<ParteContenido> ArmarParteAsync(DateOnly fecha, Turno turno, CancellationToken ct = default);
    Task<ParteEnvioResultado> EnviarParteAsync(DateOnly fecha, Turno turno, CancellationToken ct = default);
}

/// <summary>
/// Arma el parte de novedades por turno y lo envía al listado de destinatarios.
/// Contenido: presentes (#), ausentes / justificados / tardanzas (nombre y apellido).
/// El template de WhatsApp usa 8 variables de una sola línea (ver docs/TEMPLATE-PARTE.md).
/// </summary>
public class ParteService(
    IDbContextFactory<AppDbContext> dbFactory,
    ITwilioService twilio,
    IOptions<TwilioOptions> twilioOpt,
    ILogger<ParteService> logger) : IParteService
{
    private readonly TwilioOptions _tw = twilioOpt.Value;

    public async Task<ParteContenido> ArmarParteAsync(DateOnly fecha, Turno turno, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var novedades = await db.Novedades
            .Include(n => n.Empleado)
            .Where(n => n.Fecha == fecha && n.Turno == turno)
            .ToListAsync(ct);

        int presentes = novedades.Count(n => n.Estado == EstadoJornada.Presente);
        var tardanzas = Nombres(novedades, EstadoJornada.Tarde);
        var ausentes = Nombres(novedades, EstadoJornada.AusenteInjustificado);
        var justificados = Nombres(novedades, EstadoJornada.AusenteJustificado);

        var turnoTxt = turno == Turno.Manana ? "Turno Mañana" : "Turno Tarde";
        var tituloVar = $"{turnoTxt} · {fecha:dd/MM/yyyy}";          // variable {{1}}
        var encabezado = $"Novedades RR. HH. — {tituloVar}";

        // Variables del template (todas de una sola línea; nombres separados por "; ").
        var variables = new Dictionary<string, string>
        {
            ["1"] = tituloVar,
            ["2"] = presentes.ToString(),
            ["3"] = tardanzas.Count.ToString(),
            ["4"] = ListaTexto(tardanzas),
            ["5"] = ausentes.Count.ToString(),
            ["6"] = ListaTexto(ausentes),
            ["7"] = justificados.Count.ToString(),
            ["8"] = ListaTexto(justificados),
        };

        // Cuerpo para preview / envío sin template (refleja el template Opción 2).
        var cuerpo = new System.Text.StringBuilder();
        cuerpo.AppendLine($"🟢 Presentes: {presentes}");
        cuerpo.AppendLine($"🟡 Tardanzas ({tardanzas.Count}): {ListaTexto(tardanzas)}");
        cuerpo.AppendLine($"🔴 Ausentes ({ausentes.Count}): {ListaTexto(ausentes)}");
        cuerpo.Append($"🔵 Justificados ({justificados.Count}): {ListaTexto(justificados)}");

        return new ParteContenido(encabezado, cuerpo.ToString(), variables);
    }

    public async Task<ParteEnvioResultado> EnviarParteAsync(DateOnly fecha, Turno turno, CancellationToken ct = default)
    {
        var contenido = await ArmarParteAsync(fecha, turno, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var destinatarios = await db.Destinatarios.Where(d => d.Activo).ToListAsync(ct);
        if (destinatarios.Count == 0)
            logger.LogWarning("Parte {Turno} {Fecha}: no hay destinatarios activos", turno, fecha);

        int ok = 0, fail = 0;
        foreach (var d in destinatarios)
        {
            var r = !string.IsNullOrWhiteSpace(_tw.ContentSidParte)
                ? await twilio.EnviarTemplateAsync(d.Telefono, _tw.ContentSidParte, contenido.Variables, ct)
                : await twilio.EnviarMensajeAsync(d.Telefono, contenido.Completo, ct);

            db.EnviosParte.Add(new EnvioParte
            {
                Fecha = fecha,
                Turno = turno,
                Telefono = d.Telefono,
                MessageSid = r.MessageSid,
                Exito = r.Exito,
                Error = r.Error,
                Cuerpo = contenido.Completo,
                EnviadoUtc = DateTime.UtcNow
            });

            if (r.Exito) ok++; else fail++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Parte {Turno} {Fecha} enviado: {Ok} ok, {Fail} fallidos", turno, fecha, ok, fail);
        return new ParteEnvioResultado(ok, fail, contenido);
    }

    private static List<string> Nombres(IEnumerable<NovedadDiaria> ns, EstadoJornada estado) =>
        ns.Where(n => n.Estado == estado)
          .Select(n => n.Empleado.ApellidoNombre)
          .OrderBy(x => x)
          .ToList();

    // WhatsApp no permite variables vacías ni saltos de línea: lista en una línea, "—" si está vacía.
    private static string ListaTexto(List<string> nombres) =>
        nombres.Count == 0 ? "—" : string.Join("; ", nombres);
}
