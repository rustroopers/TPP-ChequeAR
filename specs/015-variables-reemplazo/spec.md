# Feature Specification: Reemplazo de variables clínicas

**Feature Branch**: `dev`

**Created**: 2026-10-18

**Status**: Implementada

**Input**: Pedido expreso del Hospital Garrahan: al editar una variable ya existente se crea una variable nueva a partir de la anterior más los cambios, con la opción de dar de baja la anterior, y las dos tienen que distinguirse. Decisiones del 2026-10-18: sólo los cambios clínicos crean variable nueva; nombre distinto siempre; aviso y botón "Reemplazar" en los cuestionarios; reportes y evolución con la anterior y la nueva separadas y marcadas.

## Contexto

La [014](../014-versionado-variables/spec.md) versionaba la variable: editarla y publicarla creaba la versión N+1 de la misma variable, con el mismo código y por lo tanto el mismo linkId en las preguntas. Los reportes y la evolución juntaban en una misma columna datos medidos con definiciones distintas (otro rango, otras opciones, otro concepto).

Con esta feature una variable publicada no cambia su definición clínica: el cambio se hace creando otra variable, con su propio código y su propia columna. Por eso las versiones de variables dejan de tener sentido y se quitan: una variable está publicada o no.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Crear una variable a partir de otra (Priority: P1)

El Administrador necesita cambiar el rango de "Peso" para pediatría. No edita "Peso": crea "Peso pediátrico" a partir de ella y decide si "Peso" queda de baja.

**Acceptance Scenarios**:

1. **Given** una variable publicada, **When** el Administrador abre su pantalla, **Then** los campos clínicos están bloqueados y puede corregir sólo el enunciado por defecto y la descripción.
2. **Given** una variable publicada, **When** elige "Crear a partir de esta", **Then** se abre una variable nueva con la misma definición, un código libre sugerido, el nombre vacío y la opción "Dar de baja «Peso» al publicar" marcada.
3. **Given** la nueva con el mismo nombre que la anterior, **When** la publica, **Then** se rechaza.
4. **Given** la nueva sin ningún cambio clínico, **When** la publica, **Then** se rechaza.
5. **Given** la nueva con el mismo concepto SNOMED y la baja marcada, **When** la publica, **Then** se publica y la anterior queda de baja en la misma operación.
6. **Given** la nueva con el mismo concepto SNOMED y la baja desmarcada, **When** la publica, **Then** se rechaza: dos variables activas no pueden compartir concepto.

### User Story 2 - Pasar los cuestionarios a la variable nueva (Priority: P1)

**Acceptance Scenarios**:

1. **Given** un cuestionario con una pregunta de "Peso" y "Peso pediátrico" publicada, **When** el Jefe abre el editor, **Then** ve un aviso con un botón "Reemplazar" por pregunta.
2. **When** reemplaza, **Then** la pregunta toma la definición de la nueva con un linkId nuevo, conserva enunciado y obligatoriedad, y las condiciones que la miraban se re-apuntan.
3. Las versiones publicadas del cuestionario no cambian hasta que el Jefe publique otra.

### User Story 3 - Reportes y evolución sin mezclar datos (Priority: P1)

**Acceptance Scenarios**:

1. **Given** fichas cargadas con la anterior y con la nueva, **When** se arma un reporte, **Then** son dos columnas: la anterior marcada "(reemplazada por …)".
2. Si las dos columnas quedan con el mismo texto, se distinguen por el nombre de la variable.
3. Lo mismo vale para la exportación, las estadísticas y la evolución del paciente.

### Edge Cases

- Reactivar la anterior mientras la nueva usa su concepto o su nombre: se rechaza.
- Variables con varias versiones de antes de esta feature: quedan con la definición de su versión vigente. Las preguntas de borradores armadas con una versión anterior se actualizan al migrar y su cuestionario queda con cambios sin publicar; las versiones publicadas del cuestionario no cambian.
- Un borrador viejo con cambios clínicos sin publicar: la pantalla muestra lo publicado y "Crear a partir de esta" parte de lo publicado.
- Fichas pendientes que se pasan a la versión nueva del cuestionario: la respuesta de la pregunta reemplazada se descarta, igual que con cualquier pregunta que se quita.
- Clonar un cuestionario con una variable reemplazada: se rechaza nombrando la variable nueva.

## Requirements *(mandatory)*

- **FR-001**: Una variable publicada MUST rechazar cambios en tipo, repetición, unidad, rango, largo, formatos, opciones, concepto SNOMED, término, código y nombre.
- **FR-002**: El enunciado por defecto y la descripción MUST poder corregirse en el lugar, sin versión nueva.
- **FR-003**: "Crear a partir de esta" MUST precargar la definición publicada, sugerir un código libre y registrar a qué variable reemplaza.
- **FR-004**: Publicar una variable que reemplaza a otra MUST exigir nombre distinto y algún cambio clínico.
- **FR-005**: El concepto SNOMED y el nombre MUST ser únicos entre las variables activas y publicadas.
- **FR-006**: Si se pidió, publicar la nueva MUST dar de baja la anterior en la misma transacción.
- **FR-007**: El editor de cuestionarios MUST avisar las preguntas cuya variable fue reemplazada y permitir reemplazarlas una por una.
- **FR-008**: Reportes, exportación, estadísticas y evolución MUST mostrar la anterior y la nueva como columnas o series separadas y distinguibles.
- **FR-009**: Crear a partir de otra, publicar, dar de baja y reactivar MUST auditarse.
- **FR-010**: Las variables MUST NOT tener versiones: una publicada se identifica por `PublishedAt`.
