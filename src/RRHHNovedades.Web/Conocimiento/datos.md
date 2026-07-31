# Reglas de oro de los datos

Estas reglas evitan números equivocados. Cualquier respuesta que las contradiga está mal.

## Estados de una jornada

Cada persona tiene EXACTAMENTE un estado por día calendario:

| Estado | Significa |
|---|---|
| Presente | Fichó en hora (o es no-fichador en día hábil) |
| Tarde | Fichó pasada la tolerancia; tiene minutos de tardanza |
| AusenteInjustificado | No fichó y no hay permiso ni licencia que cubra el día |
| AusenteJustificado | No fichó pero hay permiso de Humand o licencia manual (el motivo dice cuál) |
| FrancoNoLaborable | No le correspondía trabajar ese día |
| Pendiente | El turno todavía no empezó (día futuro u hoy temprano). NO es una ausencia. |

Precedencia de la clasificación (la aplica el sistema, vos solo la explicás): permiso que cubre el día y no fichó → Justificado (aun si Humand le quitó el horario, caso vacaciones); sin horario/no laborable → Franco; incidencia de ausencia de Humand sin permiso → Injustificado; fichó tarde → Tarde; fichó → Presente (si vino, vino, aunque tuviera permiso); día aún no evaluable → Pendiente.

## Ausencias y feriados

- **"Ausencia" en reportes = AusenteJustificado o AusenteInjustificado, NUNCA en feriado.** El feriado tiene precedencia sobre la licencia: un día feriado no cuenta como ausencia aunque la persona tuviera licencia.
- La **tasa de ausentismo** = ausencias / jornadas esperadas (presentes + tardes + pendientes + ausencias del día; excluye francos y feriados).
- Los feriados salen de Humand o de la configuración de la app (cargados hasta 2026). Para fechas posteriores al último feriado configurado, aclarar que puede faltar algún feriado.

## Licencias

- **Dos fuentes**: permisos de Humand (se ven día por día como AusenteJustificado con su motivo) y licencias manuales de RRHH (tienen rango desde/hasta explícito; "hasta" vacío = sigue vigente).
- **Los permisos de Humand NO tienen rango guardado**: si te piden "cuándo vuelve", solo podés inferirlo por los días consecutivos ya sincronizados. Si el futuro no se sincronizó, no se sabe: decilo.
- Una **licencia futura ("programada")** es un día justificado con fecha posterior a hoy: ya está cargada en Humand o como licencia manual.
- El **motivo puede traer varios tipos separados por coma** ("Vacaciones, Lic. por enfermedad"); los reportes de presentismo/ausentismo cuentan solo el primero.
- Licencia **"sin goce"** (el motivo lo dice) descuenta días liquidados; las demás se pagan.

## Liquidación (presentismo)

- El "mes" de liquidación va del **26 del mes anterior al 25 inclusive** (también para nocturnidad). "Presentismo de agosto" = 26/07 al 25/08.
- La base del mes es SIEMPRE **30 días**: trabajados = 30 − feriados − ausencias; liquidados = 30 − injustificadas − sin goce.
- **PPP** = "DESCONTAR" si hay al menos 1 injustificada en el período; si no "Si".
- El reporte de **ausentismo** en cambio usa rango calendario libre y semanas lunes-domingo recortadas al rango.

## Nocturnidad

- Horas nocturnas: banda **21:00–06:00** (cruza medianoche), redondeo: ≥45 minutos cuenta hora completa.

## Cobertura de datos (lo más importante)

- Hay **una fila por persona por día**, solo para los días que se sincronizaron. La sincronización automática cubre solo el día corriente; **el histórico se carga a mano por rangos**.
- Por eso: **un período sin datos NO significa que no hubo ausencias**. Antes de afirmar "no hubo X" o dar totales de un rango, verificá la cobertura (herramienta de cobertura) y si hay huecos, decilo: "entre tal y tal fecha no hay datos cargados".
- Los días futuros solo existen si alguien sincronizó el futuro; muestran las licencias ya programadas y el resto queda Pendiente.
