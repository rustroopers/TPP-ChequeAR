# Phase 1 — Data Model: Conformidad FHIR real y saneamiento de capas

**Feature**: `009-conformidad-fhir-capas` | **Fecha**: 2026-08-02

Esta feature **no modifica el esquema de base de datos ni las entidades de dominio**. Lo que
define es el **mapeo bidireccional** entre el modelo interno y los recursos del estándar
HL7 FHIR R5, que hoy existe sólo en un sentido y de forma parcial.

Las entidades de dominio (`ChequeAR.Domain/Entities/`) quedan intactas: son POCOs sin
dependencias y no hay razón para tocarlas.

---

## 1. Cuestionario → `Questionnaire`

**Entidad**: `ChequeAR.Domain.Entities.Questionnaire`

| Campo de dominio | Elemento FHIR | Notas |
|---|---|---|
| `Id` (Guid) | `id` | |
| — | `url` | Canonical derivado: `https://chequear.garrahan/Questionnaire/{Id}` |
| `Title` | `title` y `name` | |
| `Description` | `description` | Se omite si está vacío |
| `Status` | `status` | `Draft`→`draft`, `Active`→`active`, `Retired`→`retired` |
| `SpecialtyCode` / `SpecialtyDisplay` | `useContext[0]` | Tipo de contexto `focus`; sistema propio de especialidades |
| `ConcurrencyVersion` (int) | **`meta.versionId`** | ⚠️ Nuevo. Hoy viaja como campo propio. Nativo del estándar (FR-007) |
| — | `meta.profile[0]` | Identificador del perfil de conformidad declarado |
| `Groups` + `Questions` | `item[]` | Ver §1.1 |
| `CreatedBy`, `CreatedAt`, `UpdatedAt`, `IsDeleted`, `HasPendingChanges`, `CurrentVersionId` | **no se exponen** | Metadata interna de gestión, no del recurso clínico |

**Transiciones de estado** (sin cambios respecto de hoy):

```
Draft ──publicar──► Active ──archivar──► Retired ──publicar──► Active
  │                   │                     ▲
  └──archivar─────────┴─────────────────────┘

Regla de publicación (Questionnaire.CanPublish): requiere ≥1 pregunta, y si ya
está Active exige cambios pendientes — publicar de nuevo sin cambios no crea
una versión idéntica.

Regla de baja (QuestionnaireService.DeleteAsync): si existen fichas cargadas,
NO se elimina — se archiva. Las fichas no pueden quedar apuntando a algo que
desapareció.
```

### 1.1 Secciones y preguntas → `item[]`

| Dominio | FHIR | Notas |
|---|---|---|
| `QuestionGroup` | `item` con `type = "group"` | `LinkId` → `linkId` (cae al `Id` si está vacío); `Title` → `text`; se ordenan por `OrderIndex` |
| `Question` | `item` hoja | Anidado bajo su grupo, o suelto si no tiene `GroupId` |
| `Question.LinkId` | `item.linkId` | **Obligatorio y único** en el perfil (§5) |
| `Question.Text` | `item.text` | |
| `Question.Type` | `item.type` | Ver §1.2 |
| `Question.Required` | `item.required` | |
| `Question.RepeatsAllowed` | `item.repeats` | Se omite si es falso |
| `Question.Options` | `item.answerOption[].valueCoding` | `Code ?? Value ?? Display` → `code`; `Display ?? Value` → `display`. ⚠️ La distinción `Code`/`Value`/`Display` es un punto de pérdida conocido |
| `Question.ValidationJson` | extensiones + `maxLength` nativo | Ver §1.3 |
| `Question.EnableWhenJson` | `item.enableWhen[]` + `enableBehavior` | Ver §1.4 |
| `Question.CodingJson` | `item.code[]` + extensión propia | Ver §1.5 |

### 1.2 Tipos de pregunta (los 11 de SC-004)

| `QuestionType` | `item.type` | Ida y vuelta |
|---|---|---|
| `Decimal` | `decimal` | directo |
| `Integer` | `integer` | directo |
| `String` | `string` | directo |
| `Text` | `text` | directo |
| `Date` | `date` | directo |
| `DateTime` | `dateTime` | ⚠️ camelCase, no `datetime` |
| `Boolean` | `boolean` | directo |
| `Choice` | `choice` | directo |
| `OpenChoice` | `open-choice` | ⚠️ con guion en FHIR; `openChoice` en la interfaz de usuario |
| `Attachment` | `attachment` | directo |
| `Scale` | `integer` + extensión `questionnaire-itemControl` con código `slider` | ⚠️ **FHIR no tiene tipo escala**. Sin el marcador de control, al releer una escala vuelve como número común |

> El vocabulario que hablan las islas Vue lo fija `QuestionTypeMap.ToUi` y **no cambia**:
> `openChoice`, `dateTime`, `scale`. La traducción a FHIR ocurre en la capa de negocio.

### 1.3 Validaciones → extensiones estándar

`ValidationJson` es un contrato JSON propio almacenado como texto. Se descompone así:

| Clave | Destino FHIR |
|---|---|
| `min` | extensión `minValue` — `valueInteger` si el tipo es entero o escala, `valueDecimal` si no |
| `max` | extensión `maxValue` — mismo criterio de tipo |
| `unit` | extensión `questionnaire-unit`, `valueCoding` con sistema de unidades de medida |
| `accept` | extensión `mimeType`, `valueCode` |
| `pattern` | extensión `regex`, `valueString` |
| `maxLength` | **`item.maxLength` — campo NATIVO**, no extensión. Sólo si es > 0 |

Un `ValidationJson` ausente o ilegible **no bloquea**: se publica sin restricciones. Pero el
marcador de escala se emite igual, porque hace al **tipo**, no a la validación.

### 1.4 Lógica condicional → `enableWhen`

| Dominio (`EnableWhenJson`) | FHIR |
|---|---|
| `question` (linkId destino) | `enableWhen[0].question` |
| `op` | `enableWhen[0].operator` (por omisión `=`) |
| `value` | `enableWhen[0].answer[x]` — **tipado según el tipo de la pregunta REFERENCIADA, no de ésta** |

⚠️ **Invariante frágil**: el tipo del `answer[x]` corresponde a la pregunta apuntada.
`Boolean`→`answerBoolean`, `Integer`/`Scale`→`answerInteger`, `Decimal`→`answerDecimal`,
`Choice`/`OpenChoice`→`answerCoding`, resto→`answerString`. Resolverlo exige un índice
`linkId → Question` construido antes de recorrer los items. Es el detalle más fácil de perder
al portar al SDK.

`enableBehavior = "all"` se emite aunque haya una sola condición: deja el recurso listo para
cuando se soporte más de una.

**Regla de integridad** (FR de la spec 001, se conserva): una condición que apunta a un
`linkId` inexistente debe detectarse **antes** de publicar.

### 1.5 Codificación clínica → `item.code` + extensión

`CodingJson` guarda dos cosas de naturaleza distinta:

| Clave | Destino | Por qué |
|---|---|---|
| `system` + `code` + `display` | `item.code[0]` (`Coding`) | Es el concepto SNOMED CT elegido. Metadata clínica del recurso publicado |
| `term` | **extensión `question-term`** | Es la **semilla de autoría**: el concepto en palabras que escribió quien diseña, aunque todavía no haya código elegido. Hoy **no viaja a FHIR** a propósito. Con superficie única FHIR, sin extensión se pierde en cada guardado |

Al leer de vuelta, si no hay `term` se cae al `display` del concepto — es la mejor aproximación
disponible y evita que el campo aparezca vacío al reabrir cuestionarios anteriores a esta
feature (caso borde de la spec).

---

## 2. Versión publicada → recurso congelado

**Entidad**: `ChequeAR.Domain.Entities.QuestionnaireVersion`

| Campo | Rol |
|---|---|
| `Id` (Guid) | Identidad de la versión. Es a lo que **ancla** cada ficha |
| `VersionNumber` (int) | → `Questionnaire.version` del recurso congelado |
| `FhirJson` (CLOB) | El recurso serializado. **Inmutable** |
| `PublishedBy`, `PublishedAt`, `ChangeNote` | Trazabilidad; no se exponen en el recurso |

**Invariantes**:

- El contenido de `FhirJson` **nunca se reescribe** una vez publicado.
- **FR-014**: las versiones publicadas antes de esta feature se siguen sirviendo aunque no
  cumplan el perfil vigente. **No se revalidan ni se migran.**
- Es distinto de `Questionnaire.ConcurrencyVersion`: aquélla es la versión del **registro**
  (equivale a `meta.versionId`); ésta es una **instantánea publicada**.

---

## 3. Ficha clínica → `QuestionnaireResponse`

**Entidad**: `ChequeAR.Domain.Entities.QuestionnaireResponse`

| Campo de dominio | Elemento FHIR | Notas |
|---|---|---|
| `Id` | `id` | |
| `QuestionnaireId` | `questionnaire` | Canonical al cuestionario |
| `QuestionnaireVersionId` | **extensión `questionnaire-version-id`** | Sin equivalente nativo. Ancla la ficha a la versión con la que se completó |
| `PatientHospitalId` | `subject.identifier` | Sistema propio de identificador de historia clínica |
| `Status` | `status` | `InProgress`→`in-progress`, `Completed`→`completed`, `Amended`→`amended` |
| `AuthoredAt` | `authored` | |
| `AuthorUserId` | `author.display` | |
| `Items` | `item[]` | Ver §3.1 |
| — | `contained[0]` (`Patient`) | Ver §3.2 |
| `LastSavedAt`, `IsAutosave`, `EncounterId` | no se exponen | Metadata interna |

**Transiciones de estado**:

```
InProgress ──finalizar──► Completed
     ▲                        │
     └────────────────────────┘
       (si la validación falla, NO se finaliza: vuelve a InProgress)
```

⚠️ Al finalizar, el recurso **se regenera** con estado `completed` **antes** de validar: el
guardado al crear dice `in-progress` y quedaría contradiciendo el estado final.

### 3.1 Respuestas → `item[].answer[]`

| Campo del item de dominio | `answer[x]` |
|---|---|
| `ValueBoolean` | `valueBoolean` |
| `ValueInteger` | `valueInteger` |
| `ValueDecimal` | `valueDecimal` |
| `ValueDate` | `valueDateTime` |
| `ValueCode` | `valueCoding` (`code` y `display`) |
| `ValueString` | `valueString` |
| `AttachmentId` + `ValueString` | `valueAttachment` (`url` a `/api/attachments/{id}`, `title` = nombre de archivo) |

**Reglas**:

- Un `linkId` puede repetirse en el dominio (opción múltiple: un registro por opción marcada).
  En FHIR eso es **un** `item` con varias entradas en `answer`. El agrupado por `linkId` es
  obligatorio.
- Una pregunta sin responder se serializa **sin** `answer`, no con un `answer` vacío.
- El `item.text` sale de la **versión anclada**, no de la entidad viva: si alguien renombró una
  pregunta después, la ficha conserva lo que el médico leyó.
- El adjunto se referencia por dirección: `/api/attachments/{id}` debe seguir existiendo
  (FR-017).

### 3.2 Paciente embebido (`contained`)

Cuando hay datos demográficos disponibles, se embebe un `Patient` contenido con `name.text`,
`gender` y `birthDate`.

⚠️ **No es decorativo**: cuando la fuente hospitalaria no responde, ese `Patient` contenido es
la **única** forma de saber de quién es la ficha (FR-016). Al regenerar el recurso al finalizar,
esos datos deben **rescatarse del recurso previo**, no perderse. Sólo vale un `Patient`
contenido real: no sirve caer a `subject.display`, porque ahí se escribe la historia clínica
cuando no hay nombre, y regenerar terminaría inventando un paciente llamado como el número de HC.

---

## 4. Recursos derivados (sólo lectura)

### 4.1 Paciente → `Patient`

ChequeAR **no es dueño** de esta entidad. Se proyecta desde `PatientSearchHit`, que a su vez
sale de la vista de base de datos del hospital o, en modo degradado, de las fichas propias.

| Origen | FHIR |
|---|---|
| `HospitalId` | `identifier` con sistema propio de historia clínica |
| `FullName` | `name[0].text` |
| `AgeLabel` | derivado — no se expone; `birthDate` si está disponible |

### 4.2 Encuentro → `Encounter`

Derivado de las fichas cargadas para un paciente. `subject.identifier` = historia clínica;
la fecha sale de `AuthoredAt` de la ficha.

### 4.3 Evento de auditoría → `AuditEvent`

| Dominio | FHIR |
|---|---|
| `Action` | `action` |
| `EntityRef` | `entity.what` |
| actor | `agent.who` |
| momento | `recorded` |

---

## 5. Perfiles de conformidad (nuevos)

Hoy el sistema declara cumplir un perfil que **no existe como archivo**. Se crean dos.

### `ChequeARQuestionnaire`

| Restricción | Justificación |
|---|---|
| `title` y `status` presentes | Sin título el listado no es usable |
| `subjectType` = `Patient` | El sistema sólo cuestiona pacientes |
| `useContext` con especialidad **obligatorio** | La especialidad gobierna filtros, permisos y reportes |
| `item.linkId` obligatorio y **único en todo el recurso** | Las respuestas y los filtros de reportes referencian por `linkId`; un duplicado corrompe fichas y cohortes de forma silenciosa |
| `item.type` acotado a los 11 tipos de §1.2 | Un tipo fuera de la lista es una pregunta que el renderizador no sabe dibujar |

### `ChequeARQuestionnaireResponse`

| Restricción | Justificación |
|---|---|
| `subject.identifier` obligatorio | Una ficha sin paciente no tiene sentido clínico |
| `questionnaire` obligatorio | Sin referencia no se puede interpretar |
| Extensión de versión anclada **obligatoria** | Sin ella se pierde la garantía de que la ficha refleja lo que el médico leyó |

---

## 6. Entidades que esta feature toca sólo por capas

Sin cambio de forma; cambia **quién** las manipula (de controller a servicio).

| Entidad | Regla que se mueve a la capa de negocio |
|---|---|
| `SavedReport` | Autorización por propietario: sólo quien la creó puede modificarla o eliminarla. Merge de campos en la actualización |
| `AuditEvent` | Filtrado por actor y recurso, y paginación con total real |
| `User` | Listado con filtros y alta/modificación |

---

## 7. Modelo interno de consultas de reportes (R4)

| Antes | Después | Capa |
|---|---|---|
| `BuiltQuery.Parameters : List<OracleParameter>` | `List<QueryParameter>` (nombre, valor, enum de tipo propio) | Negocio |
| Identificador único como binario de 16 bytes con tipo del driver | `QueryParameter` de tipo `Raw16` con los bytes | Negocio construye, infraestructura traduce |
| Clonado de parámetros entre comandos | Se mueve **tal cual**, con su comentario | Infraestructura |
| Normalización de 16 bytes → identificador al leer | Se mueve **tal cual**, con su comentario | Infraestructura |

⚠️ El **texto** SQL sigue siendo dialecto del motor actual. El alcance es eliminar la
dependencia sobre los **componentes** del motor, no sobre el dialecto (ya declarado en el
"Out of Scope" de la spec).
