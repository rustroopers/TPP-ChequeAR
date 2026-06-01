# Feature Specification: Seguridad y Control de Accesos (RBAC + Auditoría)

**Feature Branch**: `005-seguridad-rbac`
**Created**: 2026-06-01
**Status**: Draft

## Contexto

Módulo transversal que provee:

- **Autenticación**: delegada a la **API de logueo del Hospital Garrahan** (no se gestionan contraseñas en ChequeAR).
- **Autorización**: RBAC con cuatro roles (`Administrador`, `Jefe`, `Médico`, `Observador`) más segmentación por especialidad.
- **Auditoría**: registro detallado de toda operación sensible (creación, edición, publicación, lectura de fichas, exports), accesible vía `AuditEvent` FHIR.

## User Scenarios & Testing

### User Story 1 - Login delegado al hospital (Priority: P1)

Un Médico autentica usando sus credenciales hospitalarias; ChequeAR obtiene su identidad y roles desde la API del hospital y emite un token de sesión para el frontend.

**Why this priority**: Sin auth no opera ningún otro módulo. Es bloqueante.

**Independent Test**: Iniciar sesión con un usuario válido, recibir token, llamar a un endpoint protegido y obtener 200; llamar sin token y obtener 401.

**Acceptance Scenarios**:

1. **Given** credenciales válidas del hospital, **When** se invoca `/auth/login`, **Then** ChequeAR devuelve un JWT con `sub` = usuario, `roles` y `specialties` claims.
2. **Given** un token expirado, **When** se llama a un endpoint protegido, **Then** se responde 401 con `WWW-Authenticate` y el cliente debe re-loguear.
3. **Given** un usuario no registrado en el hospital, **When** intenta loguearse, **Then** la API del hospital rechaza y ChequeAR responde 401.

---

### User Story 2 - Autorización por rol (Priority: P1)

Cada endpoint enforza el rol mínimo requerido.

**Acceptance Scenarios**:

1. **Given** un `Observador`, **When** intenta `POST /Questionnaire`, **Then** 403.
2. **Given** un `Jefe`, **When** intenta `$publish`, **Then** 200.
3. **Given** un `Administrador`, **When** intenta cualquier operación, **Then** se permite.

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

- **FR-001**: El sistema DEBE delegar autenticación a la API de logueo del Hospital Garrahan.
- **FR-002**: El sistema DEBE soportar los roles: `Administrador`, `Jefe`, `Médico`, `Observador`.
- **FR-003**: El sistema DEBE emitir un JWT propio con TTL ≤ 60 min y refresh token con TTL ≤ 8 h.
- **FR-004**: Cada endpoint DEBE declarar el rol mínimo requerido vía atributo `[Authorize(Roles="...")]`.
- **FR-005**: El sistema DEBE filtrar resultados por especialidad para roles `Médico` y `Observador`.
- **FR-006**: El sistema DEBE generar `AuditEvent` (FHIR R5) por cada operación sensible (lista en `audit-policy.md`).
- **FR-007**: Los `AuditEvent` DEBEN ser inmutables (no `UPDATE`/`DELETE`).
- **FR-008**: El sistema DEBE almacenar logs por ≥ 6 años (cumplimiento normativa de historia clínica en Argentina).
- **FR-009**: El sistema NO DEBE almacenar contraseñas ni hashes de usuario.
- **FR-010**: El sistema DEBE registrar intentos fallidos de autorización y permitir consulta por administrador.
- **FR-011**: El sistema DEBE proteger endpoints de administración (Hangfire dashboard, ports admin) sólo a `Administrador`.

### Matriz de permisos (resumen)

| Operación | Administrador | Jefe | Médico | Observador |
|---|:-:|:-:|:-:|:-:|
| CRUD Questionnaire | ✅ | ✅ | ❌ | ❌ |
| Publish/Archive | ✅ | ✅ | ❌ | ❌ |
| Crear/Editar QR | ✅ | ✅ | ✅ | ❌ |
| Leer QR | ✅ | ✅ (esp) | ✅ (esp) | ✅ (esp) |
| Reportes / Export | ✅ | ✅ | ✅ (esp) | ✅ (esp) |
| Auditoría | ✅ | sólo lectura propia | ❌ | ❌ |
| Gestión usuarios | ✅ | ❌ | ❌ | ❌ |

### Key Entities

- **User (referencia)**: identificado por id del hospital. ChequeAR no almacena perfil completo, sólo cache local.
- **Role**: enum fijo en código.
- **Specialty**: catálogo de especialidades pediátricas.
- **AuditEvent**: recurso FHIR R5.
- **UserSpecialty**: tabla local que mapea usuario ↔ especialidades (sync periódica con el hospital o on-login).

## Success Criteria

- **SC-001**: 100% de endpoints protegidos enforzan rol vía atributo declarativo (audit de código en CI).
- **SC-002**: 100% de operaciones sensibles producen `AuditEvent` (test E2E).
- **SC-003**: Tiempo de validación de JWT < 5 ms p95.
- **SC-004**: Auditoría retenible y consultable ≥ 6 años sin degradación de performance > 2×.

## Assumptions

- La API de logueo del hospital expone OAuth2 / OpenID Connect o un endpoint propio que devuelva user info + roles + especialidades.
- Los roles del hospital son: Administrador, Jefe, Médico, Observador (confirmado por GP 201).
- El reloj del servidor está sincronizado con NTP del hospital para validar tokens.
- La normativa relevante (Ley 25.326 datos personales, Ley 26.529 derechos del paciente, Resolución MS 1480/2011) se cumple por retención + auditoría.
