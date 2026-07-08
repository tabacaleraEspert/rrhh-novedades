using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        app.MapPost("/api/auth/login", async (HttpContext ctx, IDbContextFactory<AppDbContext> dbFactory,
            IMemoryCache cache, IConfiguration config) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim().ToLowerInvariant();
            var pin = form["pin"].ToString();

            // Rate-limiting: un PIN de 4 dígitos es fácil de fuerza bruta. Máx 5 fallos por email
            // cada 15 min. Mitigación mínima (el estándar marca los PINs simples como deuda).
            var cacheKey = $"login-fails:{email}";
            if (cache.Get<int>(cacheKey) >= 5)
                return Results.Redirect("/login?error=locked");

            using var db = await dbFactory.CreateDbContextAsync();
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == email && u.Activo);

            // PIN maestro (Key Vault: Auth--MasterPin): si coincide, entra como ese usuario (override de IT).
            var masterPin = config["Auth:MasterPin"];
            var esMaster = !string.IsNullOrEmpty(masterPin) && pin == masterPin;
            var ok = usuario is not null && (esMaster || BCrypt.Net.BCrypt.Verify(pin, usuario.PasswordHash));

            if (!ok || usuario is null)
            {
                cache.Set(cacheKey, cache.Get<int>(cacheKey) + 1, TimeSpan.FromMinutes(15));
                return Results.Redirect("/login?error=1");
            }
            cache.Remove(cacheKey);

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

        // Autologin (SSO) desde el Command Center: ticket JWT de un solo uso (ver SsoTicketService).
        // Todo fallo responde 401 genérico, sin distinguir causa. El 401 lleva body: si fuera vacío,
        // UseStatusCodePagesWithReExecute re-ejecutaría el POST contra Blazor y lo convertiría en 400.
        var noAutorizado = () => Results.Json(new { ok = false }, statusCode: StatusCodes.Status401Unauthorized);
        app.MapPost("/api/auth/sso", async (HttpContext ctx, ISsoTicketService sso, IMemoryCache cache,
            SsoLoginRequest body, CancellationToken ct) =>
        {
            // Rate-limiting por IP (la real, vía ForwardedHeaders): frena fuerza bruta de tickets.
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            var cacheKey = $"sso-fails:{ip}";
            if (cache.Get<int>(cacheKey) >= 10)
                return noAutorizado();

            var usuario = await sso.ValidarYConsumirAsync(body?.Ticket ?? string.Empty, ct);
            if (usuario is null)
            {
                cache.Set(cacheKey, cache.Get<int>(cacheKey) + 1, TimeSpan.FromMinutes(15));
                return noAutorizado();
            }
            cache.Remove(cacheKey);

            // Misma sesión que el login normal (cookie 12h con los mismos claims).
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, usuario.Nombre),
                new(ClaimTypes.Email, usuario.Email),
                new(ClaimTypes.Role, usuario.Rol),
                new("UserId", usuario.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new { ok = true });
        });

        // Landing del SSO: el Command Center manda el browser a /sso#ticket=<JWT>. El ticket viaja
        // en el fragment (nunca llega al servidor ni a logs); lo lee este JS y lo POSTea al endpoint.
        // HTML estático a propósito: el rendermode global es InteractiveServer y acá no hace falta circuito.
        app.MapGet("/sso", () => Results.Content(SsoLandingHtml, "text/html; charset=utf-8"));
    }

    private const string SsoLandingHtml = """
        <!DOCTYPE html>
        <html lang="es">
        <head><meta charset="utf-8"><title>Ingresando…</title></head>
        <body style="font-family:sans-serif;display:flex;justify-content:center;margin-top:4rem;">
        <p>Validando acceso…</p>
        <script>
        (async () => {
          const fail = () => location.replace('/login?error=sso');
          try {
            const ticket = new URLSearchParams(location.hash.slice(1)).get('ticket');
            history.replaceState(null, '', '/sso'); // saca el ticket de la barra y del historial
            if (!ticket) return fail();
            const r = await fetch('/api/auth/sso', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ ticket })
            });
            r.ok ? location.replace('/') : fail();
          } catch { fail(); }
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>Endpoints operativos para disparar manualmente la sincronización y el envío del parte (Admin y RRHH).</summary>
    private static void MapOpsEndpoints(this WebApplication app)
    {
        var ops = app.MapGroup("/api/ops").RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RRHH));

        // Alta de usuario (solo Admin). PIN por defecto 0000 si no se especifica.
        ops.MapPost("/usuarios", async (IDbContextFactory<AppDbContext> dbFactory, UsuarioNuevo body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Email))
                return Results.BadRequest(new { error = "email requerido" });
            var email = body.Email.Trim().ToLowerInvariant();
            await using var db = await dbFactory.CreateDbContextAsync();
            if (await db.Usuarios.AnyAsync(u => u.Email == email))
                return Results.Conflict(new { error = "ya existe", email });
            // DNI (opcional, para SSO): solo dígitos, único.
            var dni = string.IsNullOrWhiteSpace(body.Dni) ? null : new string(body.Dni!.Where(char.IsDigit).ToArray());
            if (dni is { Length: 0 }) dni = null;
            if (dni is not null && await db.Usuarios.AnyAsync(u => u.Dni == dni))
                return Results.Conflict(new { error = "dni ya existe", dni });
            var rol = body.Rol == Roles.Admin ? Roles.Admin : Roles.RRHH;
            db.Usuarios.Add(new Usuario
            {
                Nombre = string.IsNullOrWhiteSpace(body.Nombre) ? email : body.Nombre!.Trim(),
                Email = email,
                Rol = rol,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(string.IsNullOrWhiteSpace(body.Pin) ? "0000" : body.Pin!),
                Activo = true,
                Dni = dni
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { creado = email, rol });
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin));

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

/// <summary>Body para el alta de usuario vía API (/api/ops/usuarios).</summary>
public record UsuarioNuevo(string Email, string? Nombre, string? Rol, string? Pin, string? Dni = null);

/// <summary>Body del autologin SSO (/api/auth/sso).</summary>
public record SsoLoginRequest(string? Ticket);
