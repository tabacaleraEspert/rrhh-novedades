using Azure.Identity;
using Microsoft.AspNetCore.HttpOverrides;
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

// Secrets locales (gitignored) si existen. Se re-agregan las variables de entorno DESPUÉS
// para que conserven la precedencia (entorno > secrets locales > appsettings); si no, el
// archivo de secretos pisa los overrides de entorno (lo usa el smoke test para forzar mock).
var secretsFile = Path.Combine(builder.Environment.ContentRootPath, "appsettings.secrets.local.json");
if (File.Exists(secretsFile))
{
    builder.Configuration.AddJsonFile(secretsFile, optional: true, reloadOnChange: false);
    builder.Configuration.AddEnvironmentVariables();
}

// Producción (Azure): los secretos viven en Key Vault y se acceden con Managed Identity
// (estándar Espert: cero secretos en app-settings). En local no se setea KeyVault:Uri, así que
// esto no corre y se usan el archivo de secrets local / variables de entorno.
// En el Vault los nombres usan "--" donde la config usa ":" (ej. ConnectionStrings--Default).
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());

// Detrás del ingress de Container Apps el TLS se termina en el borde; confiar en los headers
// X-Forwarded-* para que Request.Scheme sea https (cookie de auth + redirecciones correctas).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

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
// Primero de todo: aplicar los X-Forwarded-* del ingress (scheme/host reales).
app.UseForwardedHeaders();

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
