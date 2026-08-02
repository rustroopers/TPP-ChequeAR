# Ajustes al documento de la tesis

**Estado**: insumo listo, **aplicación manual** — el documento no vive en el repositorio.

Qué corregir en el capítulo de arquitectura, ahora que el código sostiene lo que afirma.

---

## 1. Error conceptual (corregir sí o sí)

> «Las respuestas se entregarán en formato `application/fhir+json`, lo que asegura
> compatibilidad con cualquier sistema del **ecosa .NET**»

Dos problemas. El typo, y algo de fondo: `application/fhir+json` asegura compatibilidad con el
**ecosistema FHIR / de salud**, no con .NET. La compatibilidad con .NET viene del framework
elegido, no del tipo de contenido. Son dos argumentos distintos y mezclarlos debilita los dos.

**Reemplazo sugerido**:

> Las respuestas se entregarán en formato `application/fhir+json`, lo que asegura
> compatibilidad con cualquier sistema del ecosistema FHIR —servidores de terminología,
> visores clínicos, herramientas de validación— sin necesidad de documentación propietaria.

---

## 2. Acotar el alcance de la interfaz FHIR (evita una pregunta incómoda)

El párrafo dice «cada recurso clínico del estándar —pacientes, observaciones, turnos, entre
otros— tiene una representación natural como endpoint HTTP». Es cierto del estándar, pero
sugiere que el sistema los expone todos.

**Agregar**:

> La interfaz expone como recursos FHIR aquellos sobre los que el sistema tiene autoridad:
> `Questionnaire`, `QuestionnaireResponse` y `AuditEvent`. `Patient` y `Encounter` se exponen
> en modo de sólo lectura, derivados de la fuente hospitalaria o de las propias fichas, porque
> el sistema no es dueño de esas entidades. `Observation` se **consume** del servidor FHIR del
> hospital y se declara explícitamente como no soportada en el `CapabilityStatement`. Las
> funciones internas —autenticación, reportes, gestión de usuarios, adjuntos— se exponen como
> REST propio: no tienen recurso natural en el estándar y forzarlas sería peor que dejarlas
> afuera.

Declarar lo que **no** se soporta es más fuerte que omitirlo: demuestra que la decisión fue
tomada, no olvidada.

---

## 3. Reclamar lo que el código realmente hace (la sección de capas se queda corta)

«Package by Layer» describe bien la organización por rol técnico, pero el proyecto hace algo
más fuerte y el documento no lo dice.

**Agregar tras la descripción de las capas**:

> Las capas no son sólo carpetas: son ensamblados independientes, y la dependencia hacia la
> infraestructura está **invertida**. Los repositorios implementan interfaces declaradas en la
> capa de negocio, de modo que ésta no conoce —ni puede conocer— la implementación de
> persistencia. El aislamiento deja de ser una convención documentada y pasa a ser una
> garantía del compilador.
>
> El proyecto de pruebas referencia únicamente las capas de dominio y de negocio: no compila
> contra infraestructura ni contra presentación. Esa restricción es deliberada y es lo que
> hace verificable la afirmación sobre testing independiente — una prueba que necesitara la
> base de datos simplemente no compilaría.

---

## 4. Evidencia concreta para la defensa

Tres hechos verificables que valen más que una afirmación:

**a) La API pasa el validador oficial.** Cualquier recurso devuelto por `/fhir/r5` se puede
pegar en <https://validator.fhir.org/> y valida contra R5. Es una verificación externa,
independiente y reproducible frente a un jurado.

**b) La validación real encontró un defecto que llevaba tiempo produciendo datos inválidos.**
El sistema declaraba `fhirVersion: 5.0.0` y emitía vocabulario de R4 (`choice` en lugar de
`coding`), porque el validador era un sustituto que sólo verificaba la presencia de la cadena
`"resourceType"`. Nueve de las veinte versiones publicadas en la base de desarrollo estaban
afectadas.

Es un buen argumento sobre **por qué** la validación importa: no como mejora cosmética, sino
como lo que corta la acumulación silenciosa de datos inválidos. Y muestra criterio de
ingeniería que el defecto lo haya encontrado una medición sobre datos reales y no la suite de
pruebas — que estaba verde, porque codificaba el mismo error.

**c) Las afirmaciones de arquitectura son ejecutables.** `LayerDependencyTests` verifica en
cada build que la capa de negocio no referencia el motor de base de datos, que el dominio no
depende de nada fuera del framework y que las pruebas no ven infraestructura. Romper la
arquitectura rompe el build.

---

## 5. Una limitación que conviene declarar antes de que la pregunten

No existe validador de perfiles de Firely para .NET Framework: el motor se separó a paquetes
que exigen `netstandard2.1`. La validación combina el parser estricto del SDK —que sí verifica
conformidad estructural con R5— con las reglas de los perfiles implementadas explícitamente.

**Formulación honesta**:

> La validación estructural contra R5 la realiza el SDK oficial. Las restricciones adicionales
> que el sistema declara en sus perfiles se verifican mediante reglas implementadas en la capa
> de negocio, dado que el motor de interpretación automática de perfiles no está disponible
> para la versión de la plataforma acordada con el hospital. La equivalencia entre reglas y
> perfiles se sostiene por convención documentada y se contrasta contra el validador oficial
> de HL7 como parte del procedimiento de verificación.

Declararlo es mejor que omitirlo: si alguien pregunta «¿valida contra el perfil o contra
reglas escritas a mano?», conviene que la respuesta ya esté en el texto.
