# Segmentación "Rendicion App" en Humand

> *Fecha:* 28 de Mayo 2026  
> *Autor:* Equipo IT  
> *Estado:* Propuesta de implementación  
> *Contexto:* Integración Humand <> App de Rendición de Gastos

---

## 1. Hallazgo

Durante la exploración de la API de Humand (GET /segmentations) se detectó que ya existe un grupo de segmentación llamado *"Rendicion App"* (ID: 412093, visibilidad: solo administradores) configurado en el panel de Humand.

Este grupo fue creado manualmente desde el panel de administración de Humand y contiene los siguientes ítems:

| Ítem | ID | Usuarios asignados |
|------|----|--------------------|
| Administradores | 1619960 | 2 |
| Rendidores c/ ticket car | 1619962 | 0 |
| Rendidores s/ ticket car | 1619963 | 4 |
| Supervisores | 1619961 | 0 |

### Usuarios asignados actualmente

*Administradores:*
- RRHH Espert (rrhh@tabacaleraespert.com)
- Yanina Juarez

*Rendidores s/ ticket car:*
- Javier Fernandez (javierfernandez@tabacaleraespert.com)
- Laura Perelli (laura@tabacaleraespert.com)
- Magalí Belen Turra (magali@tabacaleraespert.com)
- Natalia Bizzozero (natybizzozero@gmail.com)

---

## 2. Cómo se obtuvo

La información se verificó en dos pasos contra la API pública de Humand:

### Paso 1 — Listar segmentaciones


GET https://api-prod.humand.co/public/api/v1/segmentations
Authorization: Basic <API_KEY>


La respuesta incluye todos los grupos de segmentación de la organización. Entre ellos aparece:

json
{
  "id": 412093,
  "name": "Rendicion App",
  "visibility": "ADMINS_ONLY",
  "items": [
    { "id": 1619960, "name": "Administradores", "usersCount": 0 },
    { "id": 1619961, "name": "Supervisores", "usersCount": 0 },
    { "id": 1619962, "name": "Rendidores c/ ticket car", "usersCount": 0 },
    { "id": 1619963, "name": "Rendidores s/ ticket car", "usersCount": 0 }
  ]
}


> *Nota:* El campo usersCount de la respuesta de /segmentations devuelve 0 para todos los ítems, pero los usuarios sí tienen la segmentación asignada (se verifica consultando /users).

### Paso 2 — Verificar en usuarios


GET https://api-prod.humand.co/public/api/v1/users?page=1&limit=50


Cada usuario que pertenece al grupo trae la segmentación en su array segmentations:

json
{
  "firstName": "Yanina",
  "lastName": "Juarez",
  "segmentations": [
    { "group": "Sector", "item": "RRHH" },
    { "group": "Jerarquía", "item": "Jefe" },
    { "group": "Rendicion App", "item": "Administradores" }
  ]
}


---

## 3. Propuesta de uso

Actualmente la app sincroniza *todos* los usuarios de Humand y determina roles/áreas a partir de las segmentaciones "Jerarquía" y "Sector". La segmentación "Rendicion App" no se utiliza.

### 3.1 Filtro de acceso a la app

Usar "Rendicion App" como *puerta de entrada*: solo los usuarios que tengan esta segmentación asignada en Humand deben sincronizarse y tener acceso a la app de gastos.

*Beneficio:* En lugar de traer los 188 empleados y activarlos manualmente, solo se sincronizan los que RRHH haya habilitado desde Humand. El alta y baja de acceso se gestiona directamente desde el panel de Humand sin intervención técnica.

### 3.2 Perfil según ítem

El ítem dentro de "Rendicion App" define el perfil del usuario en la app:

| Ítem en Humand | Comportamiento en la App |
|----------------|--------------------------|
| *Administradores* | Acceso administrativo completo (equivale a ADMIN / RRHH_MANAGER) |
| *Supervisores* | Aprueba/rechaza rendiciones de su equipo |
| *Rendidores c/ ticket car* | Carga gastos. Tiene habilitada la categoría "Combustible" con lógica de ticket car |
| *Rendidores s/ ticket car* | Carga gastos. No tiene acceso a categorías vinculadas a ticket car |

### 3.3 Convivencia con segmentaciones existentes

La segmentación "Rendicion App" *no reemplaza* a "Jerarquía" y "Sector", sino que las complementa:

- *Sector + Jerarquía* → determinan el *rol interno* (COM_MANAGER, PROD_SUPERVISOR, etc.) y el *área*
- *Rendicion App* → determina *si tiene acceso* a la app y *qué tipo de rendidor* es

---

## 4. Nuevos ítems propuestos

Se pueden crear nuevos ítems dentro del grupo "Rendicion App" desde el panel de administración de Humand para cubrir casos que hoy no están contemplados. Propuestas:

| Ítem propuesto | Justificación |
|----------------|---------------|
| *Rendidores c/ fondo fijo* | Choferes de logística que manejan fondo fijo y necesitan rendir periódicamente. Hoy existe la segmentación "Fondo fijo" por separado — se podría unificar acá |
| *Aprobadores financieros* | Alejandro Matarozzo y equipo de finanzas que dan conformidad final pero no cargan gastos |
| *Solo lectura* | Perfiles que necesitan consultar reportes y dashboards sin cargar ni aprobar gastos (ej: auditoría, dirección) |
| *Rendidores temporada* | Empleados por temporada (ya existe la segmentación "Modalidad de contratación" → "Por temporada") que rinden gastos solo durante su período de contrato |

> *Importante:* Crear nuevos ítems en Humand no requiere desarrollo. Se hace desde el panel de administración de Humand en Segmentación > Rendicion App. El desarrollo solo es necesario para que la app interprete esos nuevos ítems.

---

## 5. Otras segmentaciones relevantes detectadas

Al consultar la API se encontraron otras segmentaciones que podrían aportar contexto a la app:

| Segmentación | Ítems | Uso potencial |
|--------------|-------|---------------|
| *Fondo fijo* | Si / No | Identificar rendidores con fondo fijo asignado |
| *Rendicion Nafta* | ticket car / usuarios s/ ticket car | Segmentación legacy, posiblemente reemplazada por "Rendicion App" |
| *Turno* | Turno A - Cristian / Turno B - Derlis / Turno C Noche | Contexto para gastos de producción (beneficio comida por horas extra) |
| *Ventas* | AMBA / Interior | Región del vendedor — útil para categorizar viáticos |
| *Modalidad de contratacion* | A tiempo completo / Por temporada | Filtrar rendidores estacionales |

---

## 6. Próximos pasos

1. *Validar con Yanina* que los ítems actuales de "Rendicion App" son correctos y si faltan perfiles
2. *Definir* si se agregan los ítems propuestos en la sección 4 o se ajustan según necesidades
3. *Asignar usuarios* a los ítems correspondientes desde el panel de Humand (hoy hay 6 asignados de 188)
4. *Implementar* en la app la lectura de esta segmentación durante la sincronización

---

Verificado contra la API de Humand el 28/05/2026.