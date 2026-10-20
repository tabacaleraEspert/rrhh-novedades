# Plan 0002 — Horas extras y refrigerio

## Datos
- `NovedadDiaria`: `HorasExtras50`, `HorasExtras100` (double, default 0). `HorasRefrigerio` ya existía.
- `JornadaHumand`: `HorasExtras50`, `HorasExtras100`.
- `HumandService.MapJornada`: clasifica cada `categorizedHours[].category.name`:
  contiene "refrigerio" ⇒ refrigerio; contiene "extra" y "100" ⇒ 100; contiene "extra" y "50" ⇒ 50.
- `IngestaService`: persiste las dos columnas nuevas.
- DDL prod (EnsureCreated no altera tablas): `infra/ddl-2026-10-20-horas-extras.sh`.
- Backfill: botón "Sincronizar período" de la página (o `/api/ops/sync-rango`).

## Servicio
`HorasExtrasService` (`IHorasExtrasService`), mismo contrato que Nocturnidad:
`ReporteMensualAsync(anio, mes)`, `DetalleMensualAsync(empleadoId, anio, mes, incluirDiasEnCero)`,
`ExcelMensualAsync(anio, mes, area)`. Período = `NocturnidadService.PeriodoLiquidacion`.

## UI
- `Components/Pages/HorasExtras.razor` (`/horas-extras`), grupo "Nuevo" del menú, flag `Features:HorasExtras`.
- Tarjetas: HE 50, HE 100, Refrigerio (hs), Empleados. Tabla + diálogo de detalle con "Mostrar días en 0".

## Asistente
- `HorasExtrasTool` (`get_horas_extras`) + sección en `Conocimiento/datos.md`.

## Manual
- `Ayuda.razor`: ítem en "¿Qué es este sistema?" + panel "Horas extras y refrigerio (sección nueva)". `UltimaActualizacion` = 20/10/2026.

## Tests
- `HorasExtrasServiceTests`: período, acumulado, detalle con ceros, Excel.
- `HumandServiceTests`: parseo de las 4 categorías reales.
- Smoke: `/horas-extras` responde 200.
