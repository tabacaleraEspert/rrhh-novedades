using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RRHHNovedades.Web.Extensions;
using RRHHNovedades.Web.Services.Asistente;

// Sonda E2E del asistente: mismos servicios y config que la app (Conocimiento/, secrets,
// DB local con datos reales), sin el circuito Blazor. No arranca hosted services (no Run()).
const string webRoot = "/Users/davorvindis/Desktop/TabacaleraEspert/RRHH/rrhh-novedades/src/RRHHNovedades.Web";

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = webRoot });
builder.Configuration.AddJsonFile(Path.Combine(webRoot, "appsettings.secrets.local.json"), optional: false);
builder.Configuration["ConnectionStrings:Default"] = "Host=localhost;Port=5455;Database=rrhhnovedades;Username=postgres;Password=postgres";
builder.Configuration["Humand:UseMock"] = "true";
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddAppServices(builder.Configuration, builder.Environment);

var app = builder.Build();

foreach (var pregunta in new[]
{
    "¿Cuántas ausencias hubo en julio y quién fue la persona que más faltó?",
    "¿Y tardanzas en julio? Nombres y minutos.",
})
{
    Console.WriteLine($"\n════ PREGUNTA: {pregunta}");
    using var scope = app.Services.CreateScope();
    var asistente = scope.ServiceProvider.GetRequiredService<AsistenteService>();
    await foreach (var e in asistente.PreguntarAsync(pregunta, usuarioId: 3, nombreUsuario: "Davor"))
    {
        switch (e)
        {
            case EventoHerramienta h: Console.WriteLine($"  [tool] {h.Etiqueta}"); break;
            case EventoTexto t: Console.Write(t.Delta); break;
            case EventoError err: Console.WriteLine($"  [ERROR] {err.Mensaje}"); break;
            case EventoFin f: Console.WriteLine($"\n  [fin] {f.DuracionMs} ms · US$ {f.CostoUsd:0.0000}"); break;
        }
    }
}
