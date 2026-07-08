# RRHHNovedades

Dashboard de RRHH (Blazor Server + MudBlazor) y bot de WhatsApp (Twilio) que envía
mensajes a partir de datos provenientes de **Humand**.

## Stack

- .NET 10 / Blazor Server (InteractiveServer)
- MudBlazor 9.4 (UI)
- EF Core 10 + PostgreSQL (Npgsql)
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

Necesitás un PostgreSQL local. Rápido con Docker:

```bash
docker run -d --name pg-rrhh -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16
cd src/RRHHNovedades.Web
dotnet run            # http://localhost:5070
```

La connection string default (`appsettings.json`) apunta a `localhost:5432`. Para otra DB,
override con `ConnectionStrings__Default` (env var) — formato Npgsql: `Host=...;Database=...;Username=...;Password=...`.

Usuario inicial (seed): `desarrollador1@tabacaleraespert.com` / `espert` (rol Admin).
Cambiar en producción.

## Deploy

Producción on-standard (Container Apps + PostgreSQL + Key Vault, Brazil South): ver
**`docs/DEPLOY-AZURE.md`**. La vieja `docs/DEPLOY.md` quedó deprecada.

## Notas

- La inicialización de la BD usa `EnsureCreated` mientras no haya un esquema definitivo.
  Al definir los modelos finales, migrar a EF Migrations (`dotnet ef migrations add ...`).
- Credenciales de Twilio y Humand van en `appsettings.secrets.local.json` (gitignored)
  o en Key Vault en Azure. Ver `docs/TWILIO-INTEGRACION.md`.
