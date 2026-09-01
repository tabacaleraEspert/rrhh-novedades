# Relevamiento — Mega Tablero RRHH (2026-08-27)

Comparativo de los 3 tableros de RRHH existentes, para decidir la base del "mega tablero".
Relevado con agentes sobre los repos; detalle completo por tablero al final.

## Los tres candidatos

| | **tadeva-dashboard** (Joaquín) | **rrhh-novedades** (Espert) | **RRHH Command Center** (Bejerman) |
|---|---|---|---|
| Stack | Next.js 15 + Neon PG + Humand API | .NET 10 Blazor + PG + Humand API | Flask + pymssql → SJESP2 |
| Estado | Prod (Vercel/Azure), vivo | Prod (Container Apps), vivo, CI/CD + 139 tests | Prod dentro del CC, con 4 bugs de mapeo |
| Datos | Humand en vivo + Sheet dotación + snapshot PG | Humand en vivo + PG propio | Solo liquidaciones CERRADAS (lag 30-60 días) |
| Fuerte | Ausentismo auditable, parte diario, RBAC 4 roles + emulación de vistas, demografía, control horario supervisor | Automatización: partes por WhatsApp por turno, presentismo liquidación 26→25 con Excel, nocturnidad, licencias manuales, asistente IA con 9 tools, SSO con el CC | **Lo económico**: sueldos, costo laboral (remunerativo/contribuciones/retenciones), top costos, drill-down por empleado, saldo vacaciones Bejerman |
| Débil | Sin sueldos (código listo, Bejerman apagado), sin export, sin alertas, sin mobile | Sin dotación/altas-bajas, sin saldos de vacaciones, roles planos (todos ven todo), sin tardanzas en pantalla | Nada en vivo, sin asistencia diaria, filtro sector roto + 3 bugs más, sin export |

## Veredicto: el más completo

**tadeva-dashboard es el más completo y maduro como producto** (11 vistas, cálculos auditables, RBAC real, cache/resiliencia de 5 capas). Pero ninguno cubre todo: son **tres ejes complementarios**:

- **Operativo en vivo** (quién está hoy, ausentismo, turnos) → tadeva-dashboard
- **Automatización y liquidación** (partes WhatsApp, presentismo 26→25, nocturnidad, Excel) → rrhh-novedades
- **Económico histórico** (masa salarial, costos, composición) → tablero Bejerman del CC

## Propuesta de mega tablero (Espert)

**Base: fork/instancia Espert de tadeva-dashboard** — ya está corriendo local con marca Espert (`BRAND=espert`, puerto 3011) y la API key de Humand de Espert. Es la arquitectura más sólida y el módulo de sueldos Bejerman **ya está programado** (664 líneas), solo apagado.

Qué se le suma de cada mundo:

1. **De sí mismo**: encender `/sueldos` con `BEJERMAN_ENABLED=true` apuntando a SJESP2 con el usuario read-only `datco_read` (el mismo que usa el CC). Costo: solo env vars.
2. **Del tablero CC/Bejerman**: portar el drill-down por empleado (historial de sueldos + ausencias + vacaciones en un modal — su mejor feature), la tab Costos (composición, proyección anual) y el saldo de vacaciones desde `V_SaldoVacacionesTotal`. Arreglando de paso los 4 bugs de mapeo que tiene hoy.
3. **De rrhh-novedades**: el presentismo de liquidación 26→25 con export Excel, nocturnidad 21-06, licencias manuales, y el asistente IA con tools. El **parte por WhatsApp puede seguir viviendo en rrhh-novedades** (Twilio + scheduler ya probados en prod) o portarse después.
4. **Adaptaciones Espert pendientes de decisión**: sheet de dotación (Espert no tiene equivalente al de Sofía — o se arma uno, o se reemplaza el cruce por Bejerman/Humand), exclusiones de roster, calendario de feriados.

## Preguntas abiertas

1. ¿Mega tablero solo Espert, o multi-empresa (Espert + Tadeva con el switch BRAND)?
2. Base tadeva-dashboard = repo de Joaquín. ¿Fork propio, o coordinar con él?
3. ¿El mega tablero reemplaza al tablero Bejerman del CC y a pantallas de rrhh-novedades, o conviven?
4. ¿Habilitamos Bejerman/SJESP2 (credencial datco_read)?
5. ¿Dónde se deploya? (SWA no sirve: necesita server. Container App como rrhh-novedades, o Vercel como tadeva.)

---

## Detalle por tablero

### 1. tadeva-dashboard (Joaquín) — `RRHH/tadeva-dashboard`

**Vistas**: Parte Diario (default) · Resumen admin (dotación, demografía, antigüedad, cumpleaños, licencias próximas, span of control) · Home supervisor (control horario estilo Humand, tardes, extras) · Empleados 360° · Departamentos · Asistencia (fichajes con GPS/OpenStreetMap) · Vacaciones (solicitudes + saldos Humand) · Licencias CCT (guía del convenio con buscador) · Sueldos (APAGADO, código completo) · Auditoría de ausentismo día por día con revisión RRHH (4 estados) · Admin users + audit log.

**Joyas**: `ausentismo.ts` + `snapshot.ts` (KPI auditable, foto vs calculado, heurística de día cerrado 30%, reconstrucción histórica por ventanas de actividad) · `humand-client.ts` (5 capas: TTL, paginación paralela, coalescing, negative cache, pre-warm) · RBAC 4 roles + selector "Ver equipo de" · `turno-parser.ts` (rotativos, overnight, TZ) · cron snapshot autorreparable.

**No tiene**: sueldos activos, export, alertas/notificaciones, mobile/PWA, rotación/turnover, org chart, tests.

### 2. rrhh-novedades (Espert) — `RRHH/rrhh-novedades`

**Pantallas**: Dashboard (KPIs clickeables, tendencia 14d, alertas de reincidencia, drill-down por persona) · Bot de novedades (partes WhatsApp por turno con preview) · Empleados · Presentismo (planilla liquidación 26→25, columnas dinámicas por licencia, Excel formato RRHH) · Nocturnidad (banda 21-06, redondeo por noche, Excel 2 hojas) · Ausentismo (mes/semana/día, licencias manuales ABM) · Configuración (destinatarios, horarios, usuarios) · Uso del asistente (costos OpenAI).

**Joyas**: clasificador de jornada (orden estricto congelado en tests) · scheduler de partes con guard anti-doble-envío · re-sync retroactivo 30 días (licencias tardías) · asistente IA gpt con 9 herramientas tipadas sobre los mismos servicios de las pantallas + `get_cobertura_datos` anti-mentira · SSO one-shot con el CC (jti quemado, 16 tests) · IReloj TZ Argentina · CI/CD OIDC + smoke tests.

**No tiene**: dotación/altas-bajas (upsert nunca desactiva), saldos de vacaciones, horas extra, time tracking completo (solo primera/última fichada), roles granulares, sueldos, migraciones EF (DDL manual en prod = riesgo #1).

### 3. Tablero RRHH del Command Center — `command-center/dashboards/rrhh.html` + `rrhh.py`

**Tabs**: Sueldos (8 KPIs, evolución, por sector/categoría/concepto, nómina con search) · Dotación (headcount, altas/bajas inferidas) · Ausentismo (días liquidados por concepto, top faltadores, licencias prolongadas con semáforo) · Vacaciones (saldos con umbrales de riesgo 21/35) · Costos (proyección anual, doughnut composición, top 20) · **Modal drill-down por empleado**.

**Fuente**: vista `_DL_SJ_Liquidaciones` + `V_SaldoVacacionesTotal` de SJESP2, solo `liq_estado=3` (cerradas). 18 endpoints GET, caché in-memory 15 min.

**Bugs activos**: filtro sector roto (descrip vs código) · top-empleados campos vacíos · saldos vacaciones en cero (case de keys) · tooltip pesos etiquetado "ausencias" · fechas default hardcodeadas 2026.

**No tiene**: nada en vivo, asistencia, horas extras, legajo maestro, export, comparativos, ajuste por inflación.
