# Template de Twilio — Parte de novedades

Diseño del **Content Template** (plantilla de utilidad de WhatsApp) que el bot usa para enviar los
2 partes diarios.

**Template vigente: `rrhh_envio_reporte_asistencia_v2` — SID `HX08a95a17f1d7bbee214071a9d9500e81`**
(configurado en `Twilio:ContentSidParte`). Creado y enviado a aprobación por API el 11-jun-2026.

---

## Historia / lecciones de aprobación

- **v1 (`HX11f5072e...`) fue RECHAZADO por Meta** con: *"This template has too many variables for its
  length. Reduce the number of variables or increase the message length."* Tenía 8 variables y poco
  texto fijo.
- **v2 corrige eso**: 5 variables (cantidad+nombres fusionados en una variable por categoría) y una
  línea fija al pie que mejora el ratio texto/variables.
- Reglas de Meta a respetar siempre: sin saltos de línea/tabs dentro de variables, variables nunca
  vacías (usamos `"0"`), no empezar ni terminar el cuerpo con una variable, ratio razonable
  texto fijo vs. variables, categoría **Utility**.

## Cuerpo del template v2

```
*Novedades RR. HH. — {{1}}*

🟢 Presentes: {{2}}
🟡 Tardanzas: {{3}}
🔴 Ausentes: {{4}}
🔵 Justificados: {{5}}

Reporte automático de asistencia · Tabacalera Espert
```

- **Categoría:** Utility · **Idioma:** es_AR · **Tipo:** `twilio/text`.

## Variables (las arma `ParteService.ArmarParteAsync`)

| Var | Contenido | Ejemplo |
|-----|-----------|---------|
| `{{1}}` | Turno · fecha | `Turno Mañana · 09/06/2026` |
| `{{2}}` | Presentes (número) | `12` |
| `{{3}}` | Tardanzas: cantidad (nombres) | `2 (Gómez, Rosa; Pérez, Juan)` — `0` si no hay |
| `{{4}}` | Ausentes: cantidad (nombres) | `3 (López, Carla; Ruiz, Pedro; Sosa, Mario)` |
| `{{5}}` | Justificados: cantidad (nombres) | `1 (Díaz, Lucía)` |

## Cómo se ve enviado

```
*Novedades RR. HH. — Turno Mañana · 09/06/2026*

🟢 Presentes: 12
🟡 Tardanzas: 2 (Gómez, Rosa; Pérez, Juan)
🔴 Ausentes: 3 (López, Carla; Ruiz, Pedro; Sosa, Mario)
🔵 Justificados: 1 (Díaz, Lucía)

Reporte automático de asistencia · Tabacalera Espert
```

## Gestión por API (cómo se creó la v2)

```bash
# Crear template
POST https://content.twilio.com/v1/Content
{ "friendly_name": "...", "language": "es_AR", "variables": {samples}, "types": {"twilio/text": {"body": "..."}} }

# Enviar a aprobación WhatsApp
POST https://content.twilio.com/v1/Content/{HX}/ApprovalRequests/whatsapp
{ "name": "...", "category": "UTILITY" }

# Consultar estado / motivo de rechazo
GET https://content.twilio.com/v1/Content/{HX}/ApprovalRequests
```

> El contenido de un template es **inmutable**: ante un rechazo se crea un template nuevo
> (nuevo SID) y se actualiza `Twilio:ContentSidParte`.

## Notas
- Mientras el template no esté **aprobado para "business initiated"**, los envíos proactivos del bot
  no llegan; se puede probar la entrega abriendo ventana de 24 h (el destinatario le escribe "hola"
  al número emisor) o con `Twilio:ContentSidParte` vacío (texto plano, requiere ventana abierta).
- Los partes son **outbound** a la lista de RRHH (la cuenta Twilio es la de ChatbotCobros; no se toca
  su webhook). Credenciales en secrets, nunca en el repo.
- Ref general: `docs/TWILIO-INTEGRACION.md`.
