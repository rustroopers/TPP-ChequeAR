# Tasks: Gestor y Editor de Cuestionarios

> Convención: `[P]` = paralelizable; `[Sx]` = sprint sugerido.

## Fase 0 – Setup

- [ ] T001 [S1] Inicializar solución `ChequeAR.sln` con proyectos `Api`, `Application`, `Domain`, `Infrastructure`, `Shared`.
- [ ] T002 [S1][P] Configurar pipeline CI en GitHub Actions: build, unit tests, validador FHIR.
- [ ] T003 [S1][P] Definir perfil FHIR `ChequeARQuestionnaire` en `src/ChequeAR.Infrastructure/Fhir/Profiles/`.

## Fase 1 – Modelo de datos

- [ ] T010 [S1] Crear entidades en `ChequeAR.Domain.Entities`: `Questionnaire`, `QuestionnaireVersion`, `QuestionGroup`, `Question`, `AnswerOption`.
- [ ] T011 [S1] Configurar EF6 mapping y migración inicial (`Initial`).
- [ ] T012 [S1][P] Definir DTOs y mappers Domain ↔ FHIR (`QuestionnaireMapper`).

## Fase 2 – CRUD básico

- [ ] T020 [S2] Implementar `QuestionnaireController` con `GET`, `POST`, `PUT`, `DELETE`.
- [ ] T021 [S2] Implementar `QuestionnaireService` con reglas de negocio (transiciones de estado).
- [ ] T022 [S2] Implementar `QuestionnaireRepository` con queries optimizadas (índices ya definidos).
- [ ] T023 [S2][P] Tests unitarios `Domain` (entidades, value objects).
- [ ] T024 [S2][P] Tests de integración `Api` con SQL Server en Docker.

## Fase 3 – Versionado y operaciones FHIR

- [ ] T030 [S2] Implementar `$publish` (validación FHIR + creación de `QuestionnaireVersion`).
- [ ] T031 [S2] Implementar `$archive` (transición a `retired`).
- [ ] T032 [S3] Implementar `$clone`.
- [ ] T033 [S3] Implementar versionado automático al editar `active`.
- [ ] T034 [S3][P] Implementar `If-Match` y respuesta 409 en conflictos.

## Fase 4 – Funcionalidad avanzada

- [ ] T040 [S3] Soporte `enableWhen` (parsing + serialización FHIR).
- [ ] T041 [S3] Soporte preguntas tipo `attachment` (validación extensión/tamaño, integración blob).
- [ ] T042 [S3][P] Soporte agrupamiento jerárquico (subsecciones).

## Fase 5 – Hardening y entrega

- [ ] T050 [S4] Auditoría: emitir `AuditEvent` en todas las escrituras (delega en módulo 005).
- [ ] T051 [S4] Performance: índices, paginación cursor-based, tests de carga (200 RPS).
- [ ] T052 [S4][P] Documentación OpenAPI / FHIR CapabilityStatement.
- [ ] T053 [S4][P] Validar contra el validador oficial HL7 en CI.
- [ ] T054 [S4] Coverage ≥ 75% en `Domain` + `Application`.

## DoD (por tarea)

- Criterios de aceptación cumplidos.
- Tests unitarios y/o de integración.
- PR aprobado por otro integrante (Calderan / Chaia / Crudo / Merlinsky).
- Pipeline CI verde.
