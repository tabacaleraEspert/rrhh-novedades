using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>Endpoints operativos para disparar manualmente la sincronización y el envío del parte (solo Admin).</summary>
    private static void MapOpsEndpoints(this WebApplication app)
    {
        var ops = app.MapGroup("/api/ops").RequireAuthorization(p => p.RequireRole(Roles.Admin));

        ops.MapPost("/sync", async (IIngestaService ingesta, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? DateOnly.FromDateTime(DateTime.Today);
            var emp = await ingesta.SincronizarEmpleadosAsync(ct);
            var nov = await ingesta.SincronizarDiaAsync(f, ct);
            return Results.Ok(new { empleados = emp, novedades = nov, fecha = f });
        });

        ops.MapPost("/parte", async (IParteService parte, Turno turno, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? DateOnly.FromDateTime(DateTime.Today);
            var r = await parte.EnviarParteAsync(f, turno, ct);
            return Results.Ok(new { r.Enviados, r.Fallidos, contenido = r.Contenido.Completo });
        });

        ops.MapGet("/parte/preview", async (IParteService parte, Turno turno, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? DateOnly.FromDateTime(DateTime.Today);
            var c = await parte.ArmarParteAsync(f, turno, ct);
            return Results.Text(c.Completo);
        });

        // Envío de prueba a UN número puntual (no toca la lista de destinatarios).
        ops.MapPost("/parte/test", async (
            IParteService parte, ITwilioService twilio, IOptions<TwilioOptions> twOpt,
            string to, Turno turno, DateOnly? fecha, CancellationToken ct) =>
        {
            var f = fecha ?? DateOnly.FromDateTime(DateTime.Today);
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
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
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
        });
    }
}
