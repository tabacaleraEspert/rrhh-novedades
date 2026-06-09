# RRHHNovedades

Dashboard de RRHH (Blazor Server + MudBlazor) y bot de WhatsApp (Twilio) que envía
mensajes a partir de datos provenientes de **Humand**.

## Stack

- .NET 10 / Blazor Server (InteractiveServer)
- MudBlazor 9.4 (UI)
- EF Core 10 + SQL Server (LocalDB en dev)
- Twilio 7.14 (WhatsApp)
- BCrypt.Net (auth)

## Estructura

```
src/RRHHNovedades.Web/      → app web
  Components/                 → App, Routes, Layout, Pages, Shared
  Data/                       → AppDbContext + SeedData
  Models/                     → Usuario
  Services/                   → TwilioService, HumandService
  Extensions/                 → ServiceCollection + Endpoints
  HealthChecks/               → DbHealthCheck
  wwwroot/                    → app.css, logos (img/), favicon
tests/RRHHNovedades.Tests/   → xUnit + NSubstitute
docs/                         → brand guide + TWILIO-INTEGRACION.md
```

## Correr

```bash
cd src/RRHHNovedades.Web
dotnet run            # http://localhost:5070
```

Usuario inicial (seed): `desarrollador1@tabacaleraespert.com` / `espert` (rol Admin).
Cambiar en producción.

## Notas

- La inicialización de la BD usa `EnsureCreated` mientras no haya un esquema definitivo.
  Al definir los modelos finales, migrar a EF Migrations (`dotnet ef migrations add ...`).
- Credenciales de Twilio y Humand van en `appsettings.secrets.local.json` (gitignored)
  o en App Settings de Azure. Ver `docs/TWILIO-INTEGRACION.md`.
