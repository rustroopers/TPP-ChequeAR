# Feature Specification: Gestor y Editor de Cuestionarios

**Feature Branch**: `001-gestor-cuestionarios`
**Created**: 2026-06-01
**Status**: Draft
**Input**: Anteproyecto ChequeAR + Respuestas GP 201

## User Scenarios & Testing

### User Story 1 - Crear cuestionario paramétrico (Priority: P1)

Un Administrador o Jefe Médico define un nuevo cuestionario clínico para una patología (ej. enfermedad reumática), agrupa preguntas en secciones, tipifica cada variable (numérica, texto, fecha, opción única, opción múltiple, booleano, adjunto) y configura rangos de validación.

**Why this priority**: Sin la capacidad de definir cuestionarios, ningún otro módulo del sistema entrega valor.

**Independent Test**: Crear un cuestionario "Trasplante hepático – Seguimiento" con 3 secciones y 10 preguntas tipificadas, guardarlo como Borrador y verificar que persiste con todas sus configuraciones.

**Acceptance Scenarios**:

1. **Given** un usuario con rol Administrador autenticado, **When** crea un cuestionario con título, especialidad y al menos un grupo de preguntas, **Then** el sistema lo persiste en estado `Borrador` y le asigna un identificador único.
2. **Given** un cuestionario en edición, **When** se agrega una pregunta numérica con rango min/max, **Then** la configuración queda asociada a la pregunta y se valida al renderizar.
3. **Given** un usuario con rol Médico, **When** intenta crear un cuestionario, **Then** el sistema rechaza la operación con HTTP 403.

---

### User Story 2 - Publicar cuestionario para uso clínico (Priority: P1)

El Administrador transiciona un cuestionario de `Borrador` a `Publicado` para habilitar su uso en consultas reales.

**Why this priority**: La separación borrador/publicado evita que cuestionarios incompletos se utilicen en pacientes.

**Independent Test**: Cambiar el estado de un cuestionario válido y verificar que aparece disponible en el motor de renderizado; cuestionarios en borrador no aparecen.

**Acceptance Scenarios**:

1. **Given** un cuestionario en `Borrador` con todos los campos obligatorios completos, **When** el Administrador lo publica, **Then** su estado pasa a `Publicado` y queda inmutable salvo por versionado.
2. **Given** un cuestionario `Publicado`, **When** se edita su estructura, **Then** el sistema crea una nueva versión y mantiene la anterior para preservar respuestas históricas.

---

### User Story 3 - Configurar lógica condicional entre preguntas (Priority: P2)

El editor permite definir que ciertas preguntas se muestren u oculten según la respuesta a otra (skip logic).

**Why this priority**: Es central para evitar fichas largas e irrelevantes, pero la captura básica funciona sin ello.

**Acceptance Scenarios**:

1. **Given** una pregunta A booleana y una pregunta B condicionada a `A=true`, **When** se renderiza la ficha, **Then** B solo aparece cuando A vale verdadero.

---

### User Story 4 - Adjuntar imágenes/documentos a preguntas (Priority: P2)

El editor permite asociar archivos (ej. foto de una lesión) a una pregunta de tipo "adjunto".

**Acceptance Scenarios**:

1. **Given** un cuestionario en edición, **When** se agrega una pregunta tipo `attachment` con extensiones permitidas, **Then** durante el renderizado el usuario puede subir archivos que cumplan dicha restricción.

---

### Edge Cases

- Edición concurrente: dos Administradores editan el mismo cuestionario simultáneamente → optimistic locking con `versionToken`.
- Eliminación de cuestionario `Publicado` con respuestas existentes → no se elimina, se marca `Archivado`.
- Duplicar cuestionario para clonar estructura → funcionalidad permitida sobre cualquier estado.
- Cuestionario sin preguntas → no puede publicarse.

## Requirements

### Functional Requirements

- **FR-001**: El sistema DEBE permitir crear, editar, clonar, archivar y eliminar (soft delete) cuestionarios a usuarios con rol Administrador.
- **FR-002**: El sistema DEBE soportar al menos los tipos de pregunta: numérica, texto corto, texto largo, fecha, hora, opción única, opción múltiple, booleano, escala, adjunto.
- **FR-003**: Cada pregunta DEBE poder configurarse como obligatoria/opcional y con reglas de validación (regex, rango, longitud).
- **FR-004**: El sistema DEBE soportar agrupamiento jerárquico de preguntas en secciones y subsecciones.
- **FR-005**: El sistema DEBE soportar lógica condicional `enableWhen` entre preguntas (compatible con FHIR Questionnaire).
- **FR-006**: El sistema DEBE manejar dos estados: `Borrador` y `Publicado`, con transición controlada por rol.
- **FR-007**: La publicación de un cuestionario DEBE generar una versión inmutable; ediciones posteriores crean nuevas versiones.
- **FR-008**: El sistema DEBE registrar en auditoría toda creación, edición, publicación y archivado.
- **FR-009**: El sistema DEBE permitir asociar metadata clínica (CIE-10, SNOMED-CT) opcional a cuestionarios y preguntas.
- **FR-010**: El sistema DEBE serializar el cuestionario como recurso FHIR `Questionnaire` (R5).

### Key Entities

- **Questionnaire**: contenedor con título, descripción, especialidad, estado, versión, autor, fecha. Equivale a `Questionnaire` FHIR.
- **QuestionGroup**: agrupador jerárquico con título y preguntas hijas.
- **Question**: ítem con tipo, texto, validaciones, condicionales, codificación opcional.
- **AnswerOption**: opciones predefinidas para preguntas de opción única/múltiple.
- **QuestionnaireVersion**: snapshot inmutable publicado.

## Success Criteria

- **SC-001**: Un Administrador puede crear y publicar un cuestionario completo (≥10 preguntas, ≥2 secciones) en menos de 20 minutos sin asistencia técnica.
- **SC-002**: El 100% de cuestionarios publicados es validado correctamente por el validador oficial HL7 FHIR R5.
- **SC-003**: Tiempo de respuesta del endpoint de creación < 500 ms p95 con cuestionarios de hasta 100 preguntas.
- **SC-004**: Cero pérdidas de datos al versionar: respuestas históricas siempre referencian su versión exacta.

## Assumptions

- La UI del gestor (pantallas Q1–Q9) la entrega el equipo como parte de ChequeAR — ver spec 006 (`screens/01-gestor.md`). Este módulo expone la API REST consumida por esa UI.
- El identificador del autor proviene del módulo de Seguridad/RBAC (referencia al usuario autenticado vía API del Hospital).
- La especialidad pediátrica es un catálogo fijo administrado fuera del alcance del MVP.
- El validador HL7 oficial está disponible como servicio externo o librería embebida (HAPI FHIR Validator).
