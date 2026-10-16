# Feature Specification: Conformidad FHIR real y saneamiento de capas

**Feature Branch**: `009-conformidad-fhir-capas`

**Created**: 2026-08-02

**Status**: Implementada

**Input**: User description: "a partir del plan, hagamoslo con speckit para resolverlo" — derivado del análisis de conformidad entre el documento de arquitectura de la tesis y el código real.

## Contexto

El documento de arquitectura del proyecto afirma dos cosas que hoy el sistema no sostiene del
todo. Esta feature existe para que ambas pasen de ser declaraciones a ser **verificables
ejecutando el sistema**.

**Afirmación 1 — "La interfaz del sistema se expondrá como una API REST construida sobre el
estándar HL7 FHIR; las respuestas se entregarán en formato `application/fhir+json`".**
Hoy sólo la fachada es FHIR: las direcciones de los endpoints tienen forma de recurso FHIR,
pero el contenido que viaja es la representación interna del sistema. Un consumidor externo
—el sistema hospitalario, o cualquier herramienta del ecosistema FHIR— no puede interpretar
las respuestas. El recurso FHIR bien formado sí se construye, pero solamente al publicar una
versión de un cuestionario, y queda guardado en la base sin exponerse nunca. Además, los
errores se devuelven en un formato propio en lugar del recurso que el estándar define para
eso, las colecciones de resultados no respetan la estructura obligatoria, la validación
declarada es un sustituto provisorio que sólo verifica que el contenido no esté vacío, y el
perfil de conformidad que el sistema dice cumplir **no existe como artefacto publicado**.

**Afirmación 2 — "Organización interna en capas con responsabilidades bien delimitadas y
comunicación en una dirección única, lo que evita que modificaciones en la infraestructura o
en la persistencia afecten la lógica de negocio; facilita el testing independiente".**
Esto se cumple en lo estructural —las capas existen, el grafo de dependencias es acíclico, la
capa de dominio no depende de nada, y la suite de pruebas no puede siquiera compilar contra
la infraestructura— pero hay tres desviaciones concretas: la capa de lógica de negocio depende
directamente del motor de base de datos, la capa de presentación accede a la capa de datos
salteando la de negocio, y hay reglas de negocio que viven dentro de los controllers y por eso
no se pueden probar de forma aislada.

Esta feature **completa y cierra** lo que la spec 003 (Interoperabilidad FHIR) dejó en estado
de MVP, y corrige transversalmente la organización en capas.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Un sistema externo puede leer los datos clínicos (Priority: P1)

Un integrador del hospital, o cualquier herramienta estándar del ecosistema de salud, consulta
un cuestionario o una ficha clínica del sistema y recibe un recurso conforme al estándar, que
puede interpretar sin conocer nada de la implementación interna de ChequeAR.

**Why this priority**: Es la afirmación central del documento de arquitectura y el objetivo
declarado del proyecto. Sin esto, no hay interoperabilidad: sólo direcciones con nombres
sugerentes.

**Independent Test**: Consultar un cuestionario y una ficha, y pegar cada respuesta en el
validador oficial del estándar. Debe pasar sin errores **contra la especificación base**. No
requiere que ninguna otra historia esté implementada.

> La validación contra el **perfil de conformidad propio** es criterio de la historia 4, no de
> ésta: el perfil no existe como artefacto hasta entonces, y un validador externo no puede
> resolver una referencia a algo que todavía no se publicó. Separarlas evita leer como fallo de
> esta historia algo que simplemente todavía no se construyó.

**Acceptance Scenarios**:

1. **Given** un cuestionario existente en el sistema, **When** se lo consulta por su
   identificador, **Then** la respuesta es un recurso del estándar, con su tipo de recurso
   declarado y sus preguntas representadas como los elementos que el estándar define.
2. **Given** una consulta de búsqueda de cuestionarios, **When** se la ejecuta, **Then** la
   respuesta es una colección con la estructura que el estándar exige, donde cada resultado
   está envuelto como recurso y acompañado de su dirección completa.
3. **Given** cualquier consulta a la interfaz de interoperabilidad, **When** el consumidor no
   declara explícitamente qué formato acepta, **Then** la respuesta se entrega igualmente en el
   formato del estándar.
4. **Given** una ficha clínica completada, **When** se la consulta, **Then** las respuestas del
   paciente aparecen tipadas según la clase de pregunta y agrupadas correctamente cuando una
   misma pregunta admite varias respuestas.

---

### User Story 2 - Los cuestionarios se crean y editan en el formato del estándar (Priority: P1)

Quien diseña un cuestionario lo guarda y lo vuelve a abrir sin que se pierda ningún dato,
aunque la información viaje al servidor en el formato del estándar en lugar del formato interno
previo.

**Why this priority**: Sin escritura conforme, la interfaz sería de sólo lectura y el sistema
mantendría dos representaciones distintas de la misma información, que es exactamente el
problema que esta feature elimina. Además es el punto de mayor riesgo de pérdida de datos.

**Independent Test**: Crear un cuestionario con preguntas de todos los tipos soportados
—incluyendo una con lógica condicional, una escala, una con código clínico y su término
descriptivo, y una con opciones de respuesta—, guardarlo, recargar la pantalla y verificar que
todo volvió idéntico.

**Acceptance Scenarios**:

1. **Given** un cuestionario con preguntas de cada tipo soportado, **When** se lo guarda y se
   lo vuelve a abrir, **Then** ninguna propiedad se perdió: enunciado, tipo, obligatoriedad,
   opciones de respuesta, rangos, unidad, largo máximo, patrón, formatos aceptados, lógica
   condicional, código clínico y término descriptivo.
2. **Given** un cuestionario con secciones anidadas, **When** se lo guarda, **Then** la
   jerarquía de secciones y el orden de las preguntas se conservan.
3. **Given** dos personas editando el mismo cuestionario, **When** la segunda intenta guardar
   sobre una versión que ya cambió, **Then** el sistema rechaza la escritura en lugar de pisar
   el trabajo ajeno, indicando qué versión esperaba y cuál encontró.
4. **Given** una escritura sin indicar sobre qué versión se está trabajando, **When** se la
   intenta, **Then** el sistema la rechaza pidiendo esa información explícitamente.

---

### User Story 3 - Los errores son diagnosticables por un sistema externo (Priority: P1)

Cuando una operación falla, el consumidor recibe la información del error en el mismo formato
estándar que el resto de las respuestas, con severidad y código de categoría, en lugar de un
formato propio que ninguna herramienta externa sabe leer.

**Why this priority**: Forma parte de "ser FHIR". Una interfaz que responde bien en el camino
feliz pero inventa su propio formato de error no es interoperable, y es lo primero que rompe en
una integración real.

**Independent Test**: Provocar cada clase de error (recurso inexistente, conflicto de edición,
falta de precondición, regla de negocio violada, contenido inválido) y verificar que todas las
respuestas comparten la misma estructura estándar con la categoría correcta.

**Acceptance Scenarios**:

1. **Given** un identificador que no existe, **When** se consulta ese recurso, **Then** la
   respuesta es el recurso de error del estándar con la categoría "no encontrado".
2. **Given** una operación que viola una regla de negocio (publicar algo sin preguntas, editar
   algo archivado, responder un cuestionario retirado), **When** se la intenta, **Then** la
   respuesta es el recurso de error del estándar, con un texto de diagnóstico legible por una
   persona.
3. **Given** un error de cualquier tipo, **When** se lo recibe, **Then** las pantallas del
   sistema muestran el texto de diagnóstico correctamente, sin degradarse a mensajes genéricos.

---

### User Story 4 - Lo publicado cumple el perfil de conformidad declarado (Priority: P2)

Al publicar una versión de un cuestionario, el sistema la valida contra un perfil de
conformidad publicado y real, y rechaza lo que no cumple, en lugar de aceptar cualquier
contenido no vacío.

**Why this priority**: El sistema hoy **declara** cumplir un perfil que no existe como
artefacto. Es la afirmación más frágil del documento: cualquiera puede pedir ver el perfil y no
hay nada que mostrar. Va después de las tres primeras porque sin recursos conformes no hay nada
válido que validar.

**Independent Test**: Intentar publicar un cuestionario que viola el perfil (identificadores de
pregunta duplicados, sin especialidad asignada) y verificar que se rechaza con el detalle de
qué regla incumple; publicar uno correcto y verificar que pasa.

**Acceptance Scenarios**:

1. **Given** un cuestionario con dos preguntas que comparten identificador, **When** se intenta
   publicarlo, **Then** se rechaza indicando cuál es la regla incumplida.
2. **Given** un cuestionario sin especialidad asignada, **When** se intenta publicarlo, **Then**
   se rechaza.
3. **Given** un cuestionario correcto, **When** se lo publica, **Then** la versión queda
   congelada y su contenido pasa el validador oficial del estándar contra el perfil declarado.
4. **Given** el perfil de conformidad, **When** alguien lo solicita, **Then** existe como
   artefacto versionado dentro del proyecto.

---

### User Story 5 - Pacientes y encuentros se consultan como recursos del estándar (Priority: P2)

Un consumidor externo obtiene los pacientes y sus encuentros a través de la misma interfaz
estándar que el resto, sin necesitar conocer un formato propio distinto.

**Why this priority**: El documento de arquitectura nombra explícitamente "pacientes,
observaciones, turnos" como ejemplos de recursos con representación natural. Hoy los pacientes
se exponen en un formato propio. Es P2 porque el sistema no es dueño de esos datos: los deriva.

**Independent Test**: Buscar un paciente y listar sus encuentros por la interfaz estándar, y
validar ambas respuestas contra el validador oficial.

**Acceptance Scenarios**:

1. **Given** un paciente conocido por el sistema, **When** se lo busca por texto libre,
   **Then** se devuelve una colección conforme de recursos de paciente con su identificador de
   historia clínica.
2. **Given** un paciente con fichas cargadas, **When** se consultan sus encuentros, **Then** se
   devuelve una colección conforme derivada de esas fichas.
3. **Given** que la fuente de datos del hospital no responde, **When** se busca un paciente,
   **Then** el sistema sigue devolviendo los pacientes que puede derivar de sus propios datos,
   sin inventar información.
4. **Given** un consumidor que consulta las capacidades del sistema, **When** lee la
   declaración, **Then** encuentra listados exactamente los tipos de recurso soportados, y las
   observaciones aparecen declaradas como **no** soportadas.

---

### User Story 6 - La lógica de negocio no depende del motor de base de datos (Priority: P2)

Un cambio de motor, de versión de driver o de dialecto de consultas no obliga a tocar la capa
que contiene las reglas del negocio.

**Why this priority**: Es la desviación más citable respecto del documento de arquitectura,
porque contradice de frente una afirmación textual. No afecta lo que ve el usuario, por eso es
P2, pero es un requisito de la tesis, no una mejora opcional.

**Independent Test**: Verificar automáticamente que la capa de lógica de negocio no declara
ninguna dependencia sobre componentes de acceso a datos, y que la suite de pruebas de reportes
sigue pasando completa.

**Acceptance Scenarios**:

1. **Given** la capa de lógica de negocio, **When** se inspeccionan sus dependencias
   declaradas, **Then** no aparece ningún componente propio del motor de base de datos.
2. **Given** las pruebas existentes de construcción de consultas de reportes, **When** se las
   ejecuta después del cambio, **Then** todas pasan, incluida la que verifica que no se puede
   inyectar código en una consulta.
3. **Given** un reporte con filtros, rango de fechas y paginación, **When** se lo previsualiza,
   se lo exporta y se calculan sus estadísticas, **Then** los resultados son los mismos que
   antes del cambio.
4. **Given** una regresión futura que reintroduzca esa dependencia, **When** se ejecuta la
   suite, **Then** falla una prueba explícita que la detecta.

---

### User Story 7 - Las reglas de negocio son probables de forma aislada (Priority: P3)

Las decisiones del negocio —quién puede modificar qué, cómo se filtra y pagina la auditoría,
cómo se arma el tablero de inicio— se pueden probar sin levantar la capa web.

**Why this priority**: Es lo que respalda la frase "facilita el testing independiente de cada
componente". Hoy esas reglas viven dentro de los controllers y por eso no tienen ninguna
prueba. Es P3 porque no cambia comportamiento observable: habilita cobertura.

**Independent Test**: Escribir pruebas para la autorización por propietario de las consultas
guardadas y para el filtrado y paginado de auditoría, sin instanciar nada de la capa web.

**Acceptance Scenarios**:

1. **Given** una consulta guardada por una persona, **When** otra intenta modificarla o
   eliminarla, **Then** la operación se rechaza — y ese comportamiento está cubierto por una
   prueba que no involucra la capa de presentación.
2. **Given** la capa de presentación, **When** se inspecciona qué declara necesitar, **Then**
   no aparece ningún componente de acceso a datos: sólo servicios de negocio.
3. **Given** una búsqueda de auditoría con filtros y paginación, **When** se la ejecuta,
   **Then** devuelve los mismos resultados que antes, con el total real de coincidencias.
4. **Given** el tablero de inicio, **When** se lo carga, **Then** muestra la misma información
   que antes y sigue degradándose sin datos inventados cuando la base no responde.

---

### Edge Cases

- **Versiones ya publicadas que no cumplirían el perfil nuevo**: las versiones congeladas son
  un registro histórico inmutable —hay fichas clínicas que las referencian— y no se
  revalidan ni se migran. El perfil aplica a publicaciones nuevas. Consultarlas debe seguir
  funcionando aunque no cumplan el perfil actual.
- **Una ficha en curso cuya versión de cuestionario fue reemplazada**: la ficha sigue anclada a
  la versión con la que se empezó a completar, no a la vigente.
- **Un cuestionario guardado antes de esta feature, sin término descriptivo en sus preguntas
  codificadas**: al reabrirlo no debe aparecer el campo vacío ni perderse el código.
- **Contenido almacenado ilegible o corrupto** (una versión congelada que no se puede
  interpretar): la pantalla debe degradarse con un aviso, no caerse.
- **La fuente de datos del hospital no responde**: pacientes y encuentros siguen derivándose de
  los datos propios; nunca se muestran datos de demostración.
- **El servidor de terminología clínica no está configurado o no responde**: el autocompletado
  de códigos avisa y el resto del sistema sigue funcionando.
- **Una pregunta con lógica condicional que apunta a una pregunta inexistente**: se debe
  detectar antes de publicar, no después.
- **Un reporte que supera el máximo exportable en línea**: se corta con un mensaje entendible
  en lugar de agotar la memoria.

## Requirements *(mandatory)*

### Functional Requirements

**Conformidad de la interfaz de interoperabilidad**

- **FR-001**: El sistema MUST devolver, en todos los endpoints de la interfaz de
  interoperabilidad, recursos conformes al estándar HL7 FHIR R5, y no representaciones internas.
- **FR-002**: El sistema MUST entregar esas respuestas en el formato de contenido que el
  estándar define, de forma predeterminada, sin depender de que el consumidor lo solicite.
- **FR-003**: El sistema MUST devolver las colecciones de resultados con la estructura de
  agrupación que el estándar exige, incluyendo la dirección completa de cada recurso y el total
  real de coincidencias.
- **FR-004**: El sistema MUST aceptar recursos conformes al estándar en las operaciones de
  creación y modificación.
- **FR-005**: El sistema MUST preservar, en el viaje de ida y vuelta entre su representación
  interna y la del estándar, toda la información de un cuestionario sin pérdida.
- **FR-006**: El sistema MUST expresar la información que no tiene representación nativa en el
  estándar mediante los mecanismos de extensión que el propio estándar provee, con
  identificadores propios y documentados.
- **FR-007**: El sistema MUST exponer la versión de cada registro mediante el mecanismo nativo
  del estándar, además del mecanismo de control de concurrencia ya existente.

**Errores**

- **FR-008**: El sistema MUST devolver todos los errores de la interfaz de interoperabilidad
  como el recurso que el estándar define para ese fin, con severidad, categoría y un texto de
  diagnóstico legible.
- **FR-009**: Las pantallas del sistema MUST mostrar el texto de diagnóstico de esos errores en
  lugar de mensajes genéricos.

**Validación y conformidad declarada**

- **FR-010**: El sistema MUST validar los recursos contra un perfil de conformidad real antes
  de congelar una versión publicada o de finalizar una ficha.
- **FR-011**: El proyecto MUST incluir los perfiles de conformidad como artefactos versionados,
  publicados bajo los identificadores que el sistema declara cumplir.
- **FR-012**: El sistema MUST rechazar la publicación de contenido que no cumple el perfil,
  informando qué regla se incumple.
- **FR-013**: El sistema MUST declarar sus capacidades de forma que refleje exactamente los
  tipos de recurso y operaciones realmente soportados, incluyendo los explícitamente no
  soportados.
- **FR-014**: El sistema MUST seguir sirviendo las versiones publicadas antes de esta feature,
  aunque no cumplan el perfil vigente.

**Cobertura de recursos**

- **FR-015**: El sistema MUST exponer pacientes y encuentros como recursos del estándar, en
  modo de sólo lectura.
- **FR-016**: El sistema MUST conservar el comportamiento degradado actual: cuando la fuente
  hospitalaria no responde, los pacientes se derivan de los datos propios y nunca se muestran
  datos de demostración.
- **FR-017**: Las funciones internas sin recurso natural en el estándar (autenticación,
  reportes, usuarios, adjuntos, estado de integraciones) MAY permanecer fuera de la interfaz de
  interoperabilidad, y MUST quedar documentadas como tales.

**Organización en capas**

- **FR-018**: La capa de lógica de negocio MUST NOT declarar dependencias sobre componentes
  específicos del motor de base de datos.
- **FR-019**: La capa de presentación MUST NOT acceder directamente a la capa de acceso a
  datos; MUST hacerlo a través de la capa de lógica de negocio.
- **FR-020**: Las reglas de negocio MUST residir en la capa de lógica de negocio y no en los
  controllers. Esto incluye, como mínimo: la autorización por propietario de las consultas
  guardadas, el filtrado y paginado de auditoría, el armado del tablero de inicio y la
  interpretación de las respuestas del servidor de terminología.
- **FR-021**: La suite de pruebas MUST NOT depender de la capa de infraestructura ni de la de
  presentación para ejercitar las reglas de negocio.
- **FR-022**: El proyecto MUST incluir una prueba automática que falle si se reintroduce una
  dependencia de la capa de negocio hacia el motor de base de datos.

**No regresión**

- **FR-023**: El sistema MUST conservar el comportamiento observable existente en: control de
  concurrencia al editar, anclaje de una ficha a la versión con la que se completó, publicación
  y archivado, reportes (previsualización, exportación y estadísticas) y registro de auditoría.

### Key Entities

- **Cuestionario**: definición de un formulario clínico, con secciones, preguntas ordenadas,
  opciones de respuesta, reglas de validación, lógica condicional y codificación clínica. Tiene
  estado (borrador, activo, archivado) y una versión de registro para control de concurrencia.
- **Versión publicada de cuestionario**: instantánea inmutable del cuestionario en el momento de
  publicarse. Es lo que responden los clínicos y lo que referencian las fichas. Nunca se
  modifica retroactivamente.
- **Ficha clínica (respuesta a cuestionario)**: conjunto de respuestas de un paciente a una
  versión publicada específica, con autor, momento de creación y estado (en curso, completada).
- **Paciente**: persona atendida. El sistema **no es dueño** de esta entidad: la deriva de la
  fuente hospitalaria o, en modo degradado, de sus propias fichas.
- **Encuentro**: instancia de atención, derivada de las fichas cargadas para un paciente.
- **Evento de auditoría**: registro de quién hizo qué sobre qué recurso y cuándo.
- **Perfil de conformidad**: artefacto que declara qué restricciones adicionales impone el
  sistema sobre los recursos del estándar. Nuevo en esta feature.
- **Consulta guardada**: definición de reporte reutilizable, con dueño.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: El 100% de los recursos devueltos por la interfaz de interoperabilidad pasa el
  validador oficial del estándar sin errores.
- **SC-002**: Un integrador externo puede consultar cuestionarios, fichas, pacientes y
  encuentros usando exclusivamente herramientas estándar del ecosistema de salud, sin
  documentación propietaria del sistema.
- **SC-003**: El 100% de las respuestas de error de la interfaz de interoperabilidad usa el
  formato estándar, verificable recorriendo cada categoría de error.
- **SC-004**: Un cuestionario con preguntas de los 11 tipos soportados se guarda y se reabre sin
  perder ninguna propiedad — 0 campos perdidos en el ciclo completo.
- **SC-005**: El perfil de conformidad existe como artefacto consultable, y publicar contenido
  que lo viola falla el 100% de las veces con el detalle de la regla incumplida.
- **SC-006**: La capa de lógica de negocio declara 0 dependencias sobre el motor de base de
  datos, verificado automáticamente por una prueba que corre en cada build.
- **SC-007**: La capa de presentación declara 0 accesos directos a la capa de datos.
- **SC-008**: Las reglas de negocio hoy sin cobertura (autorización por propietario, filtrado de
  auditoría) quedan cubiertas por pruebas que no involucran la capa web.
- **SC-009**: La totalidad de las pruebas existentes sigue pasando, incluida la que verifica la
  imposibilidad de inyectar código en las consultas de reportes.
- **SC-010**: Los flujos de usuario existentes —crear, editar, publicar y archivar un
  cuestionario; completar y finalizar una ficha; construir, previsualizar y exportar un
  reporte— siguen funcionando sin cambios visibles para quien los usa.

## Assumptions

- **Las versiones ya publicadas no se migran ni se revalidan.** Son un registro histórico
  inmutable referenciado por fichas clínicas existentes; reescribirlas rompería la trazabilidad
  que el sistema garantiza. El perfil de conformidad aplica a publicaciones nuevas.
- **No existen todavía consumidores externos de la interfaz.** La integración con el hospital
  es hoy de entrada (el sistema consume datos del hospital, no al revés), por lo que no se
  requiere una capa de compatibilidad hacia atrás ni versionado de la interfaz. El único
  consumidor actual son las propias pantallas del sistema, que se adaptan como parte de esta
  feature.
- **La versión del estándar es HL7 FHIR R5**, la misma que el sistema ya declara.
- **El único sistema de codificación clínica soportado sigue siendo SNOMED CT**, y el
  autocompletado se resuelve del lado del servidor como hasta ahora.
- **Las observaciones quedan fuera de alcance como recurso expuesto**: el sistema no las
  produce, sólo las consume del hospital cuando precarga datos. Se declaran explícitamente
  como no soportadas.
- **Las funciones internas siguen fuera de la interfaz estándar** (autenticación, reportes,
  usuarios, adjuntos, estado de integraciones). Los adjuntos, en particular, deben seguir
  accesibles porque las fichas los referencian por dirección.
- **La organización de las pantallas no cambia.** Esta feature altera el formato de lo que
  viaja entre pantalla y servidor, no el diseño ni los flujos de la interfaz de usuario.
- **La hidratación de datos que el servidor hace al renderizar las pantallas no forma parte de
  la interfaz de interoperabilidad** y no necesita adoptar el formato del estándar.

## Dependencies

- Completa y cierra la spec **003 — Interoperabilidad FHIR**, que dejó la validación y los
  perfiles como trabajo pendiente de fases posteriores.
- Toca superficie definida en **001 (gestor de cuestionarios)**, **002 (motor de renderizado)**
  y **004 (análisis y exportación)**: los criterios de no regresión de FR-023 cubren esas tres.
- Requiere que exista un validador de conformidad capaz de evaluar recursos contra perfiles,
  y las definiciones oficiales del estándar contra las cuales validar.
- La verificación externa (SC-001) depende del validador oficial público del estándar.

## Out of Scope

- Migrar el sistema a una versión más moderna de la plataforma de ejecución.
- Exponer observaciones como recurso propio del sistema.
- Reescribir el diseño o los flujos de las pantallas.
- Convertir las funciones internas (autenticación, reportes, usuarios, adjuntos) a recursos del
  estándar.
- Reemplazar el motor de base de datos, o volver la capa de negocio agnóstica del **dialecto**
  de consultas: el alcance es eliminar la dependencia sobre los **componentes** del motor.
- Sustituir el mecanismo de inyección de dependencias propio por una biblioteca de terceros.
