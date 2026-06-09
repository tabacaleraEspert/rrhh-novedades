# CLAUDE.md — RRHHNovedades

## Proyecto
Dashboard de RRHH (Blazor Server, .NET 10) + bot de WhatsApp (Twilio) que envía mensajes
a partir de datos de **Humand**. Estética compartida con `ChatbotCobros` (marca ESPERT).

## Comandos Principales

```bash
# Build
cd src/RRHHNovedades.Web && dotnet build

# Run (puerto 5070)
cd src/RRHHNovedades.Web && dotnet run

# Tests (xUnit + NSubstitute)
dotnet test tests/RRHHNovedades.Tests/

# Migraciones EF (cuando se defina el esquema definitivo)
cd src/RRHHNovedades.Web && dotnet ef migrations add <Nombre>
cd src/RRHHNovedades.Web && dotnet ef database update
```

## Stack & Dependencias Clave
- MudBlazor 9.4.0 (UI components)
- Twilio 7.14.9 (WhatsApp bot)
- BCrypt.Net-Next 4.2.0 (auth)
- EF Core SqlServer 10.0.8

## Arquitectura
```
src/RRHHNovedades.Web/
├── Components/Pages/     → Home (dashboard), Mensajes (panel del bot),
│                           Empleados, Configuracion, Login, Error, NotFound
├── Components/Layout/    → MainLayout, EmptyLayout, NavMenu
├── Services/             → IHumandService (HumandService / MockHumandService),
│                           IngestaService (sync+clasificación), ParteService (arma+envía),
│                           ParteScheduler (BackgroundService 07/14h), TwilioService
├── Options/              → AsistenciaOptions, HumandOptions, TwilioOptions
├── Data/                 → AppDbContext + SeedData
├── Models/               → Usuario, Empleado, NovedadDiaria, DestinatarioParte, EnvioParte, Enums
├── Extensions/           → ServiceCollectionExtensions, EndpointExtensions
└── HealthChecks/         → DbHealthCheck

tests/RRHHNovedades.Tests/  → ParteServiceTests (formato del parte)
```

## Estado actual (9-jun-2026)
Núcleo del MVP implementado y probado end-to-end con datos mock (`Humand:UseMock=true`):
ingesta → clasificación → bot (2 partes diarios) → dashboard. Compila sin warnings, tests verdes.
Pendiente para producción: credenciales Twilio + template aprobado (`Twilio:ContentSidParte`) y
API key de Humand (`Humand:ApiKey`, `UseMock=false`). Plan vivo en `docs/PENDIENTES.md`.

## Flujo del bot (lo principal)
`ParteScheduler` dispara a las 07:00 (mañana) y 14:00 (tarde) — horarios en `Asistencia` (appsettings).
En cada disparo: `IngestaService` sincroniza empleados + jornadas del día desde Humand y clasifica
cada `NovedadDiaria` (Presente/Tarde/AusenteInjustificado/AusenteJustificado); `ParteService` arma el
parte por turno (presentes #, ausentes/justificados/tardanzas con apellido y nombre) y lo envía por
WhatsApp a los `DestinatarioParte` activos. Disparo manual: página **Bot de novedades** o `/api/ops/*`.

## Inicialización de BD
- Mientras no haya esquema definitivo se usa `EnsureCreatedAsync` en `Program.cs`.
- Al cerrar los modelos, reemplazar por `MigrateAsync()` + migraciones EF.

## Inicialización de BD
- Mientras no haya esquema definitivo se usa `EnsureCreatedAsync` en `Program.cs`.
- Al cerrar los modelos, reemplazar por `MigrateAsync()` + migraciones EF.

## Autenticación
- Cookie-based, 12h expiry, HttpOnly
- Roles: Admin, Operador
- Endpoints: POST /api/auth/login, GET /api/auth/logout
- Seed: desarrollador1@tabacaleraespert.com (Admin), pass `espert` — cambiar en prod

## Integraciones
- **Humand**: `IHumandService` — `/users` (empleados) y `/time-tracking/day-summaries` (jornada con
  `incidences` + `timeOffRequests`). `MockHumandService` para dev. Ver `docs/humand/ENDPOINTS-RELEVANTES.md`.
  Regla justificación: ausente + permiso del día ⇒ justificado; sin permiso ⇒ injustificado.
- **Twilio (WhatsApp)**: `ITwilioService` **outbound only** (reutiliza la cuenta de ChatbotCobros, no toca
  su webhook). Envía con Content Template (`Twilio:ContentSidParte`) o texto plano (fallback dev).
  Diseño del template en `docs/TEMPLATE-PARTE.md`. Ref general: `docs/TWILIO-INTEGRACION.md`.

## Convenciones de Código (heredadas de ChatbotCobros)
- Usar `IDbContextFactory<AppDbContext>` (NUNCA inyectar DbContext directo)
- Render mode: `@rendermode InteractiveServer` en páginas interactivas
- Error feedback: `@inject ISnackbar Snackbar`
- Null display: mostrar "—" (em dash, no "N/A", no vacío)
- No emojis en UI, usar MudIcon
- Servicios se registran en `Extensions/ServiceCollectionExtensions.cs`
- Endpoints se registran en `Extensions/EndpointExtensions.cs`

### UI/Brand
- Colores brand: espert-gold #A48242, espert-gold-light #C4A866, espert-gray-dark #53565A
- Hover tables: espert-gold `rgba(164, 130, 66, 0.25)`
- Font: Inter (Google Fonts)
- Guía completa: `docs/brand/BRAND-GUIDE.md`

### Git
- Conventional commits: `feat:`, `fix:`, `chore:`
- NUNCA hacer git push/commit sin aprobación explícita del usuario

## Desarrollo
- Correr: `cd src/RRHHNovedades.Web && dotnet run` (con `Humand:UseMock=true` no pega a prod ni necesita key).
- Twilio sin credenciales → modo DEV: loguea el parte en consola en vez de enviar.
- Disparar manualmente: página **Bot de novedades** o `POST /api/ops/sync` y `/api/ops/parte?turno=Manana` (Admin).

## Documentación relevante
- `docs/PENDIENTES.md` — plan vivo por etapas y estado.
- `docs/Novedades_RRHH.pdf` — spec funcional v1.0.
- `docs/humand/ENDPOINTS-RELEVANTES.md` — API Humand para este proyecto.
- `docs/TEMPLATE-PARTE.md` — diseño del template de WhatsApp del parte.
- `docs/TWILIO-INTEGRACION.md` — referencia técnica de Twilio/WhatsApp.
- `docs/brand/BRAND-GUIDE.md` — guía de imagen de marca ESPERT.
