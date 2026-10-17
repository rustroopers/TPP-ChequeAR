# API Contracts: Seguridad y Control de Accesos

> **Desactualizado.** `[ChequeARSpecialty]` y `AuditMiddleware` no existen: los permisos van por
> `[RequiresPermissionApi]`, el filtro por especialidad está en los servicios y los `AuditEvent` se emiten desde ellos.
> El login es local; no hay API del hospital.

## Estado de implementación (2026-06-10)

| Endpoint | Estado | Notas |
|---|---|---|
| `POST /auth/login` | ✅ implementado | proxy a `IHospitalAuthClient` |
| `POST /auth/refresh` | ✅ implementado | rota access token |
| `POST /auth/logout` | ✅ implementado | revoca refresh |
| `GET /auth/me` | ✅ implementado | id + roles + specialties del JWT |
| `GET /fhir/r5/AuditEvent` (search) | ✅ implementado | rol Admin/Jefe |
| `GET /fhir/r5/AuditEvent/{id}` | ✅ implementado (nuevo) | A7 detalle de evento |
| `GET /api/users` | ✅ implementado (nuevo) | A4 listado, rol Admin |
| `GET /api/users/{id}` | ✅ implementado (nuevo) | A5 |
| `PUT /api/users/{id}` | ✅ implementado (nuevo) | A5 save |

**MVC counterpart de login**: `AccountController.DoLogin` setea `FormsAuthentication` cookie tras llamar a `IHospitalAuthClient`. El JWT queda disponible para las islas Vue vía endpoint `GET /auth/me` (recomendado embeber en meta tag — pendiente).

**Frontend**: pantallas A1, A2, A4, A5, A6, A7 — ver `specs/006-frontend-ux/api-integration.md`.

---


## Autenticación

### `POST /auth/login`

Proxy hacia la API del hospital. Recibe credenciales, las re-envía, recibe identidad+roles+especialidades, emite JWT propio.

**Body**:

```json
{ "username": "jdperez", "password": "..." }
```

**Response 200**:

```json
{
  "accessToken": "eyJ...",
  "refreshToken": "...",
  "expiresIn": 3600,
  "user": {
    "id": "usr-123",
    "displayName": "Juan Pérez",
    "roles": ["Medico"],
    "specialties": ["hepato", "reumato"]
  }
}
```

**Response 401**: credenciales rechazadas por el hospital.

### `POST /auth/refresh`

**Body**: `{ "refreshToken": "..." }`
**Response 200**: nuevo `accessToken`.
**Response 401**: refresh inválido o expirado.

### `POST /auth/logout`

Invalida el refresh token (lista local). El access token caduca solo.

### `GET /auth/me`

Devuelve la identidad y roles del token actual.

## JWT — claims emitidos

```json
{
  "iss": "https://chequear.garrahan",
  "sub": "usr-123",
  "name": "Juan Pérez",
  "roles": ["Medico"],
  "specialties": ["hepato", "reumato"],
  "iat": 1717200000,
  "exp": 1717203600,
  "jti": "..."
}
```

Firma: HS256 con secret rotable (clave en DPAPI/secrets local).

## Autorización en endpoints

Decoradores estándar:

```csharp
[Authorize(Roles = "Administrador,Jefe")]
[ChequeARSpecialty]  // filtro adicional por claim "specialties"
public HttpResponseMessage Get(...) { ... }
```

## AuditEvent (FHIR R5)

### `GET /fhir/r5/AuditEvent`

Permisos: sólo `Administrador` ve todo; `Jefe` ve los propios; el resto sin acceso.

**Query params estándar FHIR**: `agent.who`, `entity.what`, `date`, `outcome`, `action`.

**Response 200**: `Bundle` con `AuditEvent`.

### Forma del AuditEvent

```json
{
  "resourceType": "AuditEvent",
  "category": [{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/audit-event-type","code":"rest"}]}],
  "code": {"coding":[{"code":"create | read | update | delete | execute"}]},
  "action": "C",
  "recorded": "2026-06-01T12:34:56Z",
  "outcome": {"coding":[{"code":"0"}]},
  "agent": [{
    "who": {"identifier":{"value":"usr-123"}},
    "requestor": true,
    "networkString": "10.0.0.5"
  }],
  "source": {"observer":{"display":"chequear-api-01"}},
  "entity": [{
    "what": {"reference":"QuestionnaireResponse/qr-456"},
    "type": {"code":"2","display":"System object"}
  }]
}
```

## Operaciones sensibles que generan AuditEvent

| Operación | Acción FHIR |
|---|---|
| Login exitoso/fallido | `execute` + outcome 0/4 |
| Crear/editar Questionnaire | `create` / `update` |
| Publicar/archivar Questionnaire | `update` |
| Crear/editar/finalizar QuestionnaireResponse | `create` / `update` |
| Leer QR de paciente | `read` |
| Subir adjunto | `create` |
| Ejecutar reporte/exportar | `execute` |
| Acceso al dashboard Hangfire | `read` |
| Cambios de rol (vía hospital sync) | `update` |

## Códigos de error

| Código | Causa |
|---|---|
| 401 | Sin token, token inválido o expirado |
| 403 | Rol o especialidad insuficientes |
| 423 | Cuenta bloqueada por intentos fallidos (info viene del hospital) |
