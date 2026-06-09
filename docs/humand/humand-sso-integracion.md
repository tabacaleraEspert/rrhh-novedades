# Integración SSO con Humand

> **Estado:** Pendiente confirmar con Joaquín  
> **Fecha de análisis:** Mayo 2026  
> **Autor:** Análisis técnico — Copilot

---

## Resumen ejecutivo

Humand **no actúa como proveedor OAuth2/OIDC** para apps externas. Su API pública (`api-prod.humand.co/public/api/v1`) es una **API de integración server-to-server** que usa API Keys fijas, no sesiones de usuario. Para implementar el login con Humand en nuestra app necesitamos confirmar con Joaquín cuál de los dos escenarios aplica.

---

## Lo que ya sabemos

### Humand Public API
- **URL:** `https://api-prod.humand.co/public/api/v1`
- **Docs:** `https://api-prod.humand.co/public/api/v1/api-docs/`
- **Autenticación:** `Authorization: Basic {api_key}` (API Key de organización)
- **Rate limit:** 50 requests / 60 segundos
- **Versión:** OpenAPI 3.0.3 — v1.0.0

### Endpoints relevantes para nuestra app

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| `GET` | `/users/me` | Perfil del usuario autenticado |
| `GET` | `/users` | Listar usuarios con búsqueda y paginación |
| `GET` | `/users/{employeeInternalId}` | Obtener usuario por ID interno |
| `POST` | `/users` | Crear usuario |
| `PUT` | `/users` | Upsert usuario (crear o actualizar) |
| `PATCH` | `/users/{employeeInternalId}` | Actualizar usuario parcialmente |
| `DELETE` | `/users/{employeeInternalId}` | Eliminar usuario |
| `POST` | `/users/{employeeInternalId}/deactivate` | Desactivar usuario |
| `POST` | `/users/{employeeInternalId}/reactivate` | Reactivar usuario |
| `POST` | `/users/{employeeInternalId}/password-reset` | Resetear contraseña |
| `GET` | `/segmentations` | Obtener segmentaciones (áreas/grupos) |

### SSO que Humand usa *para sí mismos*
Humand autentica a sus propios usuarios usando SSO externo (Google, Microsoft, Okta, Apple). Ellos son el **SP (Service Provider)**, no el **IdP (Identity Provider)**. Por eso no exponen:
- `/.well-known/openid-configuration` → 404
- Endpoint OAuth2 de autorización público

---

## Estado actual del código

### Frontend (`apps/web`)
```tsx
// LoginPage.tsx
const HUMAND_SSO_URL = import.meta.env.VITE_HUMAND_SSO_URL

const handleHumandSSO = () => {
  if (HUMAND_SSO_URL) {
    window.location.href = HUMAND_SSO_URL  // redirige al IdP
  } else {
    // fallback: muestra form de email/password
  }
}
```
El botón "Ingresar con Humand" ya está implementado y es el **primario**. Las credenciales son secundarias y colapsables.

### Backend (`apps/api`)
Variables de entorno preparadas pero **vacías**:
```env
HUMAND_CLIENT_ID=
HUMAND_CLIENT_SECRET=
HUMAND_ISSUER_URL=
```
No existe aún ninguna estrategia Passport ni endpoint de callback para Humand.

---

## Dos escenarios posibles

### Escenario A — Token Exchange (más probable)

El usuario obtiene un token personal desde la app/web de Humand. Nuestro backend lo valida contra la API de Humand y emite nuestro propio JWT.

**Flujo:**
```
1. Usuario hace click en "Ingresar con Humand"
2. Se abre la app/web de Humand para que el usuario copie su API token personal
3. Usuario pega el token en nuestro campo
4. Nuestro backend llama GET /users/me con Authorization: Basic {token}
5. Si responde 200 → usuario válido → buscamos/creamos en nuestra DB → emitimos JWT
6. Usuario queda autenticado
```

**Contras:** UX mala (el usuario tiene que copiar un token). No es un SSO real.

---

### Escenario B — OAuth2 privado / enterprise (requiere confirmación)

Humand ofrece un endpoint OAuth2 para integraciones enterprise que no está en su documentación pública. Joaquín debe solicitarlo y configurarlo.

**Flujo estándar OAuth2 Authorization Code:**
```
1. Usuario hace click en "Ingresar con Humand"
2. Frontend redirige a: {HUMAND_ISSUER_URL}/oauth/authorize?
       client_id={HUMAND_CLIENT_ID}
       &redirect_uri=https://api.espert.com.ar/auth/humand/callback
       &response_type=code
       &scope=openid profile email
       &state={random}
3. Usuario se autentifica en Humand
4. Humand redirige a nuestro callback con ?code=xxx
5. Backend intercambia code por token: POST {HUMAND_ISSUER_URL}/oauth/token
6. Backend valida token, obtiene perfil del usuario
7. Busca/crea usuario en nuestra DB → emite nuestro JWT
8. Redirige al frontend con JWT
```

**Ventajas:** UX correcta, SSO real, el usuario nunca abandona el flujo.

---

### Escenario C — SAML (menos probable)

Humand soporta SAML como SP (no como IdP). Sus endpoints SAML son:
- **Entity ID:** `https://api-prod.humand.co/api/v1/sso-saml/{INSTANCE_ID}`
- **ACS URL:** `https://api-prod.humand.co/api/v1/sso-saml/callback?to={INSTANCE_ID}`

Este escenario serviría si quisiéramos que *los usuarios de Espert usen Azure AD o similar* para autenticarse en Humand y en nuestra app. No aplica para usar Humand como IdP de nuestra app.

---

## Preguntas para Joaquín

> Estas respuestas desbloquean el desarrollo del SSO.

1. **¿Humand ofrece OAuth2 para apps externas?**
   - Si sí → ¿cuál es la URL de autorización y el endpoint de token?
   - Si no → ¿cómo se espera que los usuarios autentifiquen en nuestra app usando Humand?

2. **¿Tenemos `client_id` y `client_secret` de Humand?**
   - Si sí → completar `HUMAND_CLIENT_ID` y `HUMAND_CLIENT_SECRET` en el `.env` del backend.

3. **¿Cuál es el `HUMAND_ISSUER_URL`?**
   - ¿Es algo como `https://app.humand.co/{tenant}` o `https://api-prod.humand.co`?

4. **¿Tenemos el API Key de organización de Espert en Humand?**
   - Necesario para sincronizar usuarios desde Humand → nuestra DB.

---

## Plan de implementación (cuando se confirmen los datos)

### Backend (NestJS)
```
apps/api/src/auth/
├── strategies/
│   └── humand.strategy.ts     ← estrategia Passport OAuth2
├── auth.controller.ts          ← agregar GET /auth/humand y GET /auth/humand/callback
└── auth.service.ts             ← método loginWithHumand()
```

### Frontend (React)
```
VITE_HUMAND_SSO_URL=http://localhost:3001/api/auth/humand
```
El frontend ya redirige a esta URL. Solo hay que cambiar el env.

### Variables de entorno a completar
```env
# Backend
HUMAND_CLIENT_ID=<obtener de Humand>
HUMAND_CLIENT_SECRET=<obtener de Humand>
HUMAND_ISSUER_URL=<obtener de Humand>
HUMAND_API_KEY=<API Key de organización Espert en Humand>

# Frontend
VITE_HUMAND_SSO_URL=https://api.espert.com.ar/api/auth/humand
VITE_API_URL=https://api.espert.com.ar/api
```

---

## Referencias

- [Humand Public API Swagger](https://api-prod.humand.co/public/api/v1/api-docs/)
- [Humand Help Center — Developers](https://help.humand.co/hc/en-us/categories/25584060245651-Developers)
- [Humand Help Center — Integrations](https://help.humand.co/hc/en-us/articles/26405750698771-How-to-integrate-Humand-with-other-systems)
- [Humand SAML SSO (Azure)](https://help.humand.co/hc/en-us/articles/41876584426131-How-to-integrate-Azure-SAML-in-Humand)
- `documentation/Manual Web API - Humand.pdf` — manual interno (v5)
