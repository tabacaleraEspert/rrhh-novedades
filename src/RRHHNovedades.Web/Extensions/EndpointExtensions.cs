using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Models;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using System.Security.Claims;

namespace RRHHNovedades.Web.Extensions;

public static class EndpointExtensions
{
    public static WebApplication MapAppEndpoints(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapOpsEndpoints();
        app.MapHealthEndpoints();
        return app;
    }

    private static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbFactory) =>
        {
            using var db = await dbFactory.CreateDbContextAsync();
            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString();
            var password = form["password"].ToString();

            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email && u.Activo);
            if (usuario is null || !BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
                return Results.Redirect("/login?error=1");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, usuario.Nombre),
                new(ClaimTypes.Email, usuario.Email),
                new(ClaimTypes.Role, usuario.Rol),
                new("UserId", usuario.Id.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Redirect("/");
        });

        app.MapGet("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
    }

    /// <summary>Endpoints operativos para disparar manualmente la sincronización y el envío del parte (Admin y RRHH).</summary>
    private static void MapOpsEndpoints(this WebApplication app)
    {
        var ops = app.MapGroup("/api/ops").RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RRHH));

        ops.MapPost("/sync", async (IIngestaService ingesta, IReloj reloj, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? reloj.Hoy;
            var emp = await ingesta.SincronizarEmpleadosAsync(ct);
            var nov = await ingesta.SincronizarDiaAsync(f, ct);
            return Results.Ok(new { empleados = emp, novedades = nov, fecha = f });
        });

        // Backfill: sincroniza un rango de fechas (máx. 31 días) para histórico/tendencia.
        ops.MapPost("/sync-rango", async (IIngestaService ingesta, DateOnly desde, DateOnly hasta, CancellationToken ct) =>
        {
            if (hasta < desde) return Results.BadRequest(new { error = "hasta < desde" });
            if (hasta.DayNumber - desde.DayNumber > 31) return Results.BadRequest(new { error = "máximo 31 días por corrida" });

            await ingesta.SincronizarEmpleadosAsync(ct);
            var porDia = new Dictionary<string, int>();
            for (var f = desde; f <= hasta; f = f.AddDays(1))
                porDia[f.ToString("yyyy-MM-dd")] = await ingesta.SincronizarDiaAsync(f, ct);

            return Results.Ok(new { desde, hasta, dias = porDia.Count, novedadesPorDia = porDia });
        });

        ops.MapPost("/parte", async (IParteService parte, IReloj reloj, Turno turno, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? reloj.Hoy;
            var r = await parte.EnviarParteAsync(f, turno, ct);
            return Results.Ok(new { r.Enviados, r.Fallidos, contenido = r.Contenido.Completo });
        });

        // Resumen por estado (sin datos personales) para validar la clasificación.
        ops.MapGet("/resumen", async (IDbContextFactory<AppDbContext> dbFactory, IReloj reloj, DateOnly? fecha, CancellationToken ct) =>
        {
            var d = fecha ?? reloj.Hoy;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var porEstado = await db.Novedades.Where(n => n.Fecha == d)
                .GroupBy(n => n.Estado)
                .Select(g => new { Estado = g.Key, Count = g.Count() })
                .ToListAsync(ct);
            var conMotivo = await db.Novedades.CountAsync(n => n.Fecha == d && n.MotivoNovedad != null, ct);
            var porTurno = await db.Novedades.Where(n => n.Fecha == d)
                .GroupBy(n => n.Turno).Select(g => new { Turno = g.Key, Count = g.Count() }).ToListAsync(ct);
            return Results.Ok(new
            {
                fecha = d,
                total = porEstado.Sum(x => x.Count),
                porEstado = porEstado.ToDictionary(x => x.Estado.ToString(), x => x.Count),
                porTurno = porTurno.ToDictionary(x => x.Turno.ToString(), x => x.Count),
                conMotivoPermiso = conMotivo
            });
        });

        ops.MapGet("/parte/preview", async (IParteService parte, IReloj reloj, Turno turno, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? reloj.Hoy;
            var c = await parte.ArmarParteAsync(f, turno, ct);
            return Results.Text(c.Completo);
        });

        // Envío de prueba a UN número puntual (no toca la lista de destinatarios).
        ops.MapPost("/parte/test", async (
            IParteService parte, ITwilioService twilio, IOptions<TwilioOptions> twOpt, IReloj reloj,
            string to, Turno turno, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? reloj.Hoy;
            var c = await parte.ArmarParteAsync(f, turno, ct);
            var tw = twOpt.Value;
            var usaTemplate = !string.IsNullOrWhiteSpace(tw.ContentSidParte);
            var r = usaTemplate
                ? await twilio.EnviarTemplateAsync(to, tw.ContentSidParte, c.Variables, ct)
                : await twilio.EnviarMensajeAsync(to, c.Completo, ct);
            return Results.Ok(new { to, modo = usaTemplate ? "template" : "texto", r.Exito, r.MessageSid, r.Error, contenido = c.Completo });
        });
    }

    private static void MapHealthEndpoints(this WebApplication app)
    {
        // Liveness: ¿el proceso está vivo? No toca dependencias (no chequea DB). Para que el
        // orquestador no reinicie el contenedor por una caída transitoria de la base.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false, // sin checks: responde Healthy si el host respondió
            ResponseWriter = EscribirReporte
        });

        // Readiness: ¿está listo para recibir tráfico? Chequea la DB (checks con tag "ready").
        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = EscribirReporte
        });
    }

    private static async Task EscribirReporte(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
}
