using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using RRHHNovedades.Web.Components;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.Extensions;
using System.Globalization;

// Cultura es-AR (formato de fechas y moneda)
var culture = new CultureInfo("es-AR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// Secrets locales (gitignored) si existen
var secretsFile = Path.Combine(builder.Environment.ContentRootPath, "appsettings.secrets.local.json");
if (File.Exists(secretsFile))
    builder.Configuration.AddJsonFile(secretsFile, optional: true, reloadOnChange: false);

// UI
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Servicios de aplicación + autenticación
builder.Services.AddAppServices(builder.Configuration, builder.Environment);
builder.Services.AddAppAuthentication();

var app = builder.Build();

// Inicialización de base de datos
// NOTA: mientras no haya migraciones EF, se usa EnsureCreated. Cuando se defina
// el esquema definitivo, reemplazar por db.Database.MigrateAsync().
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await SeedData.InitializeAsync(db);
}

// HTTP pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// API endpoints (webhook Twilio, auth, health)
app.MapAppEndpoints();

app.Run();
