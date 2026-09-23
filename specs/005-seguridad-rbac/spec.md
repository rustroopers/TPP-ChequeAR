# Feature Specification: Seguridad y Control de Accesos (RBAC + Auditoría)

**Feature Branch**: `005-seguridad-rbac`
**Created**: 2026-06-01
**Status**: Draft

## Contexto

Módulo transversal que provee:

- **Autenticación**: local por defecto (contraseña hasheada con PBKDF2 contra `AppUser.PasswordHash`,
  ver `LocalCredentialAuthenticator`); se delega a la **API de logueo del Hospital Garrahan** el día
  que exista, configurando `HospitalAuth:LoginUrl` (`HospitalAuthClient`). Ambas implementan la misma
  interfaz (`ICredentialAuthenticator`) y son intercambiables sin tocar el resto del sistema.
- **Autorización**: RBAC con **tres roles** (`Administrador`, `Jefe`, `Médico`) más segmentación por
  especialidad. La matriz vive en un único lugar del código (`RolePolicy`), no repartida en atributos.
- **Auditoría**: registro detallado de toda operación sensible (creación, edición, publicación, lectura de fichas, exports), accesible vía `AuditEvent` FHIR.

## User Scenarios & Testing

### User Story 1 - Login local, con delegación futura al hospital (Priority: P1)

Un usuario autentica con su usuario y contraseña. Mientras la API de autenticación del Hospital
Garrahan no exista, ChequeAR valida contra su propio `AppUser.PasswordHash` (PBKDF2); cuando esa API
exista, basta configurar `HospitalAuth:LoginUrl` para que la validación pase a delegarse, sin cambiar
el resto del flujo (emisión de JWT, sync del espejo local, etc.).

**Why this priority**: Sin auth no opera ningún otro módulo. Es bloqueante.

**Independent Test**: Iniciar sesión con un usuario válido, recibir token, llamar a un endpoint protegido y obtener 200; llamar sin token y obtener 401.

**Acceptance Scenarios**:

1. **Given** credenciales válidas (local u hospital, según el modo activo), **When** se invoca `/auth/login`, **Then** ChequeAR devuelve un JWT con `sub` = usuario, `roles` y `specialties` claims.
2. **Given** un token expirado, **When** se llama a un endpoint protegido, **Then** se responde 401 con `WWW-Authenticate` y el cliente debe re-loguear.
3. **Given** un usuario inexistente, inactivo, sin contraseña configurada o con contraseña incorrecta, **When** intenta loguearse, **Then** ChequeAR responde 401 con el mismo mensaje genérico en los cuatro casos (no revela cuál aplica).

---

### User Story 2 - Autorización por rol (Priority: P1)

Cada endpoint enforza el rol mínimo requerido.

**Acceptance Scenarios**:

1. **Given** un `Médico`, **When** intenta `POST /Questionnaire`, **Then** 403 (sólo puede leer cuestionarios, no crearlos ni editarlos).
2. **Given** un `Jefe`, **When** intenta `$publish`, **Then** 200.
3. **Given** un `Administrador`, **When** intenta cualquier operación, **Then** se permite.
4. **Given** un `Jefe`, **When** intenta `GET /AuditEvent` o `GET /api/users`, **Then** 403 (auditoría y usuarios son exclusivos de `Administrador`).

---

### User Story 3 - Segmentación por especialidad (Priority: P2)

Un Médico solo accede a respuestas y cuestionarios de su(s) especialidad(es).

**Acceptance Scenarios**:

1. **Given** un Médico con especialidad "hepato", **When** consulta respuestas de "reumato", **Then** 403 o resultados vacíos según el endpoint.
2. **Given** un Jefe con dos especialidades, **When** consulta, **Then** ve resultados de ambas.

---

### User Story 4 - Auditoría detallada (Priority: P1)

Toda operación de creación, modificación, publicación, lectura de fichas de paciente y exportación genera un `AuditEvent`.

**Acceptance Scenarios**:

1. **Given** un Médico finaliza una `QuestionnaireResponse`, **When** la operación termina, **Then** se crea `AuditEvent` con agente, recurso, IP y resultado.
2. **Given** un Administrador, **When** consulta `GET /AuditEvent?agent.who=user-123`, **Then** ve todas las operaciones de ese usuario.
3. **Given** intentos fallidos repetidos de login, **When** ocurren ≥5 en 5 minutos, **Then** se registran y se dispara alerta en logs (futura integración con SIEM).

### Edge Cases

- Token revocado en el hospital → ChequeAR consulta `/introspect` o reduce TTL del JWT a 15 min.
- Usuario pierde su rol mientras tiene sesión activa → su próximo request es 403; el token caduca rápido.
- Cuestionarios sin especialidad → considerados "globales", accesibles por todos los roles autorizados.
- Logs no pueden almacenar contenido clínico crudo (PII) — sólo identificadores.

## Requirements

### Functional Requirements

- **FR-001**: El sistema DEBE autenticar localmente contra `AppUser.PasswordHash` mientras
  `HospitalAuth:LoginUrl` no esté configurado, y delegar a la API de logueo del Hospital Garrahan
  cuando sí lo esté — ambos modos vía la misma interfaz (`ICredentialAuthenticator`).
- **FR-002**: El sistema DEBE soportar los roles: `Administrador`, `Jefe`, `Médico`. Cada usuario
  tiene **exactamente uno** (no una acumulación); la jerarquía Administrador > Jefe > Médico se
  expresa sólo al emitir claims (`RoleHierarchy`), nunca en el dato guardado.
- **FR-003**: El sistema DEBE emitir un JWT propio con TTL ≤ 60 min y refresh token con TTL ≤ 8 h.
- **FR-004**: Cada endpoint DEBE declarar el permiso mínimo requerido vía
  `[RequiresPermission]`/`[RequiresPermissionApi]`, que consultan `RolePolicy` — la única fuente de
  verdad de la matriz rol→permiso. Ningún controller repite una lista de roles a mano.
- **FR-005**: El sistema DEBE filtrar resultados por especialidad para los roles `Jefe` y `Médico`
  (`Administrador` es siempre `Unrestricted`, ver `SpecialtyGuard`).
- **FR-006**: El sistema DEBE generar `AuditEvent` (FHIR R5) por cada operación sensible (lista en `audit-policy.md`).
- **FR-007**: Los `AuditEvent` DEBEN ser inmutables (no `UPDATE`/`DELETE`).
- **FR-008**: El sistema DEBE almacenar logs por ≥ 6 años (cumplimiento normativa de historia clínica en Argentina).
- **FR-009**: El sistema DEBE almacenar un hash PBKDF2 con salt por usuario (`PasswordHasher`) para el
  modo de autenticación local — nunca la contraseña en claro. Cuando `HospitalAuth:LoginUrl` está
  configurado, la autenticación se delega y el hash local queda sin uso (puede quedar `null`).
- **FR-010**: El sistema DEBE registrar intentos fallidos de autorización y permitir consulta por administrador.
- **FR-011**: El sistema DEBE proteger endpoints de administración (Hangfire dashboard, ports admin) sólo a `Administrador`.

### Matriz de permisos (resumen)

Fuente autoritativa: `ChequeAR.Application.Security.RolePolicy` (`RolePolicyTests` verifica cada celda).

| Operación | Administrador | Jefe | Médico |
|---|:-:|:-:|:-:|
| Leer cuestionarios | ✅ | ✅ | ✅ |
| Crear/editar/publicar/archivar/clonar cuestionarios | ✅ | ✅ | ❌ |
| Borrar cuestionario | ✅ | ❌ (archiva, no borra) | ❌ |
| Leer/cargar fichas (QuestionnaireResponse) | ✅ | ✅ (esp) | ✅ (esp) |
| Reportes / Export | ✅ | ✅ (esp) | ❌ |
| Auditoría | ✅ | ❌ | ❌ |
| Gestión de usuarios | ✅ | ❌ | ❌ |
| Estado de integraciones | ✅ | ✅ | ❌ |
| Terminología (`$expand`, autocomplete SNOMED) | ✅ | ✅ | ✅ |

### Key Entities

- **User**: espejo local del padrón. Con `PasswordHash`/`PasswordUpdatedAt` para el modo de
  autenticación local (ver FR-009); `null` mientras no se haya sembrado/asignado una contraseña.
- **Role**: 3 valores fijos en código (`Domain.Enums.Role`), uno por usuario.
- **Permission**: catálogo de operaciones protegidas (`Domain.Enums.Permissions`), mapeado a roles por `RolePolicy`.
- **Specialty**: catálogo de especialidades pediátricas.
- **AuditEvent**: recurso FHIR R5.
- **UserSpecialty**: tabla local que mapea usuario ↔ especialidades (sync periódica con el hospital o on-login).

## Success Criteria

- **SC-001**: 100% de endpoints protegidos enforzan rol vía atributo declarativo (audit de código en CI).
- **SC-002**: 100% de operaciones sensibles producen `AuditEvent` (test E2E).
- **SC-003**: Tiempo de validación de JWT < 5 ms p95.
- **SC-004**: Auditoría retenible y consultable ≥ 6 años sin degradación de performance > 2×.

## Assumptions

- La API de logueo del hospital, cuando exista, expone OAuth2 / OpenID Connect o un endpoint propio
  que devuelva user info + roles + especialidades; hasta entonces, ChequeAR es la fuente de verdad
  de las contraseñas (modo local, ver FR-001/FR-009).
- Los roles son: Administrador, Jefe, Médico (el rol `Observador`, presente en una iteración anterior
  de este spec, se eliminó del sistema).
- El reloj del servidor está sincronizado con NTP del hospital para validar tokens.
- La normativa relevante (Ley 25.326 datos personales, Ley 26.529 derechos del paciente, Resolución MS 1480/2011) se cumple por retención + auditoría.

## UI

Las pantallas A1–A7 son entregables del equipo (spec 006, `screens/05-seguridad.md`):

- **A1 Login / A2 Sesión expirada**: pantallas Razor sin shell — autenticación delegada al hospital, formulario propio en frontend.
- **A3 Selector de especialidad activa**: modal Razor (usuarios multi-especialidad).
- **A4 Listado / A5 Edición de usuarios**: pantallas Razor condicionales a si la gestión de usuarios queda en ChequeAR (decisión abierta — ver Open Questions de spec 006).
- **A6 Visor / A7 Detalle de auditoría**: pantallas Razor con DataTables, filtros server-side, export integrado al módulo 004.
