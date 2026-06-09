using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RRHHNovedades.Web.Data;
using RRHHNovedades.Web.HealthChecks;
using RRHHNovedades.Web.Options;
using RRHHNovedades.Web.Services;

namespace RRHHNovedades.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        // Base de datos
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlServer(config.GetConnectionString("Default")));

        // Opciones
        services.Configure<AsistenciaOptions>(config.GetSection(AsistenciaOptions.SectionName));
        services.Configure<HumandOptions>(config.GetSection(HumandOptions.SectionName));
        services.Configure<TwilioOptions>(config.GetSection(TwilioOptions.SectionName));

        // Integración Humand (real o simulada según Humand:UseMock)
        var useMock = config.GetValue<bool>($"{HumandOptions.SectionName}:UseMock");
        if (useMock)
            services.AddSingleton<IHumandService, MockHumandService>();
        else
            services.AddHttpClient<IHumandService, HumandService>();

        // Servicios de aplicación
        services.AddSingleton<ITwilioService, TwilioService>();
        services.AddScoped<IIngestaService, IngestaService>();
        services.AddScoped<IParteService, ParteService>();
        services.AddMemoryCache();

        // Bot: scheduler de los 2 partes diarios
        services.AddHostedService<ParteScheduler>();

        // Health checks
        services.AddHealthChecks()
            .AddCheck<DbHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.Cookie.HttpOnly = true;
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        return services;
    }
}
