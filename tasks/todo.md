# Mega Tablero RRHH — extensión de rrhh-novedades (solo Espert)

> Diseño 2026-08-27. Decisión Davor: NO forkear tadeva-dashboard; extender
> rrhh-novedades con las secciones que le faltan, marcadas como nuevas en la
> navegación para no romper el sistema viejo. Fuente del gap: 
> `RRHH/relevamiento-mega-tablero-rrhh.md`.

## Principios

- Lo existente NO se toca: Dashboard, Bot, Presentismo, Nocturnidad, Ausentismo,
  Configuración quedan como están.
- Cada sección nueva entra en el NavMenu bajo un grupo separado **"NUEVO"** con
  ícono Material claro + chip dorado `Nuevo` — visualmente distinguible.
- Feature flag por sección en `ConfiguracionParte`/appsettings: se puede apagar
  una sección nueva sin deploy si algo sale mal.
- Regla del repo se respeta: todo cálculo nuevo con tests; DDL manual antes del
  push (o migrar a EF Migrations — pregunta abierta).
- Mismos roles Admin/RRHH por ahora (granularidad = fase posterior).

## Secciones nuevas

### F1 — sin cambios de schema (solo lectura Humand / datos ya persistidos) ✅ 2026-08-27

- [x] **/tardanzas** — `TardanzasService` + página + Excel + tests (regla de la
      racha congelada: franco/feriado no corta, día en hora sí).
- [x] **/vacaciones** — `VacacionesService` + página (tabs Saldos/Solicitudes)
      + tests. Métodos nuevos en `IHumandService`: `ObtenerSaldosTimeOffAsync`
      (`/time-off/balances`, parseo defensivo) y `ObtenerSolicitudesTimeOffAsync`
      (`/time-off/requests`, APPROVED+IN_PROGRESS). Cache 10 min, sin persistir.
      Semáforo 21/35 configurable.
- [x] NavMenu grupo "NUEVO (beta)" + chips · `FeaturesOptions`
      (Features:Tardanzas / Features:Vacaciones) · Ayuda.razor actualizado.
- [x] **/demografia** — `DemografiaService` (cálculo puro testeado) + página +
      componente `BarraDistribucion`. Sector, turno, sexo (si el campo custom
      existe en Humand), antigüedad, pirámide etaria, cumpleaños del mes y
      esquema de jefes (relationships). `EmpleadoHumand` extendido con Status,
      FechaIngreso, FechaNacimiento, JefeId, Sexo. Todo en vivo, sin persistir.
- [x] Suite completa 179/179 en verde. Verificado local en :5070 con mock.
- [ ] **PENDIENTE #1 — validar /vacaciones contra la API REAL de Humand.** El
      shape de `/time-off/balances` no está documentado en ningún lado; el parser
      es defensivo (prueba `employeeInternalId|employeeId|userId`,
      `policy.name|policyName|policyType`, `currentBalance|balance`) pero nadie
      lo confirmó contra la respuesta real. **Cómo probarlo:** levantar con la
      key real (`Humand:UseMock=false`, ya está en
      `appsettings.secrets.local.json`) y abrir /vacaciones. Si la tabla sale
      vacía, loguear el JSON crudo de `ObtenerSaldosTimeOffAsync` y ajustar los
      nombres de campo. /demografia sí se probó contra la API real (194
      empleados) — el que quedó sin probar es solo el de saldos.
- [ ] **PENDIENTE #2 — deploy.** Los tres módulos están en el repo pero NO
      deployados. El deploy es push a main → GitHub Actions, que hoy está
      **cortado por facturación de la organización** (ver HANDOFF).

**Decisiones (Davor 2026-08-27):** F1 ya · Bejerman va (credencial `datco_read`
+ conectividad Container App→VM cuando toque F3) · F2 migra EnsureCreated →
EF Migrations.

### F2 — con schema nuevo (DDL + backfill)

- [ ] **/dotacion** — ícono `Groups` — headcount activos por área/turno,
      altas y bajas reales del período, tendencia mensual.
      Cambios: el sync de empleados hoy hace upsert y nunca desactiva → marcar
      `Activo=false` cuando el empleado desaparece de Humand/queda DEACTIVATED,
      registrar `FechaAlta`/`FechaBaja` + tabla `HistorialDotacion` (foto mensual)
      para la tendencia.
- [ ] **/horas-extra** — ícono `MoreTime` — horas extra por empleado/área/mes.
      Cambios: la ingesta hoy descarta `EXTRA_HOURS` de `incidences` y no lee
      `hours.worked/scheduled` → columna(s) nuevas en `Novedades` + backfill.

### F3 — integración Bejerman (lo económico)

- [ ] **/sueldos** — ícono `Payments` — masa salarial, composición
      (remunerativo/contribuciones/retenciones), evolución, por sector, top
      costos. Fuente: vista `_DL_SJ_Liquidaciones` de SJESP2 (SQL Server VM
      espert-vm-1) con usuario read-only `datco_read`, igual que el tablero del
      CC — arreglando los mapeos que allá están rotos.
      Requiere: credencial + conectividad Container App → VM (¿peering/firewall?).
- [ ] **Ficha 360 del empleado** — modal/página drill-down desde cualquier
      tabla: datos, historial de novedades, tardanzas, licencias, saldo
      vacaciones, y (F3) historial de sueldos. Portado del modal del tablero CC.

## Orden propuesto

F1 completo → validar con RRHH → F2 → F3. Cada fase se deploya sola.

## Preguntas abiertas (responder antes de arrancar)

1. ¿F1 arranco ya? (tardanzas + vacaciones, cero riesgo para lo viejo)
2. ¿Sueldos va? Necesito credencial `datco_read` y confirmar que la Container
   App llega a la VM (hoy el que pega a SJESP2 es el CC).
3. ¿Aprovechamos F2 para migrar EnsureCreated → EF Migrations (deuda #1 del
   repo) o seguimos con DDL manual?
4. Umbrales de semáforo de vacaciones: ¿21/35 días como el CC?
