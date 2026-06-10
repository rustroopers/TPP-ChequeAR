# Feature Specification: Motor de Renderizado Dinámico

**Feature Branch**: `002-motor-renderizado`
**Created**: 2026-06-01
**Status**: Draft

## Contexto

La UI completa de ChequeAR es entregable propio del equipo (spec 006). Este módulo se compone de dos partes:

1. **Backend de soporte al renderizado**: endpoints REST/FHIR que entregan el cuestionario publicado, persisten respuestas, validan en servidor y exponen lógica condicional.
2. **Isla Vue `questionnaire-renderer`** (pantalla R3 — ver `specs/006-frontend-ux/screens/02-renderizado.md`): componente que interpreta el JSON FHIR `Questionnaire` y produce los controles dinámicos dentro de la página Razor.

## User Scenarios & Testing

### User Story 1 - Renderizar y completar ficha clínica (Priority: P1)

Un Médico abre un paciente, selecciona un cuestionario `Publicado` y completa la ficha. El sistema valida en tiempo real y al guardar persiste un `QuestionnaireResponse`.

**Why this priority**: Es el caso de uso central — sin esto, no hay valor clínico.

**Independent Test**: Publicar un cuestionario con 10 preguntas mixtas, cargarlo desde la UI, completarlo y verificar la `QuestionnaireResponse` resultante.

**Acceptance Scenarios**:

1. **Given** un cuestionario `active` y un paciente válido, **When** el Médico abre la ficha, **Then** el motor renderiza todos los grupos y preguntas respetando el orden y los tipos.
2. **Given** una pregunta numérica con rango 1-200, **When** el usuario ingresa 250, **Then** la UI marca error en línea y el backend rechaza el guardado.
3. **Given** una pregunta condicional `enableWhen A=true`, **When** A vale `false`, **Then** B no aparece ni se valida.

---

### User Story 2 - Guardado parcial y reanudación (Priority: P2)

Un Médico interrumpe la carga y al volver retoma desde donde dejó.

**Acceptance Scenarios**:

1. **Given** una ficha en progreso, **When** el Médico cierra la ventana sin guardar explícitamente, **Then** el motor persiste un autosave cada 30 s.
2. **Given** un autosave existente, **When** el Médico abre el mismo paciente+cuestionario, **Then** se le ofrece reanudar.

---

### User Story 3 - Pre-carga desde sistemas hospitalarios (Priority: P2)

Algunas preguntas se pre-completan automáticamente desde datos ya existentes (peso, antecedentes) — vía FHIR o vista Oracle (ver módulo 003).

**Acceptance Scenarios**:

1. **Given** una pregunta marcada con `linkage` a un observable FHIR, **When** se abre la ficha, **Then** la respuesta se prellena con el último valor conocido y se marca visualmente como "pre-cargado".
2. **Given** el valor pre-cargado, **When** el Médico lo modifica, **Then** la pregunta se marca como "editado" y la versión nueva se persiste.

---

### User Story 4 - Visualizar fichas históricas en modo lectura (Priority: P3)

Rol Observador (y otros) consulta respuestas previas sin poder editarlas.

**Acceptance Scenarios**:

1. **Given** un usuario rol Observador, **When** abre una ficha, **Then** los controles se muestran deshabilitados y el botón Guardar oculto.

### Edge Cases

- Cuestionario publicado con nueva versión mientras hay un draft abierto del anterior → se guarda contra la versión original.
- Pregunta `required` no respondida → no se puede finalizar (sí se puede autosaveear).
- Adjunto > 10 MB → rechazado con error claro.
- Pérdida de red durante autosave → reintentos con backoff, cola local dentro de la isla Vue.

## Requirements

### Functional Requirements

- **FR-001**: El sistema DEBE servir `Questionnaire` por id+versión vía endpoint FHIR.
- **FR-002**: El sistema DEBE aceptar `QuestionnaireResponse` con validación estructural y de reglas.
- **FR-003**: El sistema DEBE validar `enableWhen` en servidor (no confiar solo en el cliente).
- **FR-004**: El sistema DEBE soportar autosave (status `in-progress`) y finalización (status `completed`).
- **FR-005**: El sistema DEBE pre-cargar respuestas desde fuentes externas (módulo 003) según `extension` de "fuente".
- **FR-006**: El sistema DEBE rechazar respuestas a cuestionarios `retired` salvo modo lectura.
- **FR-007**: El sistema DEBE permitir vincular cada respuesta a un paciente por su ID interno hospitalario.
- **FR-008**: El sistema DEBE preservar las respuestas asociadas a la versión exacta del `Questionnaire` con la que se cargó.
- **FR-009**: El sistema DEBE soportar adjuntos (≤10 MB; `.jpg`, `.png`, `.pdf`).

### Key Entities

- **QuestionnaireResponse**: respuesta del paciente a un cuestionario (FHIR R5).
- **Patient (referencia)**: identificado por ID interno hospitalario; el recurso completo proviene del módulo 003.
- **Encounter (opcional)**: contexto clínico de la respuesta.

## Success Criteria

- **SC-001**: Tiempo de carga de una ficha de 50 preguntas < 1 s p95.
- **SC-002**: Tiempo de guardado completo < 500 ms p95.
- **SC-003**: 0% de pérdida de datos por desconexión gracias al autosave + cola local.
- **SC-004**: 90% de usuarios completan una ficha piloto sin asistencia en menos de 5 minutos (medido en sesiones de usabilidad).

## Assumptions

- La UI (pantallas R1–R7, spec 006) implementa los controles; este módulo provee API y el componente Vue de renderizado.
- El paciente ya existe en el sistema hospitalario; se referencia, no se crea aquí.
- El usuario autenticado se obtiene del módulo 005.
