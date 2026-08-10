# Phase 0 — Research: Conformidad FHIR real y saneamiento de capas

**Feature**: `009-conformidad-fhir-capas` | **Fecha**: 2026-08-02

Todas las incógnitas técnicas del Technical Context se resolvieron. No queda ningún
`NEEDS CLARIFICATION`.

---

## R1 — SDK FHIR compatible con la plataforma de ejecución actual

**Incógnita**: ¿existe un SDK de manipulación de recursos FHIR R5 utilizable desde
.NET Framework 4.7.1? Es la restricción dominante: si la respuesta es no, toda la feature
cambia de forma.

**Decisión**: **Firely SDK, `Hl7.Fhir.R5` versión 5.13.4, pinneada.**

**Rationale**:

- Verificado en NuGet: la línea **6.x targetea `netstandard2.1` y `net8.0`** — no consumible
  desde `net471`. La nota oficial del paquete lo dice explícitamente: *"as of version 6.0 the
  minimum target framework is netstandard2.1. If you need netstandard2.0 support, please
  continue to use the 5.x version of the SDK."*
- **5.13.4 sí publica un grupo de dependencias `netstandard2.0`**, que `net471` consume.
  Es la última versión de esa línea.
- Cadena de dependencias verificada: `Hl7.Fhir.R5` → `Hl7.Fhir.Conformance` → `Hl7.Fhir.Base`,
  y este último trae `Fhir.Metrics`, `Newtonsoft.Json` (≥13.0.4), `System.Buffers`,
  `System.ComponentModel.Annotations`, `System.Reflection.Emit.Lightweight` y
  **`System.Text.Json` ≥ 9.0.1**.
- Para validación contra perfiles: `Hl7.Fhir.Specification.R5` + `Hl7.Fhir.Specification.Data.R5`
  (este último contiene el `specification.zip` con las definiciones oficiales, necesario para
  el resolver `ZipSource`).

**Riesgo asociado y mitigación**: `System.Text.Json 9` sobre .NET Framework exige binding
redirects. **A favor**: `ChequeAR.Api/Web.config` ya declara todo el stack de shims
`netstandard` (líneas 129-158): `System.Memory`, `System.Buffers`,
`System.Runtime.CompilerServices.Unsafe`, `System.Text.Json` (hoy en `6.0.0.10`),
`System.Text.Encodings.Web`, `System.Numerics.Vectors`, `System.Threading.Tasks.Extensions`,
`System.ValueTuple`. El trabajo es **subir versiones**, no introducir el mecanismo.
La versión de assembly real debe leerse del DLL tras el `restore`, no asumirse del número de
paquete.

**Alternativas consideradas**:

| Alternativa | Rechazada porque |
|---|---|
| Migrar el proyecto a .NET 8 y usar Firely 6.x | Reescritura completa de ASP.NET Web API 2 + MVC 5 + Razor + OWIN. Fuera de alcance, y el documento de arquitectura justifica la plataforma actual por la experiencia del departamento de sistemas del hospital. |
| Seguir con construcción manual de JSON y sumar sólo un validador | Deja dos representaciones de FHIR en el repositorio y no da tipado. El usuario eligió explícitamente reemplazo total. |
| `Hl7.Fhir.R4` en lugar de R5 | El sistema ya declara `fhirVersion: 5.0.0`; degradar la versión del estándar sería una regresión de conformidad. |

**Fallback documentado**: si el stack de `System.Text.Json 9` resulta inmanejable en `net471`,
bajar a `Hl7.Fhir.R5` 5.9.x, que arrastra `System.Text.Json` 8.x. Se pierde nada funcional
relevante para esta feature.

### Resultado real de la compuerta (T001-T006, ejecutada 2026-08-02)

**PASA.** El SDK carga, serializa y parsea sobre .NET Framework 4.7.1. **No hizo falta el
fallback.** Verificado con `FhirSdkSmokeTests`, 106/106 pruebas en verde, y la aplicación
respondiendo 200 en `/`, `/Questionnaires` y `/fhir/r5/metadata`.

Aparecieron **dos bloqueantes que este research no había anticipado**. Ninguno es evidente
leyendo la documentación del SDK, y los dos habrían costado caro de diagnosticar más adelante:

**1. `Newtonsoft.Json` 13.0.3 → 13.0.4 (obligatorio).** `Hl7.Fhir.Base` exige `>= 13.0.4`; con
13.0.3 pinneado el `restore` falla con `NU1605` (degradación de paquete tratada como error) y
**ningún proyecto restaura**. Afecta a `ChequeAR.Application` y `ChequeAR.Api`, que lo declaran
directamente. Sin impacto en los binding redirects: la versión de assembly sigue siendo
`13.0.0.0` en toda la línea 13.0.x.

**2. Referencia explícita al facade `netstandard` en `Web.config` (obligatorio).** Éste es el
caro. Las vistas Razor se compilan **en runtime**, con un conjunto de referencias propio e
independiente del `.csproj`. Al entrar dependencias `netstandard2.0`, el compilador de vistas
no puede resolver ni `System.Object` y falla con:

```
_Layout.cshtml(25): error CS0012: El tipo 'System.Object' está definido en un ensamblado
al que no se hace referencia. Debe agregar una referencia al ensamblado 'netstandard,
Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'.
```

El síntoma es engañoso: **500 con cuerpo vacío en toda la aplicación MVC**, mientras los
endpoints de Web API siguen respondiendo 200 — porque ésos no pasan por la compilación de
vistas. Sin `customErrors mode="Off"` y sin mirar el registro de eventos de Windows, no hay
ninguna pista de la causa. La solución es declarar el facade en
`<system.web><compilation><assemblies>`.

**Binding redirects que efectivamente cambiaron** (verificados contra la versión de assembly
real de los DLL en `bin/`, no contra el número de paquete):

| Assembly | Antes | Ahora |
|---|---|---|
| `System.Text.Json` | 6.0.0.10 | **9.0.0.1** |
| `System.Text.Encodings.Web` | 6.0.0.0 | **9.0.0.1** |
| `Microsoft.Bcl.AsyncInterfaces` | *no existía* | **9.0.0.1** |
| `System.ComponentModel.Annotations` | *no existía* | **4.2.1.0** |

Los demás shims ya declarados (`System.Memory`, `System.Buffers`,
`System.Runtime.CompilerServices.Unsafe`, `System.Numerics.Vectors`,
`System.Threading.Tasks.Extensions`, `System.ValueTuple`) coincidían con `bin/` y no se
tocaron.

**Nota lateral**: el smoke test corrido desde PowerShell falla con
`ReflectionTypeLoadException` aunque la aplicación funcione. No es un problema real:
PowerShell no lee el `Web.config` de la app, así que corre sin redirecciones. Cualquier
verificación de runtime tiene que hacerse desde un proceso que sí las aplique — el proyecto de
pruebas (que genera las suyas automáticamente) o la aplicación bajo IIS.

**Descubrimiento de inventario**: la solución tiene **6 proyectos**, no 5. Existe
`tests/integration/MockHospital.Tests` fuera de `src/`, que referencia `ChequeAR.Application` y
`ChequeAR.Infrastructure`. Hay que tenerlo en cuenta al tocar esas capas (US6 y US7 en
particular).

---

## R2 — Estrategia de superficie: ¿API interna separada o superficie única FHIR?

**Incógnita**: hoy las pantallas consumen `/fhir/r5/*` esperando la representación interna.
Convertir esas rutas a FHIR real rompe la interfaz de usuario.

**Decisión**: **superficie única FHIR.** Las pantallas pasan a hablar FHIR nativo.
*(Decisión del usuario, tomada antes de esta spec.)*

**Rationale**: elimina la duplicación de representaciones. Una vez medido, el alcance real
resultó mucho menor de lo temido: **4 puntos de llamada y 2 constructores de contenido**, no
una reescritura de la interfaz. La razón es que el editor y el renderizador **se hidratan del
lado del servidor** vía `data-props` (un ViewModel de Razor, no una llamada a la API), y eso
no se toca.

Puntos de acoplamiento medidos:

| Archivo | Línea | Qué consume hoy |
|---|---|---|
| `Scripts/islands/questionnaire-editor/index.js` | 541, 607, 622, 633, 645-647, 726 | Envía representación interna; lee `id` / `concurrencyVersion`; lee `body.error` |
| `Scripts/islands/questionnaire-renderer/index.js` | ~300-360 | Envía `{ ValueBoolean, ValueInteger, ... }` |
| `Scripts/islands/report-builder/index.js` | 32-33, 93, 183 | Lee `q.Questions` plano; `data.entry` como entidades; estado numérico |
| `Views/Encounters/Search.cshtml` | 159 | Lee `entry[].QuestionnaireId` / `.AuthoredAt` |

**Alternativas consideradas**:

| Alternativa | Rechazada porque |
|---|---|
| API interna separada (`/api/questionnaires`) + fachada FHIR | Duplica representaciones y mantenimiento. Era la opción de menor riesgo, descartada por decisión explícita. |
| Negociación de contenido en la misma ruta | `/fhir/r5/*` seguiría sirviendo no-FHIR por omisión, que es justamente el problema que la feature elimina. Contradice FR-002. |

---

## R3 — Información sin representación nativa en el estándar

**Incógnita**: dos datos del dominio no tienen campo nativo en FHIR y hoy sobreviven porque
viaja la representación interna.

**Decisión**: **dos extensiones propias, documentadas y versionadas** (mecanismo estándar del
propio FHIR — FR-006).

| Extensión | Qué transporta | Por qué hace falta |
|---|---|---|
| `.../StructureDefinition/question-term` | La **semilla clínica de autoría**: el concepto en palabras que escribe quien diseña la pregunta | Hoy se guarda en `CodingJson.term` y el constructor FHIR **deliberadamente no la emite** (es metadata de autoría, no del recurso publicado — ver comentario en `questionnaire-editor/index.js:519-523`). Con la interfaz hablando FHIR puro, sin extensión se perdería en **cada guardado**. Es el mayor riesgo de pérdida silenciosa de la feature. |
| `.../StructureDefinition/questionnaire-version-id` | El identificador de la versión congelada a la que se ancla una ficha | El dominio ancla por identificador único de versión, no por número |

**Alternativa considerada para el anclaje de versión**: usar el canonical versionado del
estándar (`Questionnaire/{id}|{n}`). **Rechazada**: el dominio ancla por el identificador de
la entidad `QuestionnaireVersion`, no por su número; traducir de ida y vuelta agrega una
búsqueda y un modo de fallo por cada lectura de ficha.

**Nota sobre `meta.versionId`**: la versión de registro usada para control de concurrencia
**sí** tiene lugar nativo en el estándar y va ahí (FR-007). No necesita extensión. El mecanismo
de `ETag` / `If-Match` / 428 / 409 que ya existe es correcto y se conserva tal cual.

---

## R4 — Sacar el driver de base de datos de la capa de negocio sin romper las pruebas

**Incógnita**: la capa de negocio importa el driver del motor en dos archivos. ¿Se mueven a
infraestructura?

**Decisión**: **el corte va por el driver, no por el archivo.** El constructor de consultas se
queda en la capa de negocio; lo que se va a infraestructura es la traducción al driver y la
ejecución.

**Rationale — la restricción que decide el diseño**: `ChequeAR.Tests/Application/ReportQueryTests.cs:152-273`
tiene **11 pruebas del constructor de consultas**, incluida una que verifica que **no se puede
inyectar código** (línea 212). `ChequeAR.Tests` referencia **sólo Domain y Application**.
Mover el constructor a infraestructura haría que esas pruebas **dejen de compilar**, debilitando
exactamente la afirmación de "testing independiente" que la feature defiende (FR-021, SC-009).

Además, es el corte conceptualmente correcto: decidir *qué columna consultar según el tipo de
pregunta* y *cómo se traduce cada operador de filtro* son reglas de negocio. Abrir una
conexión y bindear parámetros no lo es.

**Diseño**:

- Parámetro neutro en la capa de negocio (nombre, valor, y un enum de tipo propio para los
  casos que hoy necesitan el tipo del driver).
- La capa de infraestructura traduce ese parámetro neutro al del driver, y absorbe las rarezas
  específicas del motor: el identificador único como binario de 16 bytes, el clonado de
  parámetros entre comandos (un mismo parámetro no se puede compartir entre dos comandos) y la
  normalización inversa de esos 16 bytes al leer.
- La orquestación (resolver el cuestionario, armar el esquema, construir, delegar, pivotear)
  queda en la capa de negocio, conservando la regla de tope de filas exportables.

**Límite honesto y explícito**: el **texto** SQL sigue siendo dialecto del motor actual (binds
con `:nombre`, paginación con `OFFSET/FETCH`). Lo que se logra —y lo único que se debe
afirmar— es que **la capa de negocio deja de declarar dependencia sobre los componentes del
motor**. Ya está registrado en el "Out of Scope" de la spec.

**Alternativa considerada**: una abstracción completa tipo query-object agnóstica del dialecto.
**Rechazada**: reescribiría el generador de SQL entero por un beneficio que nadie va a
ejercer (el hospital corre sobre un único motor), y multiplicaría el riesgo de regresión en la
parte del sistema con más lógica sutil.

---

## R5 — Validación real contra perfiles: costo y ciclo de vida

**Incógnita**: ¿cómo se cargan los perfiles y cuánto cuesta validar?

**Decisión**: validador con resolución cacheada, combinando las definiciones oficiales del
estándar con un directorio de perfiles propios, **registrado como instancia única de la
aplicación**.

**Rationale**:

- Los perfiles propios se distribuyen como archivos copiados a la salida de compilación,
  **siguiendo el patrón que el proyecto ya usa** para la lista blanca de vistas de base de
  datos (`ServiceComposition.cs:91-95`): archivo junto al ensamblado, cargado una vez al
  arrancar, con comportamiento de reserva si falta.
- El validador lee y cachea la especificación completa del estándar. Hoy está registrado como
  **transitorio** (`ServiceComposition.cs:97`), lo cual es viable sólo porque el sustituto
  actual no hace nada. Con un validador real, crear uno por petición es inviable: debe pasar a
  instancia única, junto a las que ya existen.
- Costo de despliegue: las definiciones oficiales suman aproximadamente 10 MB. La primera
  validación es lenta; se mitiga con calentamiento al arrancar.

**Perfiles a crear** (hoy **no existe ninguno** en el repositorio, pese a que el sistema declara
cumplir uno):

1. **Perfil de cuestionario** — título y estado presentes, sujeto obligatoriamente paciente,
   contexto de uso con especialidad **obligatorio**, identificador de pregunta obligatorio y
   **único**, y tipo de pregunta acotado a los que el renderizador sabe dibujar.
2. **Perfil de ficha clínica** — identificador de paciente obligatorio, referencia al
   cuestionario obligatoria, y extensión de versión anclada obligatoria.

**Decisión sobre datos existentes**: las versiones ya congeladas **no se revalidan ni se
migran** (supuesto ya registrado en la spec). Son un registro histórico inmutable referenciado
por fichas clínicas. El perfil aplica a publicaciones nuevas; leer las viejas debe seguir
funcionando (FR-014).

---

## R6 — Serialización correcta en la plataforma web actual

**Incógnita**: ¿alcanza con registrar el tipo de contenido del estándar en el serializador JSON
existente?

**Decisión**: **no.** Hace falta un formateador de medios dedicado que use el serializador y el
parser del SDK.

**Rationale**: los objetos del SDK FHIR tienen un modelo de serialización propio (elecciones de
tipo, primitivos con extensiones, elementos con `_` prefijado). Serializarlos con el
serializador JSON genérico del proyecto produce contenido inválido. El registro actual
(`WebApiConfig.cs:26-28`) sólo agrega el tipo de contenido a la lista de aceptados del
serializador genérico — por eso hoy `application/fhir+json` "funciona" pero devuelve la
representación interna.

El formateador debe registrarse **primero** en la cadena y cubrir tanto el tipo de contenido del
estándar como el JSON genérico, para que el estándar sea el comportamiento por omisión (FR-002)
en lugar de una opción que el consumidor debe pedir.

---

## R7 — Constitución del proyecto

**Hallazgo**: `.specify/memory/constitution.md` **sigue siendo la plantilla sin completar**
(contiene `[PRINCIPLE_1_NAME]`, `[SECTION_2_NAME]`, etc.). No hay principios ratificados contra
los cuales evaluar la compuerta de Constitution Check.

**Decisión**: documentar la ausencia, evaluar la compuerta contra los principios **de facto**
observables en el código, y recomendar ejecutar `/speckit-constitution` para que la compuerta
tenga valor real en features futuras. No se bloquea esta feature por ello: los principios de
facto son consistentes y esta feature los refuerza en lugar de violarlos.

---

---

## R8 — El sistema declara R5 pero emite vocabulario R4

**Hallazgo**, no decisión previa. Apareció al medir si el JSON ya congelado sobrevive al
parser estricto de Firely, antes de reescribir el reader (T023).

**Método**: se exportaron los 55 recursos reales de la base de desarrollo (20 versiones
publicadas + 35 fichas) y se parsearon con `FhirJsonParser` estricto y permisivo.

```
estricto = 46    solo-permisivo = 9    falla-ambos = 0

[SOLO PERMISIVO] Literal 'choice' is not a valid value
                 for enumeration 'QuestionnaireItemType'
```

**Causa**: `choice` y `open-choice` son vocabulario de **R4**. **R5 los eliminó** y los
reemplazó por `coding` + `answerConstraint`. Verificado contra el enum del SDK — los únicos
`item.type` válidos en R5 son:

```
attachment, boolean, coding, date, dateTime, decimal, display, group,
integer, quantity, question, reference, string, text, time, url
```

En el SDK: `Questionnaire.ItemComponent.AnswerConstraint`, de tipo
`Questionnaire.QuestionnaireAnswerConstraint?`, con literales `optionsOnly`, `optionsOrType`,
`optionsOrString`.

**Por qué nadie lo vio**: el validador era un sustituto que sólo verificaba la presencia de la
cadena `"resourceType"` (`FhirValidatorService.cs:6`). Es exactamente el agujero que esta
feature cierra — y resulta que ya había dejado pasar datos inválidos a producción de
desarrollo. **Refuerza el caso de US4**: la validación real no es una mejora cosmética, es lo
que evita seguir acumulando esto.

**Decisiones derivadas**:

| Qué | Decisión |
|---|---|
| Escritura (T018b) | `Choice` → `coding` + `optionsOnly`; `OpenChoice` → `coding` + `optionsOrString` |
| Lectura de lo congelado (T023b) | Parser permisivo + normalización `choice`/`open-choice` → `coding`. **Asimetría deliberada**: permisivo al leer lo viejo, estricto al escribir lo nuevo |
| Datos existentes | **No se migran** (FR-014). Son inmutables y hay fichas que los referencian |
| Perfil (T056) | Restringe `item.type` con vocabulario R5 |
| Front | **No se toca**. `choice`/`openChoice` es el vocabulario interno de ChequeAR (`QuestionTypeMap.ToUi`) y no tiene por qué coincidir con el del estándar |

**Alternativa considerada**: migrar las 9 versiones congeladas al vocabulario correcto.
**Rechazada**: reescribir una versión publicada rompe la garantía de inmutabilidad sobre la
que se apoya el anclaje de fichas — el médico firmó lo que leyó, y eso no se reescribe
retroactivamente ni para corregirlo.

**Costo de no haberlo detectado ahora**: la prueba de aceptación del MVP (US1, validación en
validator.fhir.org) habría fallado sin explicación aparente, con el builder ya reescrito y la
tabla de mapeo de `data-model.md` guiando hacia el error.

---

## R9 — No existe validador de perfiles de Firely para .NET Framework

**Hallazgo que contradice R5.** Apareció al implementar el validador real (T059).

**Lo que R5 daba por sentado**: que `Hl7.Fhir.Specification.R5` traía la clase `Validator`,
como en la línea 4.x del SDK.

**Lo que pasa realmente**:

- `Hl7.Fhir.Specification.R5` 5.13.4 **no contiene ningún DLL**: es un metapaquete que sólo
  arrastra `Hl7.Fhir.R5` y `Hl7.Fhir.Specification.Data.R5`. Verificado inspeccionando el
  paquete restaurado en la caché de NuGet.
- La documentación del paquete lo dice: *"The profile validator has been split off into its
  own repository"*.
- El motor separado es `Firely.Fhir.Validation.R5`. **Ninguna versión targetea
  `netstandard2.0`**: la 2.7.3 declara `netstandard2.1` y `net8.0`; la 3.x además exige
  `Hl7.Fhir.R5 >= 6.3.0`, que tampoco corre en net471.

**Conclusión**: sobre .NET Framework 4.7.1 **no hay forma de interpretar automáticamente un
StructureDefinition**. No es una limitación de esta implementación sino del ecosistema, y no
se resuelve eligiendo otra versión.

**Lo que SÍ está disponible en `netstandard2.0`** y es lo que se usa:

| Pieza | Qué aporta |
|---|---|
| `FhirJsonParser` estricto | Conformidad estructural con R5: tipos, cardinalidades, vocabularios cerrados, formas de `value[x]`. **No es poco**: es exactamente lo que detectó el problema de R8 |
| `ZipSource` + `DirectorySource` + `CachedResolver` | Carga de la especificación oficial y de los artefactos propios |

**Diseño resultante**:

- Las **reglas** de los perfiles viven en `ChequeAR.Application.Fhir.ProfileRules` — son
  decisiones del dominio, y ahí se prueban sin levantar infraestructura (FR-020, FR-021).
- El **adaptador** `FhirValidatorService` aporta lo que sí es infraestructura: parseo estricto
  y carga de artefactos. Los tipos permitidos se leen del **ValueSet publicado**, no de una
  lista en código, para que el artefacto siga siendo la fuente de verdad.
- Los perfiles se publican igual como artefactos versionados (FR-011): el sistema los declara
  en `meta.profile` y alguien puede pedir verlos. Que el runtime no los interprete solo no los
  vuelve decorativos — son el contrato, y la verificación cruzada contra el validador oficial
  de HL7 está en `quickstart.md`.

**Costo honesto y explícito**: `ProfileRules` y los `StructureDefinition` deben mantenerse
sincronizados **a mano**. Está anotado como advertencia en ambos archivos. Si el proyecto
migra a .NET moderno, el reemplazo es directo: `Firely.Fhir.Validation.R5` pasa a interpretar
los perfiles y `ProfileRules` se borra.

**Alternativas consideradas**:

| Alternativa | Rechazada porque |
|---|---|
| Migrar a .NET 8 para usar el validador | Fuera de alcance declarado de la feature; implicaría reescribir Web API 2 + MVC 5 + Razor + OWIN |
| Escribir un intérprete propio de StructureDefinition | Desproporcionado. Es un motor complejo y sutil, y equivocarse ahí es peor que no tenerlo: daría falsa confianza |
| Dejar la validación como estaba | Es lo que produjo el problema de R8 |

**Impacto sobre lo declarado en la spec**: FR-010 y FR-012 se cumplen (se valida y se rechaza
lo que no cumple, con una regla por mensaje). Lo que no se cumple literalmente es que la
validación esté *dirigida por* el perfil; la equivalencia entre reglas y perfil la sostiene una
convención documentada, no el runtime. Vale decirlo así en la tesis.

---

## R10 — Resultado del validador oficial de HL7

**Ejecutado** en <https://validator.fhir.org/> (validator-wrapper 1.0.81, validador v6.9.12)
sobre un `Questionnaire` real devuelto por la API. El validador **autodetectó R5**.

### Resultado: cero errores estructurales

| # | Severidad | Mensaje | Origen |
|---|---|---|---|
| 1 | Error | `Error performing tx5 operation 'validate-code: timeout'` | Servidor de terminología público (`tx.fhir.org`) |
| 2 | **Error** | **`The extension .../question-term could not be found so is not allowed here`** | **Nuestro** |
| 3 | Warning | `validate-code: timeout` | Servidor de terminología |
| 4 | Warning | `validate-code: timeout` | Servidor de terminología |

**Ni un solo error de estructura, cardinalidad, tipo ni vocabulario.** Es exactamente lo que
había que demostrar: el recurso es R5 válido. Comparar con el estado previo a la feature, donde
el mismo validador habría rechazado `item.type = "choice"` (R8).

Tres de los cuatro hallazgos son **timeouts del servidor de terminología público**, que es
infraestructura compartida y ajena: no puede resolver los códigos SNOMED en el tiempo
disponible. No dicen nada sobre el recurso.

### El hallazgo que sí es nuestro

El validador no puede resolver `https://chequear.garrahan/StructureDefinition/question-term`
porque **ese canonical no existe públicamente**: los perfiles viven en el repositorio y se
copian a la salida de compilación, no están publicados en la web.

Consecuencia concreta: **un integrador externo que valide nuestros recursos va a ver ese
error.** No es un defecto del recurso, pero sí un límite de lo que hoy podemos afirmar sobre
validación por terceros.

Se resuelve de dos formas, ninguna urgente y ambas fuera del alcance de esta feature:

1. Publicar los artefactos de conformidad en el dominio del hospital, de modo que el canonical
   resuelva. Es la solución correcta y depende de infraestructura del hospital.
2. Distribuirlos como paquete FHIR (formato NPM) para que el validador los cargue junto al
   recurso.

### Nota metodológica

El primer intento **con** `meta.profile` declarado dio *timeout* del servicio completo, dos
veces: el validador intenta descargar el perfil declarado y se queda esperando. Quitando esa
declaración —que es justamente lo que pide el criterio de aceptación de US1, validar contra
**R5 base**— la validación completó.

Es la misma causa raíz que el hallazgo de arriba: nuestros canonical no resuelven
públicamente. Vale registrarlo porque cualquiera que repita la verificación se va a topar con
el mismo timeout y puede confundirlo con un problema del recurso.

---

## Resumen de decisiones

| # | Decisión |
|---|---|
| R1 | Firely `Hl7.Fhir.R5` 5.13.4 pinneada (última línea `netstandard2.0`); fallback 5.9.x |
| R2 | Superficie única FHIR; 4 puntos de llamada + 2 constructores de contenido a adaptar |
| R3 | Dos extensiones propias: término de autoría y anclaje de versión. `meta.versionId` nativo |
| R4 | Corte por el driver, no por el archivo: constructor de consultas se queda en negocio |
| R5 | Validador con resolución cacheada como instancia única; 2 perfiles nuevos; sin migración |
| R6 | Formateador de medios dedicado del SDK, registrado primero y por omisión |
| R7 | Constitución sin ratificar: compuerta evaluada contra principios de facto |
| R8 | El vocabulario de opción era R4: `Choice`/`OpenChoice` → `coding` + `answerConstraint`. Lectura permisiva de lo congelado, escritura estricta |
| R9 | **Corrige R5**: no hay validador de perfiles de Firely para net471. Parseo estricto + reglas explícitas en la capa de negocio; los perfiles siguen siendo artefactos publicados |
| R10 | Validador oficial de HL7: **cero errores estructurales**. Lo único propio es que nuestros canonical no resuelven públicamente — falta publicar los perfiles en el dominio del hospital |
