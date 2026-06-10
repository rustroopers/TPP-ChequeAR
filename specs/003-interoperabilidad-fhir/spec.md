# Feature Specification: Capa de Interoperabilidad y Almacenamiento (FHIR)

**Feature Branch**: `003-interoperabilidad-fhir`
**Created**: 2026-06-01
**Status**: Draft

## Contexto

Núcleo de datos del sistema basado en HL7 FHIR R5. Provee:

- Persistencia interna de los recursos clínicos generados por ChequeAR (Questionnaire, QuestionnaireResponse, AuditEvent, Patient referenciado, Observation derivada).
- Cliente FHIR para consultar datos pre-existentes en el sistema hospitalario.
- Adaptador a la **vista Oracle** del hospital para los datos que no estén disponibles vía FHIR.
- Mapeo y validación contra el validador oficial HL7.

## User Scenarios & Testing

### User Story 1 - Persistir respuestas como recursos FHIR (Priority: P1)

Cuando el módulo 002 envía una `QuestionnaireResponse`, esta se persiste íntegra y queda disponible vía API FHIR estándar.

**Why this priority**: Sin persistencia FHIR no hay interoperabilidad — el objetivo principal del proyecto.

**Independent Test**: `POST` de un `QuestionnaireResponse` válido y verificación con el validador oficial HL7 R5 que el recurso almacenado es 100% compatible.

**Acceptance Scenarios**:

1. **Given** un `QuestionnaireResponse` recibido, **When** se persiste, **Then** un `GET` posterior devuelve el recurso byte-equivalente (semánticamente) al original.
2. **Given** un recurso inválido contra el perfil FHIR, **When** se intenta persistir, **Then** el sistema lo rechaza con `OperationOutcome`.

---

### User Story 2 - Consultar datos pre-existentes vía FHIR del hospital (Priority: P1)

El módulo 002 (vía `PrefillService`) o un usuario solicita datos clínicos previos de un paciente (peso, antecedentes). El módulo consume la API FHIR del hospital y devuelve `Observation`, `Condition`, `Patient`.

**Acceptance Scenarios**:

1. **Given** un paciente con ID interno `PAC-001` y datos de peso registrados, **When** se consulta `Observation?subject=PAC-001&code=29463-7`, **Then** se devuelve el último valor con su unidad.
2. **Given** que la API FHIR del hospital está caída, **When** se consulta, **Then** el sistema responde 503 con mensaje claro y NO interrumpe operaciones internas.

---

### User Story 3 - Adaptador a vista Oracle del hospital (Priority: P1)

Para variables no disponibles en FHIR, ChequeAR lee de una vista Oracle dedicada (solo lectura), exponiendo el dato como un recurso FHIR ad-hoc para uniformidad.

**Acceptance Scenarios**:

1. **Given** la vista `VW_PACIENTE_ANTECEDENTES`, **When** se consulta por `PAC-001`, **Then** el adaptador devuelve un `Bundle` de `Condition` derivado.
2. **Given** una columna con datos faltantes en la vista, **When** se mapea, **Then** se omite el recurso correspondiente sin error.

---

### User Story 4 - Validación FHIR continua (Priority: P2)

Todo recurso generado/recibido pasa por el validador oficial antes de ser persistido y publicado.

**Acceptance Scenarios**:

1. **Given** un recurso con `linkId` duplicado en `Questionnaire`, **When** se valida, **Then** el validador devuelve error y se rechaza la operación.

### Edge Cases

- Latencia alta en la API FHIR del hospital → caché LRU con TTL 60 s para lecturas idempotentes.
- Cambio de schema de la vista Oracle → adapter declara columnas esperadas; falta de columna devuelve error monitoreado.
- Inconsistencia entre `Patient` en FHIR del hospital y nuestro identificador → resolver canónicamente con el ID interno como autoridad.
- Pérdida temporal de conectividad a Oracle → circuit breaker con fallback a "datos no disponibles" en lugar de error duro.

## Requirements

### Functional Requirements

- **FR-001**: El sistema DEBE almacenar `Questionnaire`, `QuestionnaireResponse`, `AuditEvent`, `Observation` (derivadas) y referencias `Patient` con fidelidad FHIR R5.
- **FR-002**: El sistema DEBE exponer endpoints REST FHIR R5 (`GET`/`POST`/`PUT`/`DELETE`) con `application/fhir+json`.
- **FR-003**: El sistema DEBE exponer `CapabilityStatement` en `GET /metadata`.
- **FR-004**: El sistema DEBE consumir la API FHIR del hospital con autenticación delegada (token del usuario o service account).
- **FR-005**: El sistema DEBE proveer un adaptador a la vista Oracle del hospital (read-only) con mapeo declarativo columna → campo FHIR.
- **FR-006**: El sistema DEBE validar todos los recursos contra el validador oficial HL7 R5 y los perfiles propios de ChequeAR.
- **FR-007**: El sistema DEBE manejar fallos del hospital con circuit breaker, timeouts (5 s) y reintentos limitados (3 con backoff).
- **FR-008**: El sistema DEBE cachear lecturas idempotentes (Patient, Observation últimas) por 60 s.
- **FR-009**: El sistema DEBE soportar búsqueda FHIR con parámetros estándar (`subject`, `code`, `date`, `_lastUpdated`, `_count`).

### Key Entities

- **Recursos FHIR persistidos**: Questionnaire, QuestionnaireVersion (interno), QuestionnaireResponse, AuditEvent, Observation (derivadas opcionales), Attachment.
- **Adaptador Oracle**: tabla de mapeo declarativa `OracleViewMapping` (vista → recurso FHIR).
- **HospitalFhirClient**: cliente HTTP tipado para la API FHIR del hospital.

## Success Criteria

- **SC-001**: 100% de recursos generados aprueban el validador oficial HL7 (medido en CI sobre dataset de prueba).
- **SC-002**: Lecturas FHIR < 200 ms p95 para recursos propios; < 1 s p95 vía hospital (incluye cache hits).
- **SC-003**: Disponibilidad del módulo 99.5% mensual (excluyendo caída del backend hospitalario).
- **SC-004**: Cero pérdida de datos: writes confirmados son durables (fsync + replicación SQL).

## Assumptions

- El hospital expone una API FHIR (versión a confirmar) con OAuth2/JWT.
- La vista Oracle se materializa y refresca por el hospital; ChequeAR sólo lee.
- Los identificadores de paciente son consistentes entre FHIR hospital, Oracle y ChequeAR (ID interno).

## UI

Este módulo es predominantemente backend. Sus únicas superficies de UI son las pantallas **F1 (Panel salud de integraciones)** y **F2 (Visor CapabilityStatement)**, ambas administrativas. Ver `specs/006-frontend-ux/screens/03-fhir.md`.
