using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.HealthChecks;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;
using RRHHNovedades.Web.Services.Asistente;
using RRHHNovedades.Web.Services.Asistente.Herramientas;

namespace RRHHNovedades.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        // Base de datos (PostgreSQL — estándar Espert para apps transaccionales)
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(config.GetConnectionString("Default")));

        // Opciones
        services.Configure<AsistenciaOptions>(config.GetSection(AsistenciaOptions.SectionName));
        services.Configure<HumandOptions>(config.GetSection(HumandOptions.SectionName));
        services.Configure<TwilioOptions>(config.GetSection(TwilioOptions.SectionName));
        services.Configure<SsoOptions>(config.GetSection(SsoOptions.SectionName));
        services.Configure<AsistenteOptions>(config.GetSection(AsistenteOptions.SectionName));

        // Integración Humand (real o simulada según Humand:UseMock)
        var useMock = config.GetValue<bool>($"{HumandOptions.SectionName}:UseMock");
        if (useMock)
            services.AddSingleton<IHumandService, MockHumandService>();
        else
            services.AddHttpClient<IHumandService, HumandService>();

        // Servicios de aplicación
        // Reloj único en hora Argentina: toda comparación/visualización de fecha-hora pasa por acá.
        services.AddSingleton<IReloj, RelojArgentino>();
        services.AddSingleton<ITwilioService, TwilioService>();
        services.AddScoped<IIngestaService, IngestaService>();
        services.AddScoped<IParteService, ParteService>();
        services.AddScoped<INocturnidadService, NocturnidadService>();
        services.AddScoped<IPresentismoService, PresentismoService>();
        services.AddScoped<IAusentismoService, AusentismoService>();
        services.AddScoped<ILicenciaManualService, LicenciaManualService>();
        services.AddScoped<ISsoTicketService, SsoTicketService>();
        services.AddMemoryCache();

        // Asistente IA de consultas (chat sobre los datos del tablero)
        services.AddScoped<IConsultaAsistenteService, ConsultaAsistenteService>();
        services.AddSingleton<PromptBuilder>();
        services.AddSingleton<IChatProveedor, OpenAIProveedor>();
        services.AddScoped<IAsistenteTool, BuscarEmpleadoTool>();
        services.AddScoped<IAsistenteTool, HistorialEmpleadoTool>();
        services.AddScoped<IAsistenteTool, AusentismoTool>();
        services.AddScoped<IAsistenteTool, TardanzasTool>();
        services.AddScoped<IAsistenteTool, LicenciasTool>();
        services.AddScoped<IAsistenteTool, PresentismoTool>();
        services.AddScoped<IAsistenteTool, NocturnidadTool>();
        services.AddScoped<IAsistenteTool, ResumenDiaTool>();
        services.AddScoped<IAsistenteTool, CoberturaDatosTool>();
        services.AddScoped<AsistenteToolRegistry>();
        services.AddScoped<IRegistroTurnosService, RegistroTurnosService>();
        services.AddScoped<AsistenteEstado>();
        services.AddScoped<AsistenteService>();

        // Bot: scheduler de los 2 partes diarios
        services.AddHostedService<ParteScheduler>();

        // Health checks
        services.AddHealthChecks()
            .AddCheck<DbHealthCheck>("database", tags: ["ready"])
            .AddCheck<ConocimientoHealthCheck>("conocimiento-asistente", tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.Cookie.HttpOnly = true;
                // Sin esto, un challenge redirige al default /Account/Login (no existe) → 404.
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
