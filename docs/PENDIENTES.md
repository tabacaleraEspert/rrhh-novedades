# PENDIENTES — Novedades RRHH

Plan de ejecución por etapas. Base funcional: `docs/Novedades_RRHH.pdf` (spec v1.0) +
**definiciones de la reunión (9-jun-2026)** abajo. Ver `CLAUDE.md`.

> **Estado (16-jun-2026):** **CADENA COMPLETA PROBADA END-TO-END** ✅. Template v2
> `HX08a95a17f1d7bbee214071a9d9500e81` aprobado por Meta. Envío real a un número de prueba confirmado
> `status=delivered` por la API de Twilio. 20 tests verdes + smoke E2E + CI (GitHub Actions).
> Dashboard completo (KPIs, filtros, tendencia 14 días, ausentismo por área, alertas, drill-down,
> export CSV), página de Ayuda, auto-sync configurable. Humand: key cargada, lectura validada.
>
> **Listo para producción salvo:** (1) **deploy a Azure** (sin esto el scheduler no corre solo);
> (2) cargar destinatarios reales en Configuración; (3) migraciones EF (reemplazar EnsureCreated);
> (4) rotar API key de Humand y AuthToken de Twilio (fueron por chat).

---

## Definiciones de la reunión (9-jun-2026) — ALCANCE REAL

> Filosofía: **algo medianamente básico e ir incorporando funcionalidades más tarde.**

**🎯 Lo principal del proyecto es el BOT que envía 2 mensajes por día.** El dashboard es secundario.

### El bot (prioridad 1)
- **2 mensajes/día**, a las **07:00** y **14:00**, horarios **parametrizables**.
- Envío **outbound únicamente** a un **listado de teléfonos** (equipo RRHH / Yanina) — configurable.
- Se manda con un **template de Twilio** (Content Template).
- **Reutiliza la cuenta de Twilio del proyecto `ChatbotCobros`** (mismas credenciales / mismo
  número WhatsApp ya aprobado). Es una app **separada, sólo envía**; **no debe afectar** a ChatbotCobros
  (no toca su webhook ni su flujo inbound).
- **Contenido de cada parte:**
  - **Presentes:** sólo el **número** (cantidad).
  - **Ausentes:** **nombre y apellido**.
  - **Justificados:** **nombre y apellido**.
  - **Tardanzas:** **nombre y apellido**.

### El dashboard (prioridad 2)
- Todas las novedades + **KPIs básicos**.
- Datos principales: **Presentes, Ausentes, Tardanzas, Justificados**.
- **Vista global del día** + **separada por turnos mañana y tarde**.
- Fichaje: hora de entrada, salida, ausencias.
- Novedades: certificados médicos, vacaciones, motivos de falta o llegada tarde.

### Fuera de alcance por ahora
- Flujo **inbound** del bot (responder el parte para ver detalle) → NO se implementa todavía.
- Histórico/cierre de período, auditoría de ediciones, escalamientos → más adelante.

---

## Pregunta de la reunión sobre Humand — RESPUESTA

> *"No saben si una persona falta pero tiene vacaciones, Humand ya lo tiene como justificado o hay que modificar la lógica."*

La data necesaria **ya viene en la API**, así que **no dependemos** de un flag propio de Humand:
`GET /time-tracking/day-summaries` devuelve por empleado/día tanto `incidences` (puede traer `ABSENT`)
como `timeOffRequests` (los permisos/licencias que **cubren ese día**). Entonces la lógica de
justificación la resolvemos nosotros con una regla simple y robusta:

- **Ausente + `timeOffRequests` no vacío para el día → Justificado** (vacaciones, certificado, etc.).
- **Ausente + sin `timeOffRequests` → Injustificado.**

Lo único a **validar con datos reales** es si, cuando hay vacaciones aprobadas, Humand igual marca
`ABSENT` en `incidences` o directamente no lo cuenta como ausencia. En cualquiera de los dos casos
nuestra regla funciona (cruzamos contra `timeOffRequests`). **Acción:** probar con un empleado real
de vacaciones y confirmar el comportamiento.

---

## Estado Humand — DESBLOQUEADO ✅ (8-jun-2026)

OpenAPI confirmado en vivo. Detalle: `docs/humand/ENDPOINTS-RELEVANTES.md` (+ `humand-openapi.json`).

- ✅ **Fichadas / estado de jornada**: `GET /time-tracking/day-summaries` — por empleado/día: horario
  teórico (`timeSlots`), fichadas (`entries`), permisos que cubren el día (`timeOffRequests`), feriados,
  horas, e **`incidences` pre-calculado** (`ABSENT`, `LATE`, `EXTRA_HOURS`, ...).
- ✅ **Permisos/licencias** (certificados, vacaciones): `GET /time-off/requests`.
- ✅ **Turnos**: `GET /shifts/calendar`, `/shifts/templates`.
- ✅ **Empleados/áreas/teléfono**: `/users`, `/segmentations`, `/departments`.
- ✅ **API Key prod + URL base**: en `datos_api.md`. Auth `Basic`. Rate limit 50/60s.
- ⚠️ **Webhook de asistencia**: NO existe → **polling por turno**.

### Seguridad (acción recomendada)
- [ ] ⚠️ `docs/humand/datos_api.md` tiene la **API Key de PRODUCCIÓN en texto plano**. Mover a
  `appsettings.secrets.local.json` / App Settings de Azure y **rotarla** (`POST /api-keys/rotate`)
  si el repo se versiona. Considerar allowlist de IPs (`/api-keys/allowed-ips`).

---

## Pendientes de negocio (Yanina / RRHH)

No bloquean arrancar (se trabajan con defaults configurables):
- [ ] Horarios exactos de turno mañana / tarde y tolerancia/umbral de "tarde" (o si se usa el `LATE` de Humand tal cual).
- [ ] Qué tipos de licencia (`policyTypeIds`) cuentan como justificada; si `IN_PROGRESS` cuenta o sólo `APPROVED`.
- [ ] Listado definitivo de teléfonos destinatarios de los partes.
- [ ] Formato/copy exacto del template del parte (texto a aprobar en Twilio/Meta).

## Bloqueo técnico — Twilio
- [ ] **Crear y aprobar el Content Template** del parte en Twilio (Meta). Obtener su `ContentSid` (HX...).
- [ ] Conseguir las credenciales de la cuenta Twilio de ChatbotCobros (`AccountSid`, `AuthToken`, número WhatsApp)
  para esta app (van en secrets, no en el repo).
- Nota de diseño del template: los partes tienen **listas de nombres de largo variable**; con plantillas de
  WhatsApp conviene pasar el cuerpo armado como variable(s) (p. ej. `{{1}}`=encabezado turno/fecha,
  `{{2}}`=cuerpo con presentes/ausentes/justificados/tardanzas).

---

## Para poner en producción

> El MVP funciona completamente en local. Lo único que falta para que el bot envíe partes solo
> es deploarlo. **Guía paso a paso completa: [`docs/DEPLOY.md`](DEPLOY.md).**

Resumen de lo pendiente para producción:
- [ ] Deploy a **Azure App Service** (.NET 10, Windows, Always On) + **Azure SQL**.
- [ ] Cargar las 5 variables secretas en App Settings (ver DEPLOY.md §3).
- [ ] Cargar destinatarios reales en **Configuración** y probar un envío.
- [ ] **Seguridad:** rotar API Key de Humand y Auth Token de Twilio; cambiar la contraseña seed;
  eliminar `docs/humand/datos_api.md`.
- [ ] **Migraciones EF:** reemplazar `EnsureCreated` por `Migrate` cuando el esquema se estabilice.

---

## Etapas (reordenadas: el bot primero)

### Etapa 1 — Modelo de dominio mínimo + parámetros  ✅ HECHO
- [x] Enum `EstadoJornada`: Presente / Tarde / AusenteInjustificado / AusenteJustificado / FrancoNoLaborable.
- [x] Enum `Turno`: Mañana / Tarde.
- [x] Entidades: `Empleado`, `NovedadDiaria`, `DestinatarioParte`, `EnvioParte`.
- [x] Idempotencia: índice único `(EmpleadoId, Fecha)`.
- [x] Config `Asistencia`/`Humand`/`Twilio` (Options + appsettings). Destinatarios editables en Configuración.
- [ ] _(Pendiente)_ Migración EF inicial — hoy usa `EnsureCreated`; pasar a `Migrate` cuando se estabilice el esquema.

### Etapa 2 — Ingesta + clasificación desde Humand  ✅ HECHO (con mock)
- [x] `HumandService`: cliente HTTP `Authorization: Basic`, paginación, backoff 429.
- [x] `MockHumandService` (Humand:UseMock=true) con datos variados para desarrollo.
- [x] Sync empleados desde `/users`; sync jornada desde `/time-tracking/day-summaries`.
- [x] Clasificación: `incidences` (LATE/ABSENT) + cruce con `timeOffRequests` → Presente/Tarde/Just/Injust.
- [x] Persistencia idempotente y reevaluable (`IngestaService`).
- [x] ~~Validar contra la API real~~ — key cargada, datos reales verificados el 10-jun-2026 (16 tardanzas, 5 ausentes, 2 justificados correctos).

### Etapa 3 — 🎯 BOT: 2 partes diarios por WhatsApp  ✅ HECHO (envío en modo DEV)
- [x] `TwilioService` outbound, con fallback DEV (loguea si no hay credenciales).
- [x] Armado del parte por turno: presentes (#), ausentes/justificados/tardanzas (apellido, nombre).
- [x] `ParteScheduler` (`BackgroundService`) a las 07:00 y 14:00 (TZ Argentina), parametrizable.
- [x] Envío por template (`ContentSidParte`) o texto plano; registro en `EnviosParte`.
- [x] Endpoints ops (`/api/ops/sync`, `/parte`, `/parte/preview`) + panel en la página Bot.
- [x] ~~Credenciales Twilio reales~~ — cargadas en `appsettings.secrets.local.json`.
- [x] ~~Template `ContentSidParte` aprobado~~ — `HX08a95a17f1d7bbee214071a9d9500e81` confirmado `delivered` el 16-jun-2026.

### Etapa 4 — Dashboard básico  ✅ HECHO
- [x] KPI cards: Presentes, Ausentes, Tardanzas, Justificados.
- [x] Vista global del día + filtro Global / Mañana / Tarde + selector de fecha.
- [x] Tabla de personas con estado, hora entrada/salida y motivo.
- [x] Página Empleados (listado sincronizado) y Configuración (destinatarios + horarios).

### Etapa 5 — Más adelante  _(iterativo, fuera del MVP)_
- [ ] Inbound del bot (responder el parte → detalle por persona).
- [ ] Histórico + tendencia, ausentismo por área, alertas/reincidencias.
- [ ] Auditoría de ediciones, cierre de período inmutable, export para liquidación.
- [ ] Permisos por rol / privacidad de nombres.
- [ ] Inferir turno del empleado desde la segmentación "Turno" de Humand (hoy se infiere del horario del día).

---

## Buenas prácticas (transversales)
- Tolerancia y horarios = **parámetros**, nunca constantes en código.
- Todo en **TZ Argentina**.
- Ingesta **idempotente** (reproceso sin duplicar).
- El bot **no debe afectar** a ChatbotCobros (sólo outbound, sin tocar su webhook).

## Referencias
- `docs/Novedades_RRHH.pdf` — spec funcional v1.0.
- `docs/humand/ENDPOINTS-RELEVANTES.md` — API Humand para este proyecto.
- `docs/TWILIO-INTEGRACION.md` — referencia técnica Twilio/WhatsApp.
- `docs/brand/BRAND-GUIDE.md` — guía de marca ESPERT.
