# Humand API — Endpoints relevantes para Novedades RRHH

Referencia curada para **este** proyecto (asistencia/novedades). Extraída del OpenAPI en vivo
(`docs/humand/humand-openapi.json`, 77 paths). Los otros `.md` de esta carpeta vienen del
proyecto de **gastos** y son referencia de la mecánica general de la API.

- **Base URL:** `https://api-prod.humand.co/public/api/v1`
- **Auth:** `Authorization: Basic {API_KEY}` (key de organización, server-to-server). Ver `datos_api.md`.
- **Rate limit:** 50 requests / 60 s. Existe allowlist de IPs por key (`/api-keys/allowed-ips`).
- **Sin `updatedSince`** general → sincronización por rango de fechas + diff.
- **Webhooks:** sólo Usuarios y Chats (no asistencia) → para novedades, **polling por turno**.
- **OpenAPI:** v1.0.0 / OpenAPI 3.0.3.

---

## ✅ Verificado en producción (jun-2026)

Probado en vivo contra la API key real:

| Endpoint | Resultado |
|---|---|
| `GET /users` | 200 (191 empleados) |
| `GET /segmentations` | 200 |
| `GET /time-off/requests` | 200 |
| `GET /time-tracking/day-summaries?employeeIds&startDate&endDate` | **200** — trae `incidences`, `timeOffRequests`, `timeSlots`, `entries`, `hours` |
| `GET /time-tracking/entries?employeeId&startDate&endDate` | **200** |
| `GET /time-tracking/entries` (sin params) | 400 |

> **Aclaración importante (vs. `docs/humand/humand-solicitud-endpoints.md`):** ese documento es de
> **otro proyecto** (liquidación de sueldos) y concluye que no hay lectura de Time Tracking porque
> `/time-tracking/entries` devolvía **400**. Eso era por **faltarle los parámetros requeridos**
> (`employeeId`, `startDate`, `endDate`) — un endpoint inexistente da **404**, no 400. Además nunca
> probaron `/time-tracking/day-summaries`. El **Manual V5 (nov-2025)** documenta Time Tracking como
> solo-escritura (clockIn/clockOut) porque la lectura se agregó después. **Para este proyecto NO hace
> falta solicitar nada a Humand: la lectura de asistencia ya funciona con la key actual.**

**`policyTypeIds` reales de Espert** (de la doc de liquidación, para mapear ausencia justificada):
`30371` Vacaciones · `30367` Lic. enfermedad · `30368` Días de estudio · `183319` Salidas anticipadas.

---

## 1. El endpoint central: `GET /time-tracking/day-summaries`

Resumen de jornada **por empleado y día**. Es la pieza que resuelve casi todo el dominio de novedades.

**Params:** `employeeIds` (req, csv), `startDate` (req, YYYY-MM-DD), `endDate` (req), `page`, `limit`.

**Respuesta** (`TimeTrackingDaySummaryPaginated.items[] = TimeTrackingDaySummary`):

| Campo | Tipo | Para qué |
|---|---|---|
| `referenceDate` | date | El día de la jornada |
| `employeeId` / `userId` | string / int | Identificación del empleado |
| `isWorkday` / `hasSchedule` | bool | ¿Corresponde trabajar? ¿Tiene turno asignado? → estado **Franco/No laborable** |
| `weekday` | enum MONDAY..SUNDAY | — |
| `timeSlots` | `[{startTime, endTime}]` | **Jornada teórica** del día (horario esperado) |
| `entries` | `[TimeTrackingEntry]` | **Fichadas reales** (START/END con hora) |
| `timeOffRequests` | `[{id, name}]` | Permisos/licencias que **cubren el día** → justifica ausencia |
| `holidays` | `[{id, name}]` | Feriados |
| `hours` | `{estimated, scheduled, timeOff, worked}` | Horas trabajadas vs esperadas |
| `categorizedHours` | `[{category, hours}]` | Desglose de horas |
| **`incidences`** | **`[enum]`** | **Pre-calculado por Humand** (ver abajo) ⭐ |

### `incidences` — el atajo clave
Humand ya clasifica el día. Enum:
`ABSENT`, `LATE`, `UNDERWORKED`, `EXTRA_HOURS`, `LOCATION_INCIDENCE`, `FACIAL_RECOGNITION_SKIPPED`, `AUTO_CLOSE`.

### Cómo representa Humand una ausencia justificada (verificado con datos reales, jun-2026)

Cuando hay un permiso aprobado que cubre el día (vacaciones, día de estudio, etc.), Humand:
- **NO marca `ABSENT`** en `incidences`;
- **quita el horario del día**: `isWorkday=false` y `hasSchedule=false`;
- **embebe el permiso** en `timeOffRequests` del propio day-summary (`[{id, name}]`).

⇒ La decisión justificado/injustificado es **nuestra**, con la regla: *permiso del día + no fichó ⇒
Justificado*. Y debe evaluarse **ANTES** que la regla de franco, porque el día con permiso parece
"sin horario" (si se chequea franco primero, los de vacaciones caen como Franco y desaparecen del parte).

Mapeo a nuestros estados de jornada (en este orden):

| Orden | Nuestro estado | Cómo se determina del day-summary |
|---|---|---|
| 1 | **Ausente justificada** | `timeOffRequests` no vacío **y** sin fichada de entrada |
| 2 | **Franco / No laborable** | `isWorkday=false` o `hasSchedule=false` (sin permiso) |
| 3 | **Ausente injustificada** | `incidences` contiene `ABSENT` (a esta altura, sin permiso) |
| 4 | **Tarde** | `incidences` contiene `LATE` (minutos: `entries` vs `timeSlots`) |
| 5 | **Presente** | tiene fichada de entrada |
| 6 | **Ausente injustificada** | laborable, sin fichada, sin permiso |

| Otros | |
|---|---|
| **Horas extra** | `incidences` contiene `EXTRA_HOURS` (cantidad en `hours.worked` − `hours.scheduled`) |

> Implicancia: **no hace falta un motor de cálculo de tardanza/ausencia desde cero**; se consume la
> clasificación de Humand (`LATE`/`ABSENT`) y la justificación se resuelve con los `timeOffRequests`
> embebidos — sin llamada extra a `/time-off/requests` (ese endpoint queda para el detalle/auditoría).

### ⚠️ Paginación de Humand — `totalPages` viene mal

En `/time-tracking/day-summaries` (verificado en prod), **`totalPages` devuelve siempre 1** y `count`
es el de la página, no el total. **Nunca** cortar por `totalPages`: paginar mientras la página venga
llena (`items.length == limit`). El `limit` máximo es **50** (con más devuelve 400).

`TimeTrackingEntry`: `{ id, userId, employeeInternalId, referenceDate, type: START|END, time: DateTime, source, site, comment }`.

---

## 2. Permisos / Licencias: `GET /time-off/requests`

Fuente directa de las **ausencias justificadas** (también vienen embebidas en el day-summary, pero acá
se ve el detalle y el estado de aprobación).

**Params (todos opcionales):** `states` (csv de `APPROVED|IN_PROGRESS|REJECTED|CANCELLED`),
`policyTypeIds`, `fromDate`, `toDate`, `resolutionFromDate/ToDate`, `createdAtSince`,
`userId`, `employeeInternalId`, `requestId`, `page`, `limit`.

`TimeOffRequestState`: **APPROVED / IN_PROGRESS / REJECTED / CANCELLED** → sólo `APPROVED` (y según
regla de negocio, quizá `IN_PROGRESS`) cuentan como justificada. **Pendiente confirmar con Yanina.**

Relacionados: `GET /time-off/balances`, `/time-off/balances/by-cycle`, `POST /time-off/policies/{id}/requests/bulk`.

---

## 3. Turnos (jornada teórica): `GET /shifts/*`

- `GET /shifts/calendar` — calendario día a día de turnos asignados por empleado. Params: `dateFrom`,
  `dateTo` (YYYY-MM-DD, default hoy), `page`, `limit`. Item = `PublicApiAssignedShift { date, employeeId, shifts:[{ name, timeSlots:[{startTime,endTime}] }] }`.
- `GET /shifts/templates` — plantillas de turno (`{ id, name, color, timeSlots }`).

> El day-summary ya incluye `timeSlots`, así que para clasificar puede alcanzar con él. Los `/shifts/*`
> sirven para mostrar/parametrizar los turnos y mapear nuestros "turno mañana / turno tarde".
> Nota: en Humand existe además una **segmentación "Turno"** (Turno A/B/C) — ver `segmentacion-roles-humand.md`.

---

## 4. Empleados y estructura

- `GET /users` — listar (búsqueda + paginación). `{ count, users: [User] }`. Campos por usuario
  (ver `humand-api-posibilidades.md`): `employeeInternalId`, `email`, `firstName/lastName`, `phone`
  (→ destino WhatsApp), `position`, `department`, `status`, `managerId`, `segmentations[]`.
- `GET /users/{employeeInternalId}` — detalle.
- `GET /segmentations` — grupos/ítems (Sector, Jerarquía, Turno, etc.). `GET /segmentations/users` — usuarios por ítem.
- `GET /departments`, `GET /job-positions` — áreas y puestos (para "ausentismo por área").

---

## 5. Diseño de ingesta sugerido (polling por turno)

```
Por cada corte de turno (p. ej. 07:30 y 14:30):
  1. (cache) lista de empleados activos del turno  ← /users + /segmentations
  2. GET /time-tracking/day-summaries?employeeIds=...&startDate=hoy&endDate=hoy   (paginado)
  3. Clasificar cada día con incidences + timeOffRequests (justif/injust)
  4. Persistir NovedadDiaria idempotente (clave: employeeInternalId + referenceDate)
  5. Re-sync posterior recalcula → injustificada puede pasar a justificada (cierre del círculo)
```

- **Idempotencia:** clave `employeeInternalId + referenceDate` (upsert, no insert).
- **Modo T-1:** si la API no responde en el corte, reintentar y/o procesar el día anterior.
- **Rate limit (50/60s):** `employeeIds` admite varios por request → batch grande; backoff en 429.
- **TZ Argentina** para todos los cortes y comparaciones horarias.

---

## Pendientes / a confirmar con Humand
- Confirmar que `incidences` (LATE/ABSENT/EXTRA_HOURS) está poblado para la operación real de Espert
  (depende de cómo configuran fichador/turnos), con datos de prueba.
- Qué `policyTypeIds` / nombres de licencia cuentan como **ausencia justificada** (def. de negocio).
- Si `states=IN_PROGRESS` debe contar como justificada provisional.
- Rate limit: ¿por key o por IP? ¿se puede subir? ¿hay sandbox? (ver `humand-api-posibilidades.md` §10).
