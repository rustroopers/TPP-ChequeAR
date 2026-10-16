# Feature Specification: Versionado de variables clínicas

**Feature Branch**: `fix/logica-clinica-fhir`

**Created**: 2026-10-15

**Status**: Draft

**Input**: Decisión del dueño a partir de CLI-10 del análisis integral: "las variables vamos a versionarlas también, de forma similar a los cuestionarios". Decisiones del 2026-10-15: publicación explícita, aviso con botón para actualizar en los cuestionarios, código y tipo fijos desde la primera publicación.

## Contexto

Hoy una variable clínica es una única fila editable con un contador `Revision`. Al agregarla a un cuestionario, la pregunta copia la definición y guarda la revisión. De ahí salen los problemas de CLI-10:

1. Activar o desactivar una variable con opciones falla (ORA-00001) y además sube la revisión, así que todos los borradores avisan un cambio que no existió.
2. Una variable que sólo usan versiones publicadas se puede borrar: las versiones congeladas quedan apuntando a algo que no existe.
3. El código y el tipo se editan aunque la variable esté en uso, y los reportes mezclan columnas bajo el mismo linkId.
4. Dos administradores que editan a la vez se pisan sin aviso.

Además, la definición de una revisión vieja no se guarda en ningún lado: sólo queda la copia dentro de cada pregunta.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publicar una versión de una variable (Priority: P1)

El Administrador corrige el rango de "Temperatura" o agrega una opción a "Severidad". Quiere preparar el cambio sin que afecte a nadie hasta que esté listo, y dejar constancia de qué cambió.

**Why this priority**: Es el núcleo de la feature. Sin versiones publicadas no hay nada que congelar ni a qué atar las preguntas.

**Independent Test**: Editar una variable publicada, verificar que los Jefes siguen viendo la versión vigente, publicar con una nota y verificar que aparece la versión nueva en el historial.

**Acceptance Scenarios**:

1. **Given** una variable nueva, **When** el Administrador la guarda, **Then** queda como borrador sin versión publicada y no aparece en el buscador del editor de cuestionarios.
2. **Given** un borrador válido, **When** el Administrador publica con una nota, **Then** se crea la versión 1, inmutable, con la definición completa, la nota, quién y cuándo.
3. **Given** una variable publicada, **When** el Administrador edita el borrador, **Then** la variable muestra "cambios sin publicar" y los Jefes siguen usando la versión vigente.
4. **Given** cambios sin publicar, **When** el Administrador publica, **Then** se crea la versión N+1 y pasa a ser la vigente.
5. **Given** una variable publicada, **When** el Administrador abre su historial, **Then** ve todas las versiones con su número, fecha, autor y nota, y puede ver la definición de cada una.

---

### User Story 2 - Actualizar las preguntas de un cuestionario a la versión nueva (Priority: P1)

El Jefe abre el editor de su cuestionario después de que el Administrador publicó una versión nueva de una variable que usa. Quiere decidir si toma el cambio, sin tener que borrar y volver a agregar la pregunta.

**Why this priority**: Hoy la única salida es borrar la pregunta, lo que cambia el linkId y rompe la continuidad de reportes y evolución.

**Independent Test**: Publicar la versión 2 de una variable usada en un borrador, abrir el editor y actualizar la pregunta con el botón.

**Acceptance Scenarios**:

1. **Given** un borrador con una pregunta atada a la versión 1 y una versión 2 publicada, **When** el Jefe abre el editor, **Then** ve un aviso con las preguntas desactualizadas y un botón "Actualizar a v2" en cada una.
2. **Given** ese aviso, **When** el Jefe actualiza la pregunta, **Then** toma la definición de la versión 2 y conserva el enunciado, si es obligatoria, la condición y el linkId.
3. **Given** un cuestionario publicado, **When** se publica una versión nueva de una variable que usa, **Then** la versión publicada del cuestionario no cambia.
4. **Given** una actualización que deja sin sentido una condición (por ejemplo, la opción que la condición esperaba ya no existe), **When** el Jefe actualiza, **Then** el editor lo marca igual que hoy marca las condiciones inválidas.

---

### User Story 3 - Dar de baja, reactivar y borrar sin romper lo publicado (Priority: P2)

El Administrador quiere retirar una variable que ya no se usa sin romper los cuestionarios que la tienen.

**Acceptance Scenarios**:

1. **Given** una variable con opciones, **When** el Administrador la desactiva o la reactiva, **Then** la operación funciona y no se crea una versión nueva ni aparece el aviso de cambios en los cuestionarios.
2. **Given** una variable que nunca se publicó, **When** el Administrador la borra, **Then** se elimina.
3. **Given** una variable con alguna versión publicada, **When** el Administrador la borra, **Then** se desactiva en vez de eliminarse, y se le informa.
4. **Given** una variable inactiva, **When** un Jefe busca en el catálogo, **Then** no la encuentra; las preguntas que ya la usan siguen funcionando.

---

### User Story 4 - Proteger la identidad de la variable (Priority: P2)

**Acceptance Scenarios**:

1. **Given** una variable con al menos una versión publicada, **When** el Administrador intenta cambiar el código o el tipo, **Then** el sistema lo rechaza y explica que hay que crear otra variable.
2. **Given** dos administradores editando el mismo borrador, **When** el segundo guarda después del primero, **Then** el sistema rechaza el segundo guardado con un aviso, en vez de pisar el primero.

### Edge Cases

- Variables existentes al migrar: cada una pasa a la versión 1 con su definición actual.
- Preguntas existentes que usaban una revisión vieja: no hay definición guardada de esa revisión. Quedan sin versión y el editor las marca como desactualizadas.
- Publicar sin cambios respecto de la vigente: no crea una versión nueva.
- Una opción eliminada en una versión nueva: las respuestas viejas conservan su código, y los reportes lo muestran con la etiqueta de la versión en que existía (ver CLI-07).
- Una variable con cambios sin publicar que se agrega a un cuestionario: se usa la versión vigente, nunca el borrador.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Cada variable MUST tener un borrador editable y cero o más versiones publicadas inmutables.
- **FR-002**: Publicar MUST validar el borrador con las reglas actuales, crear la versión N+1 con la definición completa (incluidas las opciones), la nota, el autor y la fecha, y marcarla como vigente.
- **FR-003**: El editor de cuestionarios y el buscador del catálogo MUST ofrecer sólo variables activas con versión publicada, y MUST usar la versión vigente.
- **FR-004**: Cada pregunta MUST quedar atada a una versión concreta de su variable; la versión publicada del cuestionario MUST registrar qué versión usó cada pregunta.
- **FR-005**: El editor MUST avisar las preguntas atadas a una versión anterior a la vigente y MUST permitir actualizarlas una por una sin cambiar el linkId.
- **FR-006**: Activar y desactivar MUST cambiar sólo el estado de la variable, sin crear versión.
- **FR-007**: Borrar MUST eliminar sólo variables nunca publicadas; con versiones publicadas MUST desactivar.
- **FR-008**: El código y el tipo MUST quedar fijos desde la primera publicación.
- **FR-009**: El guardado del borrador MUST rechazar una edición basada en una revisión desactualizada (concurrencia optimista).
- **FR-010**: Publicar, activar, desactivar y borrar MUST auditarse.

### Key Entities

- **Variable clínica**: el borrador editable, su estado (activa o inactiva) y la versión vigente.
- **Versión de variable**: foto inmutable de la definición publicada, con número, nota, autor y fecha.
- **Pregunta**: copia la definición de una versión concreta y la referencia.

## Success Criteria *(mandatory)*

- **SC-001**: Activar y desactivar cualquier variable, con o sin opciones, funciona sin errores.
- **SC-002**: Ninguna versión publicada de un cuestionario referencia una variable o versión inexistente.
- **SC-003**: Un Jefe toma la definición nueva de una variable sin borrar la pregunta ni cambiar su linkId.
- **SC-004**: Ninguna variable publicada cambia de código ni de tipo.
