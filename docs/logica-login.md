# Lógica de Negocio — Login / Autenticación

> Aplicación: **Tablero de Ventas** (Next.js App Router)
> Última actualización: 2026-06-16
> Alcance: este documento describe la lógica **tal como está implementada en este repositorio**. Para la política transversal del ecosistema (ciclo de vida de usuario, OTP, 2FA, política de contraseñas), ver [usuarios-y-roles.md](usuarios-y-roles.md).

---

## 1. Resumen

El login autentica a un usuario contra una base de datos centralizada de usuarios (`espert-auth` en Azure SQL), emite un **JWT firmado** y lo persiste en una **cookie HttpOnly** (`auth-token`). Toda navegación posterior es protegida por el `middleware`, que valida el token en cada request.

Componentes clave:

| Pieza | Archivo | Rol |
|---|---|---|
| Formulario de login | [src/app/login/page.tsx](../src/app/login/page.tsx) | UI cliente, envía credenciales |
| Endpoint de login | [src/app/api/auth/login/route.ts](../src/app/api/auth/login/route.ts) | Valida credenciales, emite token |
| Endpoint de logout | [src/app/api/auth/logout/route.ts](../src/app/api/auth/logout/route.ts) | Borra la cookie |
| Sesión actual | [src/app/api/auth/me/route.ts](../src/app/api/auth/me/route.ts) | Devuelve el usuario autenticado |
| Utilidades JWT/cookie | [src/lib/auth.ts](../src/lib/auth.ts) | Crear/verificar token, opciones de cookie |
| Acceso a BD de auth | [src/lib/auth-db.ts](../src/lib/auth-db.ts) | Pool y consultas a `espert-auth.dbo.Usuarios` |
| Protección de rutas | [src/middleware.ts](../src/middleware.ts) | Valida token, rate limit, headers de seguridad |
| Rate limiting | [src/lib/rate-limit.ts](../src/lib/rate-limit.ts) | 60 req/min por IP en `/api` |

---

## 2. Flujo de login (paso a paso)

```
[Login Page]                [POST /api/auth/login]                [espert-auth DB]
     │                                │                                  │
     │  email + password (JSON)       │                                  │
     ├───────────────────────────────►│                                  │
     │                                │  1. valida schema (Zod)          │
     │                                │  2. ¿bypass demo?  ──► sí ──► emite token demo (admin)
     │                                │  3. ¿DB configurada? ──► no ──► 503
     │                                │  4. findUsuarioByEmail ──────────►│
     │                                │◄─────────────────── usuario / null│
     │                                │  5. ¿existe e isActive?           │
     │                                │  6. bcrypt.compare(pwd, hash)     │
     │                                │  7. createToken (JWT 8h)          │
     │                                │  8. logAudit LOGIN (best-effort)  │
     │   Set-Cookie: auth-token       │                                  │
     │◄───────────────────────────────┤                                  │
     │  router.push("/dashboard")     │                                  │
     ▼                                                                    
```

### Detalle del endpoint `POST /api/auth/login`

1. **Validación de entrada (Zod):** `email` (formato email) + `password` (string no vacío). Si falla → `400 { error: "Datos inválidos", details }`.

2. **Bypass de desarrollo (demo):** si `email === "demo@espert.com"` y `password === "demo"`, se emite un token con rol `admin` sin tocar la base de datos. Pensado para entornos sin BD.
   > ⚠️ Esta credencial está hardcodeada en el código. Debe deshabilitarse/removerse antes de producción.

3. **BD configurada:** `isAuthDbConfigured()` exige `AZURE_SQL_SERVER`, `AZURE_SQL_USER`, `AZURE_SQL_PASSWORD`, `AZURE_AUTH_DATABASE`. Si falta alguna → `503 { error: "Base de datos de autenticación no configurada" }`.

4. **Búsqueda de usuario:** `findUsuarioByEmail(email)` consulta `espert-auth.dbo.Usuarios` (consulta parametrizada → sin SQL injection).

5. **Usuario válido y activo:** si `!user || !user.isActive` → `401 { error: "Credenciales inválidas" }`.
   > El mismo mensaje genérico se usa para "usuario inexistente", "usuario inactivo" y "contraseña incorrecta" — no se filtra qué condición falló.

6. **Verificación de contraseña:** `bcrypt.compare(password, user.passwordHash)`. Si no coincide → `401 { error: "Credenciales inválidas" }`.

7. **Emisión de token:** `createToken({ userId, email, role: user.rol, nombre })`. JWT HS256, expiración **8 horas**.

8. **Auditoría:** `logAudit({ userId, action: "LOGIN", resource: "auth" })` en modo *best-effort* (`.catch(() => {})`) — un fallo de auditoría **no** bloquea el login.

9. **Respuesta:** `200 { user: { id, email, name, role } }` + `Set-Cookie: auth-token=<jwt>`.

10. **Errores:** `ZodError → 400`; cualquier otro error → `500 { error: "Error interno del servidor" }` (con log en consola).

---

## 3. Token (JWT) y cookie

Definidos en [src/lib/auth.ts](../src/lib/auth.ts).

### Payload del JWT (`JWTPayload`)

| Campo | Tipo | Descripción |
|---|---|---|
| `userId` | string | ID del usuario |
| `email` | string | Email |
| `role` | string | Rol (`admin`, `facturacion`, `vendedor`, `logistica`) |
| `nombre` | string | Nombre para mostrar |
| `vendedorCodigoSk` | number \| null? | Código de vendedor para filtrar datos. `null`/`undefined` ⇒ ve **todos** los datos (admin). |

- **Algoritmo:** HS256.
- **Secret:** `process.env.JWT_SECRET` (fallback inseguro `"dev-secret-change-in-production"` si no está seteado).
- **Expiración:** `8h` (`setExpirationTime("8h")`), con `setIssuedAt()`.

> **Nota:** el endpoint de login actual **no** setea `vendedorCodigoSk` en el token. El campo existe en el tipo y lo consume el middleware/queries para filtrar datos por vendedor, pero hoy no se popula al iniciar sesión.

### Cookie (`getTokenCookieOptions`)

| Opción | Valor |
|---|---|
| `name` | `auth-token` |
| `httpOnly` | `true` (no accesible desde JS del cliente) |
| `secure` | `true` solo en producción (`NODE_ENV === "production"`) |
| `sameSite` | `lax` |
| `path` | `/` |
| `maxAge` | `60 * 60 * 8` = 8 horas |

### Helpers

- `verifyToken(token)` → valida firma y expiración; devuelve el payload o `null` si falla.
- `getSession()` → lee la cookie `auth-token` y la verifica (usado por `/api/auth/me`).

---

## 4. Logout

`POST /api/auth/logout` ([route.ts](../src/app/api/auth/logout/route.ts)):

- Re-emite la cookie `auth-token` con valor `""` y `maxAge: 0`, borrándola del navegador.
- Devuelve `200 { success: true }`.
- Es stateless: no hay invalidación de token del lado servidor (el JWT sigue siendo técnicamente válido hasta su expiración natural si alguien lo capturó; el logout solo elimina la cookie del cliente).

---

## 5. Protección de rutas (middleware)

[src/middleware.ts](../src/middleware.ts) corre en cada request (matcher excluye `_next/static`, `_next/image`, `favicon.ico`).

Orden de evaluación:

1. **Estáticos / internos de Next:** rutas que empiezan con `/_next`, `/favicon` o que contienen `.` pasan directo.

2. **Rate limiting (solo `/api`):** identifica IP por `x-forwarded-for` / `x-real-ip`. Límite **60 req/min por IP** ([rate-limit.ts](../src/lib/rate-limit.ts), en memoria). Excedido → `429 { error: "Too many requests" }` con `Retry-After: 60`.

3. **Rutas públicas (`PUBLIC_PATHS`):** `/login`, `/api/auth/login`, `/api-docs`, `/api/docs`. Pasan sin token (con headers de seguridad).

4. **Verificación de token:** lee cookie `auth-token`.
   - Sin token → **redirect a `/login`**.
   - Token inválido (`verifyToken` devuelve `null`) → **redirect a `/login`**.

5. **Inyección de contexto en headers** (para uso downstream):
   - `x-user-id` ← `payload.userId`
   - `x-user-role` ← `payload.role`
   - `x-vendedor-sk` ← `payload.vendedorCodigoSk` (solo si está presente; controla el filtrado de datos por vendedor).

6. **Headers de seguridad** (en todas las respuestas del middleware):
   - `Content-Security-Policy` (restrictivo; `frame-ancestors 'none'`, `form-action 'self'`, etc.)
   - `X-Content-Type-Options: nosniff`
   - `X-Frame-Options: DENY`
   - `Referrer-Policy: strict-origin-when-cross-origin`

---

## 6. Modelo de datos de usuario

Tabla `espert-auth.dbo.Usuarios` (Azure SQL). Campos relevantes para login ([auth-db.ts](../src/lib/auth-db.ts) — `UsuarioDB`):

| Campo | Tipo | Uso en login |
|---|---|---|
| `id` | string | Identidad en el token |
| `nombre` | string | `nombre` en token / `name` en respuesta |
| `email` | string | Credencial de login (único) |
| `passwordHash` | string | Comparado con `bcrypt.compare` |
| `rol` | string | `role` en el token |
| `isActive` | boolean | Debe ser `true` para autenticar |

- **No hay autoregistro.** La creación de usuarios la hace `admin` (`createUsuario` inserta con `isActive = 0`). El usuario queda inactivo hasta su activación; mientras `isActive = false` el login devuelve `401`.
- Roles y matriz de permisos: ver [usuarios-y-roles.md](usuarios-y-roles.md).

---

## 7. Respuestas y códigos de estado

| Situación | HTTP | Cuerpo |
|---|---|---|
| Login exitoso | 200 | `{ user: { id, email, name, role } }` + cookie |
| Datos de entrada inválidos | 400 | `{ error: "Datos inválidos", details }` |
| Credenciales inválidas / usuario inactivo | 401 | `{ error: "Credenciales inválidas" }` |
| BD de auth no configurada | 503 | `{ error: "Base de datos de autenticación no configurada" }` |
| Error interno | 500 | `{ error: "Error interno del servidor" }` |
| Rate limit excedido (middleware) | 429 | `{ error: "Too many requests" }` |

---

## 8. Consideraciones de seguridad

- ✅ Cookie `HttpOnly` + `SameSite=lax`; `Secure` en producción.
- ✅ Contraseñas con `bcrypt`; consultas SQL parametrizadas.
- ✅ Mensaje de error genérico (no enumera usuarios).
- ✅ Rate limiting y headers de seguridad (CSP, anti-clickjacking) en el middleware.
- ⚠️ **Bypass demo hardcodeado** (`demo@espert.com` / `demo`, rol admin) — remover antes de producción.
- ⚠️ **`JWT_SECRET` con fallback inseguro** — garantizar que esté seteado en todos los entornos.
- ⚠️ **Logout sin invalidación server-side** — el JWT permanece válido hasta expirar.
- ⚠️ **Rate limit en memoria** — no se comparte entre instancias (no apto para escalado horizontal sin un store compartido).
- ⚠️ `vendedorCodigoSk` no se setea en el token al hacer login, pese a que el middleware/queries lo usan para filtrar datos por vendedor.
