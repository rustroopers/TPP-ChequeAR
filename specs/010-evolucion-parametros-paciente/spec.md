# Feature Specification: Evolución longitudinal de parámetros por paciente

**Feature Branch**: `010-evolucion-parametros-paciente`

**Created**: 2026-08-10

**Status**: Implementada

**Input**: User description: "Evolución longitudinal de parámetros por paciente (gráficos de tendencia): desde el historial de un paciente, elegir un cuestionario y uno o más parámetros numéricos/fecha de ese cuestionario para ver su evolución en el tiempo a través de las fichas finalizadas del paciente, un gráfico por parámetro, con franja de rango normal, sin límite de parámetros seleccionables, mismo cuestionario únicamente, mismos permisos que el historial del paciente hoy."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ver la evolución de un parámetro clínico (Priority: P1)

Un profesional de la salud está revisando el historial de un paciente y quiere entender cómo evolucionó un valor puntual (por ejemplo, un peso o un resultado de laboratorio) a lo largo de varias consultas, sin tener que abrir ficha por ficha y comparar mentalmente los valores.

**Why this priority**: Es el valor central de la feature — sin esto no hay evolución que mostrar. Resuelve directamente el problema de que hoy la única forma de ver un dato histórico es abrir cada ficha finalizada una por una.

**Independent Test**: Puede probarse entrando al historial de un paciente con al menos dos fichas finalizadas del mismo cuestionario, eligiendo ese cuestionario y un parámetro numérico respondido en ambas, y verificando que se muestra un gráfico con los dos valores ubicados en sus fechas correspondientes.

**Acceptance Scenarios**:

1. **Given** un paciente con tres fichas finalizadas del mismo cuestionario, cada una con un valor distinto cargado para el mismo parámetro numérico, **When** el profesional elige ese cuestionario y ese parámetro, **Then** el sistema muestra un gráfico con los tres valores ordenados cronológicamente por fecha de finalización de cada ficha.
2. **Given** un paciente con una sola ficha finalizada de un cuestionario, **When** el profesional elige un parámetro de esa ficha, **Then** el sistema muestra el único punto disponible en vez de un error o un gráfico vacío.
3. **Given** un paciente sin ninguna ficha finalizada del cuestionario elegido, **When** el profesional intenta ver la evolución, **Then** el sistema indica claramente que no hay datos suficientes para graficar.

---

### User Story 2 - Comparar varios parámetros a la vez (Priority: P2)

El profesional quiere ver, uno debajo del otro y alineados por fecha, la evolución de varios parámetros del mismo cuestionario a la vez (por ejemplo, peso y un resultado de laboratorio), para detectar patrones que aparecen al mirarlos en conjunto.

**Why this priority**: Extiende el valor de la User Story 1 sin ser indispensable para el primer uso — se puede lanzar el MVP con selección de un solo parámetro y esta historia agrega la comparación.

**Independent Test**: Puede probarse seleccionando dos o más parámetros numéricos del mismo cuestionario para el mismo paciente y verificando que aparece un gráfico independiente por cada uno, todos compartiendo el mismo eje de fechas.

**Acceptance Scenarios**:

1. **Given** un paciente con fichas finalizadas que incluyen valores para dos parámetros distintos, **When** el profesional selecciona ambos parámetros, **Then** el sistema muestra dos gráficos separados, uno por parámetro, alineados verticalmente sobre el mismo rango de fechas.
2. **Given** un paciente con fichas finalizadas de un cuestionario con varios parámetros numéricos, **When** el profesional selecciona más de cuatro parámetros a la vez, **Then** el sistema los muestra todos, sin imponer un tope de cantidad.

---

### User Story 3 - Distinguir valores dentro o fuera de lo esperado (Priority: P3)

El profesional quiere, al mirar la evolución de un parámetro, ubicar de un vistazo si los valores históricos estuvieron dentro del rango considerado normal para esa pregunta, sin tener que recordar de memoria cuál es ese rango.

**Why this priority**: Agrega valor clínico al gráfico pero no es indispensable para que la comparación temporal (User Stories 1 y 2) ya sea útil por sí sola.

**Independent Test**: Puede probarse graficando un parámetro cuya pregunta tiene un rango normal configurado, y verificando que el gráfico muestra una franja de referencia con ese rango superpuesta a los valores.

**Acceptance Scenarios**:

1. **Given** un parámetro cuya pregunta tiene definido un rango normal (mínimo y máximo), **When** se grafica su evolución, **Then** el gráfico muestra una franja visual con ese rango de referencia.
2. **Given** un parámetro cuya pregunta no tiene rango normal configurado, **When** se grafica su evolución, **Then** el gráfico se muestra igual, sin la franja, sin generar error.

---

### Edge Cases

- ¿Qué pasa si el profesional elige un cuestionario que el paciente nunca completó? El sistema debe mostrar que no hay fichas finalizadas disponibles, sin permitir seleccionar parámetros.
- ¿Qué pasa con una pregunta que en algunas fichas fue respondida y en otras no? Los puntos faltantes simplemente no aparecen en el gráfico de ese parámetro; el resto de los puntos se muestra igual.
- ¿Qué pasa si dos fichas finalizadas del mismo cuestionario quedaron con la misma fecha de finalización? Ambos puntos se muestran en el gráfico en esa misma posición temporal, sin que uno reemplace al otro.
- ¿Qué pasa con las fichas que están "en progreso" o completadas parcialmente? No se incluyen en el gráfico bajo ninguna circunstancia — solo cuentan las fichas finalizadas.
- ¿Qué pasa con una pregunta marcada como repetible (que permite varios valores dentro de la misma ficha)? No aparece en la lista de parámetros seleccionables, para evitar ambigüedad sobre qué valor graficar en esa fecha.
- ¿Qué pasa si el cuestionario elegido no tiene ninguna pregunta numérica o de fecha? La lista de parámetros seleccionables queda vacía y el sistema lo indica.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: El sistema DEBE permitir, desde el historial de un paciente, elegir un cuestionario entre aquellos que el paciente completó al menos una vez.
- **FR-002**: El sistema DEBE listar, para el cuestionario elegido, las preguntas de valor único de tipo numérico o fecha disponibles para graficar.
- **FR-003**: El sistema DEBE excluir de esa lista las preguntas que permiten múltiples valores dentro de una misma ficha (preguntas repetibles).
- **FR-004**: El sistema DEBE permitir seleccionar uno o más parámetros de esa lista, sin límite en la cantidad de parámetros seleccionables a la vez.
- **FR-005**: El sistema DEBE mostrar un gráfico de evolución independiente por cada parámetro seleccionado.
- **FR-006**: El sistema DEBE alinear todos los gráficos mostrados sobre el mismo eje temporal, de modo que puedan leerse en conjunto.
- **FR-007**: El sistema DEBE incluir en cada gráfico únicamente valores provenientes de fichas finalizadas del paciente; fichas en progreso o con guardado parcial no deben aparecer.
- **FR-008**: El sistema DEBE ordenar los puntos de cada gráfico cronológicamente según la fecha de finalización de la ficha de la que provienen.
- **FR-009**: El sistema DEBE mostrar, para cada parámetro cuya pregunta tenga configurado un rango normal (mínimo y/o máximo), una franja de referencia visual con ese rango dentro de su gráfico.
- **FR-010**: El sistema DEBE mostrar el gráfico igualmente para parámetros sin rango normal configurado, simplemente sin la franja de referencia.
- **FR-011**: El sistema DEBE indicar de forma clara cuándo no hay datos suficientes para graficar un cuestionario o un parámetro elegido, en vez de mostrar un gráfico vacío sin explicación.
- **FR-012**: El sistema DEBE restringir el acceso a esta funcionalidad al mismo conjunto de roles que hoy puede acceder al historial del paciente, sin agregar restricciones adicionales.
- **FR-013**: El sistema NO DEBE permitir combinar en una misma vista parámetros de cuestionarios distintos ni comparar la evolución de más de un paciente a la vez.

### Key Entities

- **Ficha finalizada**: una respuesta completa a un cuestionario, asociada a un paciente y a una fecha de finalización; es la unidad que aporta cada punto a los gráficos.
- **Parámetro (pregunta)**: un campo de valor único, numérico o de fecha, definido dentro de un cuestionario, que puede tener opcionalmente un rango normal (mínimo/máximo) asociado.
- **Rango normal**: el mínimo y/o máximo considerado esperable para un parámetro, definido a nivel de la pregunta; se usa solo como referencia visual, no bloquea ni valida nada en esta feature.
- **Paciente**: sujeto sobre el que se consulta la evolución; el alcance de esta feature es siempre un único paciente por vez.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un profesional puede llegar a ver la evolución de un parámetro de un paciente en 3 pasos o menos desde el historial del paciente (elegir cuestionario, elegir parámetro(s), ver gráfico).
- **SC-002**: El sistema muestra correctamente la evolución con al menos 5 parámetros seleccionados a la vez, sin que la información se vuelva ilegible o se corte.
- **SC-003**: El 100% de los gráficos generados reflejan exclusivamente fichas finalizadas — ningún dato de una ficha en progreso o parcial aparece en un gráfico.
- **SC-004**: El 100% de los parámetros con rango normal configurado muestran su franja de referencia correctamente en el gráfico.
- **SC-005**: Un profesional que no encuentra datos para graficar entiende, sin ambigüedad, que la causa es la falta de fichas finalizadas (no un error del sistema).

## Assumptions

- La comparación es siempre dentro de un mismo cuestionario; comparar parámetros de cuestionarios distintos o entre pacientes distintos queda fuera de alcance de esta primera versión.
- El rango normal mostrado es el configurado actualmente en la pregunta; no se versiona un rango histórico distinto por cada versión pasada del cuestionario.
- Los roles que hoy pueden ver el historial de un paciente son los mismos que pueden ver esta evolución; no se introduce ningún esquema de permisos nuevo (por ejemplo, no se restringe por "médico tratante").
- Las preguntas repetibles no tienen hoy uso real en los cuestionarios existentes del sistema, por lo que excluirlas de la selección no reduce cobertura sobre casos de uso actuales.
- Esta feature depende de que existan fichas finalizadas (no solo en progreso) para poder mostrar cualquier evolución.
