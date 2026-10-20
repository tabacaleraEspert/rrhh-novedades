# Spec 0002 — Horas extras y refrigerio

**Fecha:** 20-oct-2026 · **Pedido por:** Davor · **Estado:** implementado (pendiente DDL + deploy)

## Qué
Una sección nueva del tablero, **"Horas extras y refrigerio"**, con el mismo formato que Nocturnidad:
acumulado del **período de liquidación (26 → 25)** por empleado, filtro por área, buscador,
detalle día por día al hacer clic, botón "Sincronizar período" y export a Excel (Resumen + Detalle).

Por empleado se ve: días con horas extra, **horas extra al 50 %**, **horas extra al 100 %**, total,
**horas de refrigerio** y días con refrigerio.

## Fuente de los datos (políticas de Humand y fichador)
Las horas NO se calculan acá: las calcula **Humand** con sus **políticas de marcaje** sobre las
fichadas del kiosco de Planta, y las entrega por día en `categorizedHours` del day-summary.
Relevado contra la API real el 20-oct-2026 (período 26/09 → 20/10, 206 empleados, 5.150 jornadas):

| Política (categoría) | Jornadas | Cómo se comporta (observado) |
|---|---|---|
| Horas extras al 50 | 722 | Horas trabajadas por encima del horario programado, de lunes a viernes y los sábados hasta las 16:00. |
| Horas extras al 100 | 81 | Solo sábados, a partir de las 16:00 (no hubo domingos ni feriados en la muestra). |
| Refrigerio | 639 | Lun–vie: las horas extra que **superan las 3** (ej. 3,89 h extra ⇒ 0,89 de refrigerio; 2,1 h extra ⇒ 0). Sábados sin horario: cuenta **toda la jornada** (ej. 6,13 h trabajadas ⇒ 6,14 de refrigerio). |
| Horas nocturnas | 635 | Humand también las calcula; la sección Nocturnidad sigue usando el cálculo propio (banda 21–06). |

La incidencia `EXTRA_HOURS` **no viene poblada** en la operación real (0 casos); la fuente son las
categorías.

## Por qué
RRHH liquida horas extra (50 %/100 %) y el beneficio de comida/refrigerio por horas extra. Hoy el
único lugar donde se veía algo era la columna "Refrigerio" de Presentismo; las extras no se guardaban.

## Fuera de alcance
- Reglas propias de cálculo o redondeo (se muestra lo que Humand calcula, con decimales).
- Modificar las políticas en Humand.
- Cargar las horas en Sueldos automáticamente (los supervisores siguen cargando desde su app).

## Criterios de aceptación
- La ingesta guarda por día HE 50, HE 100 y refrigerio; un re-sync del período rellena meses viejos.
- Página `/horas-extras` (grupo "Nuevo", apagable con `Features:HorasExtras`).
- Asistente IA puede responder "horas extra de X en octubre".
- Ayuda actualizada. Tests + smoke. DDL en prod antes del deploy.

## Preguntas abiertas para RRHH
1. El refrigerio de los sábados (toda la jornada) ¿es la política deseada o está mal configurada en Humand?
2. ¿Redondear a 0,5 h como cargan los supervisores en Sueldos, o dejar decimales?
