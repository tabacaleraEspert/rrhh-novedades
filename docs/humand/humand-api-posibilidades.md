# Posibilidades con la API de Humand

> **Contexto:** Tenemos acceso a la API pública de Humand con API Key  
> **Objetivo:** Evaluar qué datos podemos sincronizar para nuestra app de gastos  
> **Fecha:** Mayo 2026

---

## 1. Resumen ejecutivo

Con la **API Key de organización** podemos hacer integraciones **server-to-server** con Humand para:

✅ **Sincronizar todos los usuarios** de la organización  
✅ **Obtener la estructura organizacional** (áreas, departamentos, segmentaciones)  
✅ **Construir el organigrama completo** con jerarquías  
✅ **Mantener los datos actualizados** mediante sincronización periódica  
✅ **Gestionar usuarios** (crear, actualizar, desactivar)

**Limitación:** La API Key **NO es para autenticación de usuarios finales**. Es para operaciones administrativas server-to-server. Para el login SSO necesitamos OAuth2 (pendiente de confirmación con Humand).

---

## 2. Endpoints disponibles con API Key

### 2.1. Gestión de Usuarios

| Endpoint | Método | Descripción | Uso para Organigrama |
|----------|--------|-------------|----------------------|
| `/users` | `GET` | Listar todos los usuarios con paginación | ✅ **Esencial** - Traer todos los empleados |
| `/users/{id}` | `GET` | Obtener detalles de un usuario específico | ✅ Datos completos del empleado |
| `/users/me` | `GET` | Perfil del usuario autenticado (con su API token) | ❌ No aplica (es para tokens personales) |
| `/users` | `POST` | Crear nuevo usuario | ⚠️ Opcional - Solo si creamos usuarios |
| `/users` | `PUT` | Upsert (crear o actualizar) usuario | ⚠️ Opcional - Sincronización bidireccional |
| `/users/{id}` | `PATCH` | Actualizar usuario parcialmente | ⚠️ Opcional - Actualizaciones |
| `/users/{id}` | `DELETE` | Eliminar usuario | ⚠️ Opcional - Limpieza |
| `/users/{id}/deactivate` | `POST` | Desactivar usuario | ✅ **Importante** - Gestión de bajas |
| `/users/{id}/reactivate` | `POST` | Reactivar usuario | ✅ Gestión de altas |

### 2.2. Estructura Organizacional

| Endpoint | Método | Descripción | Uso para Organigrama |
|----------|--------|-------------|----------------------|
| `/segmentations` | `GET` | Obtener segmentaciones (áreas/departamentos) | ✅ **Esencial** - Estructura de áreas |

---

## 3. Datos que podemos obtener de cada usuario

Según la documentación de la API de Humand, cada usuario incluye:

### Campos principales:
```json
{
  "id": "12345",
  "employeeInternalId": "EMP-001",
  "email": "juan.perez@espert.com.ar",
  "firstName": "Juan",
  "lastName": "Pérez",
  "displayName": "Juan Pérez",
  "phone": "+54 9 11 1234-5678",
  "profileImage": "https://...",
  "birthDate": "1990-05-15",
  "hireDate": "2020-01-10",
  "position": "Gerente de Ventas",
  "department": "Ventas",
  "location": "Buenos Aires",
  "status": "active",
  "managerId": "67890",  // 👈 CLAVE para jerarquías
  "segmentations": [
    {
      "id": "seg-001",
      "name": "Ventas",
      "type": "department",
      "parentId": "seg-parent"
    }
  ]
}
```

### Campos clave para el organigrama:

| Campo | Descripción | Importancia |
|-------|-------------|-------------|
| `managerId` | ID del jefe directo | ⭐⭐⭐ **Crítico** - construye la jerarquía |
| `position` | Cargo/puesto | ⭐⭐⭐ Se muestra en el organigrama |
| `department` | Departamento/área | ⭐⭐⭐ Agrupación visual |
| `segmentations` | Segmentaciones (áreas, equipos) | ⭐⭐⭐ Estructura organizacional |
| `status` | Estado (active/inactive) | ⭐⭐⭐ Filtrar usuarios activos |
| `hireDate` | Fecha de ingreso | ⭐⭐ Info adicional útil |
| `profileImage` | Foto de perfil | ⭐⭐ UX del organigrama |

---

## 4. Cómo construir el organigrama

### 4.1. Flujo de sincronización

```
1. GET /users?page=1&limit=100
   ↓
2. GET /users?page=2&limit=100
   ↓
3. ... (repetir hasta obtener todos)
   ↓
4. GET /segmentations
   ↓
5. Construir árbol jerárquico usando managerId
   ↓
6. Guardar en nuestra DB con estructura de árbol
```

### 4.2. Estructura de árbol con `managerId`

```
CEO (managerId: null)
├── CFO (managerId: CEO.id)
│   ├── Contador Senior (managerId: CFO.id)
│   └── Tesorero (managerId: CFO.id)
│
├── CTO (managerId: CEO.id)
│   ├── Tech Lead Frontend (managerId: CTO.id)
│   │   ├── Dev React (managerId: TechLead.id)
│   │   └── Dev React (managerId: TechLead.id)
│   │
│   └── Tech Lead Backend (managerId: CTO.id)
│       ├── Dev NestJS (managerId: TechLead.id)
│       └── Dev NestJS (managerId: TechLead.id)
│
└── CMO (managerId: CEO.id)
    ├── Gerente Ventas (managerId: CMO.id)
    └── Gerente Marketing (managerId: CMO.id)
```

### 4.3. Algoritmo de construcción

```typescript
// Pseudo-código
interface Employee {
  id: string;
  name: string;
  managerId: string | null;
  position: string;
  department: string;
  children?: Employee[];
}

function buildOrgChart(employees: Employee[]): Employee[] {
  const employeeMap = new Map<string, Employee>();
  const roots: Employee[] = [];

  // Crear mapa de empleados
  employees.forEach(emp => {
    employeeMap.set(emp.id, { ...emp, children: [] });
  });

  // Construir árbol
  employees.forEach(emp => {
    const employee = employeeMap.get(emp.id);
    if (emp.managerId) {
      const manager = employeeMap.get(emp.managerId);
      if (manager) {
        manager.children.push(employee);
      }
    } else {
      // Sin manager = raíz (CEO, Directores)
      roots.push(employee);
    }
  });

  return roots;
}
```

---

## 5. Modelo de datos sugerido para nuestra DB

### Tabla `employees`

```sql
CREATE TABLE employees (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  
  -- Datos de Humand
  humand_id VARCHAR(255) UNIQUE NOT NULL,
  employee_internal_id VARCHAR(100),
  
  -- Datos personales
  email VARCHAR(255) UNIQUE NOT NULL,
  first_name VARCHAR(100) NOT NULL,
  last_name VARCHAR(100) NOT NULL,
  display_name VARCHAR(200),
  phone VARCHAR(50),
  profile_image_url TEXT,
  birth_date DATE,
  
  -- Datos organizacionales
  position VARCHAR(200),
  department VARCHAR(200),
  location VARCHAR(200),
  hire_date DATE,
  
  -- Jerarquía
  manager_id UUID REFERENCES employees(id) ON DELETE SET NULL,
  
  -- Estado
  status VARCHAR(20) DEFAULT 'active', -- active, inactive
  
  -- Segmentaciones (JSON)
  segmentations JSONB,
  
  -- Metadata
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW(),
  last_synced_at TIMESTAMP,
  
  -- Índices
  INDEX idx_manager_id (manager_id),
  INDEX idx_status (status),
  INDEX idx_department (department),
  INDEX idx_humand_id (humand_id)
);
```

### Tabla `departments` (opcional, depende de segmentations)

```sql
CREATE TABLE departments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  humand_id VARCHAR(255) UNIQUE NOT NULL,
  name VARCHAR(200) NOT NULL,
  type VARCHAR(50), -- department, team, area
  parent_id UUID REFERENCES departments(id) ON DELETE SET NULL,
  description TEXT,
  created_at TIMESTAMP DEFAULT NOW(),
  updated_at TIMESTAMP DEFAULT NOW()
);
```

---

## 6. Casos de uso concretos

### 6.1. ✅ Construir organigrama completo

**Endpoint:** `GET /users?limit=100&page=1`

**Proceso:**
1. Obtener todos los usuarios paginados
2. Construir árbol jerárquico usando `managerId`
3. Renderizar componente de organigrama visual

**Beneficio:** Vista completa de la estructura de la empresa

---

### 6.2. ✅ Autocompletar aprobadores de gastos

**Endpoint:** `GET /users?search=juan`

**Proceso:**
1. Usuario está cargando un gasto
2. Necesita seleccionar quién aprueba
3. Buscar en la API por nombre → sugerir jefes directos

**Beneficio:** No necesita escribir emails manualmente

---

### 6.3. ✅ Validar jerarquías de aprobación

**Lógica:**
```typescript
// Usuario carga gasto → Necesita aprobación de su jefe
const expense = { userId: 'user-123', amount: 5000 };
const user = await getEmployee('user-123');
const manager = await getEmployee(user.managerId);

// Enviar notificación al jefe
await sendApprovalRequest(manager.email, expense);
```

**Beneficio:** Automatizar flujo de aprobaciones basado en jerarquía real

---

### 6.4. ✅ Sincronizar altas/bajas de personal

**Endpoints:**
- `GET /users` → Traer lista completa
- Comparar con nuestra DB → detectar nuevos / inactivos
- `POST /users/{id}/deactivate` → (opcional) marcar bajas desde nuestra app

**Beneficio:** Lista de usuarios siempre actualizada sin intervención manual

---

### 6.5. ✅ Reportes por departamento/área

**Query en nuestra DB:**
```sql
SELECT 
  department,
  COUNT(*) as total_employees,
  SUM(CASE WHEN status = 'active' THEN 1 ELSE 0 END) as active,
  AVG(total_expenses) as avg_expenses
FROM employees e
LEFT JOIN expenses ex ON ex.user_id = e.id
GROUP BY department;
```

**Beneficio:** Analytics organizacionales cruzando datos de Humand + Gastos

---

## 7. Estrategia de sincronización

### 7.1. Sincronización inicial (primera vez)

```typescript
async function initialSync() {
  console.log('🔄 Sincronizando usuarios desde Humand...');
  
  let page = 1;
  let hasMore = true;
  
  while (hasMore) {
    const response = await humandAPI.getUsers({ page, limit: 100 });
    
    for (const humandUser of response.data) {
      await upsertEmployee({
        humandId: humandUser.id,
        email: humandUser.email,
        firstName: humandUser.firstName,
        lastName: humandUser.lastName,
        position: humandUser.position,
        department: humandUser.department,
        managerId: humandUser.managerId,
        status: humandUser.status,
        // ... más campos
      });
    }
    
    hasMore = response.pagination.hasNext;
    page++;
  }
  
  console.log('✅ Sincronización completa');
}
```

### 7.2. Sincronización incremental (cron job)

```typescript
// Ejecutar cada 6 horas
@Cron('0 */6 * * *')
async function incrementalSync() {
  const lastSync = await getLastSyncTimestamp();
  
  // Humand no tiene query param "updatedSince", 
  // entonces traemos todos y comparamos
  const humandUsers = await humandAPI.getAllUsers();
  const ourUsers = await db.employees.findAll();
  
  // Detectar nuevos
  const newUsers = humandUsers.filter(hu => 
    !ourUsers.some(ou => ou.humandId === hu.id)
  );
  
  // Detectar actualizados (comparar updated_at o campos)
  const updatedUsers = humandUsers.filter(hu => {
    const ourUser = ourUsers.find(ou => ou.humandId === hu.id);
    return ourUser && isUserDataDifferent(hu, ourUser);
  });
  
  // Detectar inactivos
  const inactiveUsers = ourUsers.filter(ou => 
    !humandUsers.some(hu => hu.id === ou.humandId)
  );
  
  // Aplicar cambios
  await createEmployees(newUsers);
  await updateEmployees(updatedUsers);
  await deactivateEmployees(inactiveUsers);
  
  await saveLastSyncTimestamp(new Date());
}
```

### 7.3. Webhook (si Humand lo soporta - a confirmar)

Idealmente, Humand enviaría webhooks cuando:
- Se crea un usuario
- Se actualiza un usuario
- Se desactiva un usuario
- Cambia una jerarquía

```typescript
@Post('/webhooks/humand/user-updated')
async handleHumandWebhook(@Body() payload: HumandWebhookPayload) {
  const { event, data } = payload;
  
  switch (event) {
    case 'user.created':
      await createEmployee(data);
      break;
    case 'user.updated':
      await updateEmployee(data);
      break;
    case 'user.deactivated':
      await deactivateEmployee(data.id);
      break;
  }
}
```

**⚠️ Nota:** Hay que confirmar con Humand si soportan webhooks.

---

## 8. Limitaciones y consideraciones

### 8.1. Rate Limiting

**Límite actual:** 50 requests / 60 segundos

**Estrategia:**
- Usar paginación grande (`limit=100`) para minimizar requests
- Implementar retry con backoff exponencial
- Cachear datos (no hacer requests en cada página vista)
- Sincronizar en background (cron jobs)

```typescript
// Ejemplo de rate limit handling
async function fetchWithRetry(url: string, retries = 3) {
  try {
    return await fetch(url);
  } catch (error) {
    if (error.status === 429 && retries > 0) {
      const waitTime = Math.pow(2, 3 - retries) * 1000; // 1s, 2s, 4s
      await sleep(waitTime);
      return fetchWithRetry(url, retries - 1);
    }
    throw error;
  }
}
```

### 8.2. Autenticación

**⚠️ IMPORTANTE:** La API Key es para operaciones **server-to-server**, no para usuarios finales.

```
❌ INCORRECTO:
Usuario → Frontend → API Humand (con API Key fija)
  └─ Esto expone la API Key al cliente

✅ CORRECTO:
Usuario → Nuestro Backend → API Humand (con API Key)
  └─ La API Key nunca sale del servidor
```

### 8.3. Datos sensibles

La API devuelve datos personales (emails, teléfonos, fechas de nacimiento). Asegurar:
- Almacenar según GDPR/Ley de Protección de Datos
- Implementar RBAC (Role-Based Access Control)
- Log de accesos a datos sensibles
- Encriptación en reposo y en tránsito

---

## 9. Implementación completada ✅

**Fecha:** Mayo 2026  
**Estado:** Funcionalidad lista, pendiente API Key de Humand

### Backend implementado

**Módulos creados:**
- `apps/api/src/humand/` - Módulo de integración con Humand API
  - `humand.service.ts` - Cliente HTTP con autenticación y paginación
  - `humand.module.ts` - Módulo exportable
  - `dto/humand-user.dto.ts` - DTOs de tipos de Humand
- `apps/api/src/users/users.service.ts` - Método `syncFromHumand()`
- `apps/api/src/users/users.controller.ts` - Endpoint `POST /users/sync-humand`
- `apps/api/src/users/dto/sync-humand-result.dto.ts` - DTO de respuesta

**Características:**
- ✅ Autenticación con API Key (Basic Auth)
- ✅ Paginación automática (100 usuarios por página)
- ✅ Manejo de rate limiting (espera cada 40 páginas)
- ✅ Mapeo automático de roles basado en `position`
- ✅ Mapeo automático de áreas basado en `department`
- ✅ Usuarios creados con `isActive = false` (inactivos)
- ✅ Skip de usuarios existentes (por `humandId`)
- ✅ Logging detallado de proceso
- ✅ Manejo robusto de errores

### Frontend implementado

**Componentes creados:**
- `apps/web/src/features/users/SyncHumandResultModal.tsx` - Modal de resultados
- `apps/web/src/features/users/users.api.ts` - Método `syncFromHumand()`
- `apps/web/src/features/users/UsersPage.tsx` - Botón "Sincronizar con Humand"

**Características:**
- ✅ Botón junto a "Nuevo usuario" (solo Admin/RRHH Manager)
- ✅ Loading state durante sincronización
- ✅ Modal con estadísticas: total, creados, omitidos, errores
- ✅ Tabla de usuarios creados con detalles
- ✅ Lista de errores encontrados
- ✅ Refresco automático de lista de usuarios

### Configuración

```env
# apps/api/.env
HUMAND_API_URL=https://api-prod.humand.co/public/api/v1
HUMAND_API_KEY=<solicitar_a_humand>
```

### Uso

1. Admin ingresa a `/users`
2. Click en "Sincronizar con Humand"
3. Proceso automático:
   - Obtiene todos los usuarios de Humand
   - Filtra los que ya existen (por `humandId`)
   - Crea usuarios nuevos con estado inactivo
   - Mapea roles y áreas automáticamente
4. Muestra modal con resultados detallados

### Mapeo automático implementado

#### Roles (position → UserRole)

```typescript
"Gerente RRHH" → RRHH_MANAGER
"Asistente RRHH" → RRHH_ASSISTANT
"Finanzas" | "Contador" → FINANCE
"Tesorería" | "Tesorero" → TREASURY
"Supervisor" | "Jefe" → SUPERVISOR
Cualquier otro → EMPLOYEE
```

#### Áreas (department → AreaCode)

```typescript
"Comercial" | "Ventas" → COM
"Producción" → PROD
"Logística" → LOG
"Administración" → ADM
"RRHH" | "Recursos Humanos" → RRHH
"Marketing" → MKT
"Dirección" → DIR
"Tecnología" | "IT" | "Sistemas" → IT
Cualquier otro → ADM
```

### Estado de usuarios sincronizados

Todos los usuarios sincronizados quedan:
- ✅ `isActive = false` (inactivos)
- ✅ `accountActivated = false`
- ✅ `passwordHash = null`
- ✅ `supervisorId = null` (se sincroniza en fase 2)
- ✅ `regionId = AMBA` (default, ajustable después)

**Activación:** Cuando se implemente SSO OAuth2, al hacer login por primera vez se activarán automáticamente.

---

## 10. Plan de implementación

### Fase 1: Setup inicial (1-2 días)
- [x] Obtener API Key de Humand
- [ ] Configurar variables de entorno
- [ ] Crear servicio `HumandApiService` en NestJS
- [ ] Implementar autenticación con API Key

### Fase 2: Sincronización de usuarios (2-3 días)
- [ ] Crear modelo `Employee` en la DB
- [ ] Implementar `initialSync()` - traer todos los usuarios
- [ ] Mapear campos de Humand → nuestra estructura
- [ ] Testing con usuarios reales

### Fase 3: Construcción del organigrama (3-4 días)
- [ ] Algoritmo de construcción de árbol jerárquico
- [ ] Endpoint API `/employees/org-chart`
- [ ] Componente visual de organigrama (React)
- [ ] Filtros por departamento/área

### Fase 4: Sincronización continua (2-3 días)
- [ ] Implementar `incrementalSync()` con cron job
- [ ] Manejo de usuarios nuevos/actualizados/inactivos
- [ ] Logs y notificaciones de sincronización
- [ ] Dashboard de estado de sincronización

### Fase 5: Integración con gastos (2-3 días)
- [ ] Autocompletar aprobadores desde jerarquía
- [ ] Workflow de aprobaciones automático
- [ ] Reportes por departamento
- [ ] Validaciones de permisos según rol

---

## 10. Preguntas pendientes para Humand

1. **¿La API devuelve el campo `managerId` en la respuesta de `/users`?**
   - Esto es crítico para construir jerarquías

2. **¿Qué contiene exactamente el campo `segmentations`?**
   - ¿Es una estructura jerárquica de áreas/departamentos?
   - ¿Cómo se relaciona con `department`?

3. **¿Existe un endpoint para obtener solo los cambios desde una fecha?**
   - Ej: `GET /users?updatedSince=2026-05-01`
   - Esto optimizaría la sincronización incremental

4. **¿Soportan webhooks para notificar cambios en usuarios?**
   - Evitaría polling constante

5. **¿El rate limit de 50 req/60s aplica por API Key o por IP?**
   - ¿Podemos solicitar un aumento del límite para sincronizaciones?

6. **¿Existe un ambiente de testing/sandbox?**
   - Para probar integraciones sin afectar producción

---

## 11. Referencias

- **Humand API Docs:** https://api-prod.humand.co/public/api/v1/api-docs/
- **Análisis SSO:** `docs/humand-sso-integracion.md`
- **Manual API Humand:** `documentation/Manual Web API - Humand.pdf`

---

## 12. Conclusión

✅ **Sí, podemos construir el organigrama completo** usando:
- `GET /users` → todos los empleados
- Campo `managerId` → jerarquías
- `GET /segmentations` → estructura de áreas

✅ **Podemos sincronizar automáticamente** con cron jobs cada 6 horas

✅ **Podemos integrar jerarquías con el sistema de aprobación de gastos**

⚠️ **Necesitamos confirmar:**
- Que el campo `managerId` existe en la respuesta
- Si hay webhooks disponibles
- Posibilidad de aumentar rate limit

📋 **Próximo paso:** Solicitar a Joaquín el API Key de producción y confirmar estructura de datos con Humand.
