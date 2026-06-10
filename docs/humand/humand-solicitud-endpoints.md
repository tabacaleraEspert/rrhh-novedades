# Humand Web API — Estado de integración y solicitud de endpoints

> **Empresa:** Tabacalera Espert S.A.  
> **Documento preparado por:** Área de Sistemas  
> **Fecha:** Junio 2026  
> **Referencia API:** Manual Web API V5 — `https://api-prod.humand.co/public/api/v1`

---

## 1. Contexto del proyecto

Tabacalera Espert está desarrollando un **sistema de liquidación de sueldos** que tiene como objetivo automatizar el circuito completo:

```
Humand (RRHH + novedades del período)
        ↓  sincronización automática vía API
Liquidador de Sueldos (sistema propio)
        ↓  cálculo del período (básico, antigüedad, presentismo, HE, aportes)
        ↓  generación de recibos PDF
        ↓  publicación de recibos en Humand
Bejerman (ERP de liquidación)
        ↓  exportación en formato TXT
```

Para que esta automatización sea posible, necesitamos que **Humand nos provea acceso de lectura a las novedades del período**: principalmente registros de asistencia (horas trabajadas, horas extras) y, si estuvieran disponibles, faltas injustificadas.

---

## 2. Endpoints verificados — lo que ya funciona

Las siguientes pruebas se realizaron en **junio de 2026** contra el ambiente de producción con la API Key vigente.

### 2.1 Empleados y segmentaciones ✅

| Endpoint | Método | Resultado |
|----------|--------|-----------|
| `/users` | GET | ✅ 200 — 191 empleados activos con DNI, legajo, fecha ingreso, sector, turno, obra social |
| `/users/{employeeInternalId}` | GET | ✅ 200 — detalle completo de un empleado por DNI |
| `/segmentations` | GET | ✅ 200 — catálogo completo de grupos y valores |

### 2.2 Vacaciones y permisos ✅

| Endpoint | Método | Resultado |
|----------|--------|-----------|
| `/time-off/requests` | GET | ✅ 200 — 97 solicitudes reales con datos completos |

**Filtros disponibles y verificados:**

```
GET /time-off/requests
  ?page=1
  &limit=100
  &states=APPROVED
  &policyTypeIds=30371,30367,30368
  &fromDate=2026-05-26
  &toDate=2026-06-25
```

**Tipos de política activos en producción de Espert:**

| policyTypeId | Nombre | Uso para liquidación |
|-------------|--------|---------------------|
| 30371 | Vacaciones | Novedad `LICENCIA_VACACIONES` |
| 30367 | Lic. por enfermedad | Novedad `LICENCIA_ENFERMEDAD` |
| 30368 | Días de estudio | Novedad `FALTA_JUSTIFICADA_CON_GOCE` |
| 183319 | Salidas anticipadas | Novedad `FALTA_JUSTIFICADA_CON_GOCE` |

Este endpoint **cubre las novedades de licencias** del período de forma automatizable. La respuesta incluye: DNI del empleado (`creator.employeeInternalId`), fecha desde/hasta, cantidad de días y estado de aprobación.

### 2.3 Documentos personales (recibos) ✅ documentado

| Endpoint | Método | Resultado |
|----------|--------|-----------|
| `/users/{employeeInternalId}/documents/files` | POST | ✅ Documentado en Manual V5 — pendiente de prueba en producción |

Permite publicar recibos PDF directamente en el perfil del empleado dentro de Humand, con soporte de firma digital. Este endpoint es clave para cerrar el circuito: liquidar → generar recibo → publicar en Humand.

### 2.4 Control de Asistencia — solo escritura ⚠️

| Endpoint | Método | Resultado |
|----------|--------|-----------|
| `POST /time-tracking/entries/clockIn` | POST | ✅ Documentado — registra inicio de jornada |
| `POST /time-tracking/entries/clockOut` | POST | ✅ Documentado — registra fin de jornada |
| `GET /time-tracking/entries` | GET | ❌ 400 Bad Request — no existe o no está expuesto |

Los endpoints de Time Tracking son exclusivamente de **escritura** (envío de fichajes desde terminales hacia Humand). No existe un endpoint de **lectura** de registros de asistencia.

---

## 3. Lo que necesitamos y no está disponible

### 3.1 Registros de asistencia (Time Tracking) — lectura

**Es el endpoint más crítico para la automatización.**

Para liquidar sueldos necesitamos saber, por cada empleado y período:

| Dato | Para calcular |
|------|--------------|
| Horas trabajadas por día | Detectar días no trabajados, calcular horas extras |
| Hora de entrada y salida | Verificar cumplimiento del turno |
| Días con presencia efectiva | Presentismo (aplica según convenio) |
| Horas extras (cantidad y tipo) | Recargo del 50% o 100% según CCT 410/05 |

**Endpoint que necesitamos:**

```
GET /time-tracking/entries
  ?employeeId={id}           (o employeeInternalId={dni})
  &fromDate=2026-05-26
  &toDate=2026-06-25
  &page=1
  &limit=500
```

**Respuesta mínima esperada:**

```json
{
  "count": 22,
  "items": [
    {
      "employeeInternalId": "33405198",
      "date": "2026-05-26",
      "clockIn": "2026-05-26T07:58:00.000-03:00",
      "clockOut": "2026-05-26T17:02:00.000-03:00",
      "hoursWorked": 9.07,
      "overtime": 1.07,
      "type": "REGULAR"
    }
  ]
}
```

**Alternativa aceptable:** un endpoint de resumen por empleado y período que devuelva días trabajados y horas extras totales, sin necesidad del detalle minuto a minuto.

### 3.2 Faltas injustificadas

Actualmente no hay forma de obtener, vía API, los días en que un empleado no concurrió sin justificación. Esta información es necesaria para:

- Descontar el día del salario (`salario ÷ 30 × días de falta`)
- Determinar si el empleado **pierde el presentismo** del período (CCT 410/05: una falta injustificada anula el adicional)

**Endpoint que necesitamos:**

```
GET /absences
  ?employeeInternalId={dni}
  &fromDate=2026-05-26
  &toDate=2026-06-25
  &type=UNJUSTIFIED
```

O bien, que el endpoint `/time-off/requests` incluya un tipo de política para **faltas injustificadas** si ese concepto ya existe en el módulo de licencias de Humand.

---

## 4. Impacto en la automatización

| Escenario | Sin los endpoints | Con los endpoints |
|-----------|------------------|------------------|
| Vacaciones y licencias médicas | ✅ Ya automático | — |
| Presentismo | ❌ Manual — el liquidador revisa caso a caso | ✅ Automático |
| Horas extras | ❌ Manual — carga en sistema propio | ✅ Automático |
| Faltas injustificadas | ❌ Manual — carga en sistema propio | ✅ Automático |
| % de automatización del período | ~30% | ~85% |

---

## 5. Preguntas específicas para el equipo de Humand

1. **¿Existe un endpoint GET para leer registros de Time Tracking?** Si existe pero no está habilitado para nuestra API Key, ¿pueden habilitarlo?

2. **¿Los registros de fichaje (clock-in/clock-out) que llegan a Humand son accesibles por API para lectura?** Nuestro objetivo es consultarlos por empleado y rango de fechas para el período de liquidación.

3. **¿Hay algún reporte o exportación de asistencia disponible vía API?** Por ejemplo, un endpoint de resumen que devuelva horas trabajadas y extras por empleado por mes.

4. **¿Las faltas injustificadas se registran como un tipo de licencia en el módulo de Time Off?** Si es así, ¿bajo qué `policyTypeId`? Podríamos consultarlas con el mismo endpoint de `/time-off/requests` que ya funciona.

5. **¿Está disponible el endpoint `POST /users/{employeeInternalId}/documents/files` en producción?** Necesitamos confirmar el `folderId` de la carpeta donde deben publicarse los recibos de sueldos y si las coordenadas de firma son necesarias.

---

## 6. Alternativa si no se puede abrir el endpoint

Si Humand no puede exponer un endpoint de lectura de asistencia, la alternativa es utilizar el **archivo TXT que Humand genera desde su interfaz web** (el mismo que actualmente se usa para importar novedades en Bejerman).

En ese caso necesitaríamos:
- Un **ejemplo del TXT real** generado desde Humand para un período cerrado
- Confirmar si el formato es estable y exportable programáticamente (vía script o descarga automática)
- Documentación del formato de columnas y delimitadores

---

## Referencias

- Manual Web API V5 — proporcionado por Humand (noviembre 2025)
- Pruebas realizadas en producción: junio 2026
- API Key de producción: disponible en entorno seguro del proyecto
- Base URL: `https://api-prod.humand.co/public/api/v1`
