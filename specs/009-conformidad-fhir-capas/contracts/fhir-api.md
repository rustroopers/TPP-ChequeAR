# Contrato — Interfaz de interoperabilidad FHIR R5

**Feature**: `009-conformidad-fhir-capas` | **Base**: `/fhir/r5` | **Versión del estándar**: 5.0.0

Este documento define el contrato **después** de la feature. Donde el comportamiento cambia
respecto de hoy, está marcado con ⚠️.

---

## Reglas transversales

| Regla | Detalle |
|---|---|
| **Tipo de contenido** | ⚠️ `application/fhir+json` **por omisión**, sin requerir `Accept`. Hoy se devuelve `application/json` salvo que el consumidor lo pida (FR-002) |
| **Cuerpo de entrada** | ⚠️ Recursos FHIR. Hoy se acepta la representación interna (FR-004) |
| **Errores** | ⚠️ Siempre `OperationOutcome`. Hoy `{ "error": "..." }` (FR-008) |
| **Colecciones** | ⚠️ `Bundle` con `type = "searchset"`, `total` real y `entry[].fullUrl` + `entry[].resource`. Hoy `entry[]` son entidades desnudas (FR-003) |
| **Autenticación** | Sin cambios: `Authorization: Bearer <jwt>`. `/fhir/r5/metadata` es anónimo |
| **Autorización** | Sin cambios respecto de los roles actuales por operación |
| **Versión de registro** | ⚠️ En `meta.versionId`, además del `ETag` que ya existe (FR-007) |

### Catálogo de `OperationOutcome`

| HTTP | `issue.severity` | `issue.code` | Cuándo |
|---|---|---|---|
| 400 | `error` | `invalid` | Cuerpo ausente o no interpretable |
| 403 | `error` | `forbidden` | Rol insuficiente, o consulta guardada de otra persona |
| 404 | `error` | `not-found` | Identificador inexistente |
| 409 | `error` | `conflict` | Conflicto de edición. `diagnostics` incluye versión esperada y encontrada |
| 422 | `error` | `business-rule` | Regla de negocio violada, o validación de perfil fallida |
| 428 | `error` | `required` | Falta `If-Match` en una escritura |
| 429 / 502 / 503 | `error` | `transient` | Servicio externo saturado, caído o sin configurar |

`issue.diagnostics` lleva siempre un texto legible por una persona, en español. Es lo que las
pantallas muestran (FR-009).

---

## `GET /fhir/r5/metadata`

Anónimo. Devuelve `CapabilityStatement`.

⚠️ **Cambios**: se declaran `Patient` y `Encounter`; `Observation` aparece explícitamente como
**no soportado** (FR-013).

| Recurso | Interacciones | Operaciones |
|---|---|---|
| `Questionnaire` | `read`, `search-type`, `create`, `update`, `delete` | `$publish`, `$archive`, `$clone` |
| `QuestionnaireResponse` | `read`, `search-type`, `create`, `update` | `$finalize` |
| `AuditEvent` | `read`, `search-type` | — |
| `Patient` | `read`, `search-type` | — ⚠️ nuevo |
| `Encounter` | `search-type` | — ⚠️ nuevo |
| `ValueSet` | — | `$expand` |
| `Observation` | — | ⚠️ declarado **no soportado** |

---

## `Questionnaire`

### `GET /fhir/r5/Questionnaire`

Parámetros: `status`, `title:contains`, `specialty`, `_count` (por omisión 50), `_offset`.

⚠️ Devuelve `Bundle` `searchset` con `entry[].resource` como `Questionnaire` completos.

### `GET /fhir/r5/Questionnaire/{id}`

⚠️ Devuelve un `Questionnaire` real: `resourceType`, `item[]` anidado, `answerOption`,
`enableWhen`, extensiones. Hoy devuelve la entidad interna.

Cabecera `ETag: W/"{n}"` (sin cambios) **y** `meta.versionId = "{n}"` (⚠️ nuevo).

`404` → `OperationOutcome` con `not-found`.

### `POST /fhir/r5/Questionnaire`

Roles: Administrador, Jefe. ⚠️ Cuerpo: `Questionnaire` FHIR.

`201 Created` con el recurso y su `ETag`.

### `PUT /fhir/r5/Questionnaire/{id}`

Roles: Administrador, Jefe. ⚠️ Cuerpo: `Questionnaire` FHIR.
**`If-Match` obligatorio** (sin cambios en la semántica).

| Situación | Respuesta |
|---|---|
| OK | `200` con el recurso y `ETag` nuevo |
| Sin `If-Match` | ⚠️ `428` + `OperationOutcome` `required` |
| Versión desactualizada | ⚠️ `409` + `OperationOutcome` `conflict` con esperada y encontrada |
| Cuestionario archivado | ⚠️ `422` + `OperationOutcome` `business-rule` |

### `POST /fhir/r5/Questionnaire/{id}/$publish`

Roles: Administrador, Jefe. Congela una versión.

⚠️ **Ahora valida contra el perfil `ChequeARQuestionnaire`** (FR-010, FR-012). Si falla →
`422` + `OperationOutcome` con **un `issue` por regla incumplida**.

Reglas de negocio previas a la validación (sin cambios): requiere ≥1 pregunta; un cuestionario
activo sin cambios pendientes no se republica.

### `POST /fhir/r5/Questionnaire/{id}/$archive` · `/$clone`

Sin cambios de semántica. ⚠️ Errores como `OperationOutcome`; `$clone` devuelve el
`Questionnaire` FHIR creado.

### `DELETE /fhir/r5/Questionnaire/{id}`

Rol: Administrador. Sin cambios en la regla: si hay fichas cargadas **se archiva en lugar de
eliminarse** (un `204` sería mentira: el recurso sigue existiendo).

| Situación | Respuesta |
|---|---|
| Eliminado | `204` |
| Archivado por estar en uso | ⚠️ `200` + `OperationOutcome` `informational` explicando por qué |

---

## `QuestionnaireResponse`

### `GET /fhir/r5/QuestionnaireResponse`

Parámetros: `subject`, `questionnaire`, `status`, `_count`, `_offset`.
⚠️ `Bundle` `searchset` conforme.

> Consumidor interno afectado: `Views/Encounters/Search.cshtml:159` pasa a leer
> `entry[].resource.questionnaire` y `.authored`.

### `GET /fhir/r5/QuestionnaireResponse/{id}`

⚠️ `QuestionnaireResponse` real: `item[].answer[].value[x]` tipado, `linkId` repetidos
agrupados en un `item` con varias `answer`, `Patient` contenido cuando hay datos demográficos.

### `POST /fhir/r5/QuestionnaireResponse`

Roles: Administrador, Jefe, Médico. ⚠️ Cuerpo: `QuestionnaireResponse` FHIR, con el anclaje de
versión en la extensión `questionnaire-version-id`.

Si no se indica versión, se cae a la vigente. Si el cuestionario no tiene ninguna versión
publicada → `422`.

### `POST /fhir/r5/QuestionnaireResponse/{id}/$finalize`

⚠️ Valida contra el perfil `ChequeARQuestionnaireResponse`. Si falla, **no se finaliza**: la
ficha vuelve a `in-progress` y se responde `422` + `OperationOutcome`.

Finalizar algo ya completado es idempotente: devuelve el recurso sin error.

### Sesión de edición y guardado parcial (spec 013)

La ficha se completa en varias sesiones. Una sola persona edita a la vez (lock pesimista con
vencimiento) y cada escritura lleva además `If-Match` (`ConcurrencyVersion` como ETag débil).
Roles: Administrador, Jefe, Médico.

| Operación | Headers | Respuesta |
|---|---|---|
| `POST {id}/$lock` | — | `{ lockToken, expiresAt, concurrencyVersion }` + ETag; `409` con `lock-holder` / `lock-expires` si la tiene otro |
| `POST {id}/$renew-lock` | `X-Lock-Token` | `{ expiresAt, concurrencyVersion }` |
| `POST {id}/$unlock` | `X-Lock-Token` (opcional) | `200`; idempotente |
| `POST {id}/$save-progress` | `X-Lock-Token`, `If-Match` | `QuestionnaireResponse` + ETag + `X-Lock-Expires-At` |
| `POST {id}/$finalize` | `X-Lock-Token` | `QuestionnaireResponse` |
| `POST {id}/$amend` / `$amend-commit` | `X-Lock-Token` (en el commit) | Corrección de una ficha finalizada, con motivo |
| `POST {id}/$abandon` | — | Descarta un borrador `in-progress` |
| `GET {id}/$retarget-preview` · `POST {id}/$retarget-version` | `X-Lock-Token` (en el POST) | Pasa la ficha a la versión vigente del cuestionario |

Sin el header requerido → `428` + `OperationOutcome`. El cuerpo de `$save-progress`,
`$finalize`, `$unlock` y `$amend-commit` es la forma interna de guardado (`Items` con
`LinkId`/`ValueXxx`), no un recurso: las lecturas sí devuelven el `QuestionnaireResponse` FHIR.

---

## `Patient` ⚠️ nuevo

Reemplaza `/api/patients`. Sólo lectura — ChequeAR no es dueño de la entidad.

### `GET /fhir/r5/Patient?name={texto}`

`Bundle` `searchset` de `Patient` con `identifier` de historia clínica. Pagina con `_count`
(por omisión 50) y `_offset`, hasta 1000 resultados. La vista no da un total: `total` se
informa sólo cuando la búsqueda entra entera, y el link `next` indica si hay más.

**Modo degradado (FR-016)**: si la fuente hospitalaria no responde, los pacientes se derivan de
las fichas propias. **Nunca** se devuelven datos de demostración.

### `GET /fhir/r5/Patient/{hospitalId}`

`Patient` o `404` + `OperationOutcome`.

---

## `Encounter` ⚠️ nuevo

Reemplaza `/api/patients/{id}/encounters`. Sólo lectura, derivado de las fichas.

### `GET /fhir/r5/Encounter?subject={hospitalId}`

`Bundle` `searchset` de `Encounter`, con `total` y paginado por `_count` y `_offset`.

---

## `AuditEvent`

### `GET /fhir/r5/AuditEvent`

Roles: Administrador, Jefe. Parámetros: `agent.who`, `entity.what`, `_count`, `_offset`.

`Bundle` conforme con `entry[].resource` como `AuditEvent` FHIR. El filtrado y la
paginación se hacen en la base.

### `GET /fhir/r5/AuditEvent/{id}`

`AuditEvent` FHIR o `404` + `OperationOutcome`.

---

## `ValueSet`

### `GET /fhir/r5/ValueSet/$expand?filter={texto}&count={n}`

Los tres roles. Autocompletado SNOMED CT contra el servidor terminológico. Devuelve un
`ValueSet` (`url = http://snomed.info/sct?fhir_vs`) con los conceptos en `expansion.contains`
y los parámetros usados en `expansion.parameter`. `count` va de 1 a 50.

Menos de 3 caracteres devuelve expansión vacía sin consultar el servidor externo. Los errores
(`429`, `502`, `503`) son `OperationOutcome` con `code = "transient"` y el tiempo de espera en
el texto.

---

## Fuera de la interfaz de interoperabilidad (FR-017)

Permanecen como REST propio por no tener recurso natural en el estándar. **Deben seguir
existiendo** — en particular los adjuntos, que las fichas referencian por dirección desde
`valueAttachment.url`.

| Ruta | Motivo |
|---|---|
| `/auth` | Autenticación contra el hospital |
| `/api/reports`, `/api/reports/saved` | Consultas y exportación; sin recurso FHIR natural |
| `/api/users` | Espejo del padrón hospitalario |
| `/api/attachments` | Binarios; destino de `valueAttachment.url` |
| `/api/integrations`, `/api/health` | Diagnóstico operativo |
| `/api/variables` | Catálogo de variables clínicas (spec 014) |
| `/api/specialties` | Catálogo de especialidades |
| `/api/patients/{id}/trend*` | Series para la pantalla de evolución (spec 010) |
| `/api/deploy` | Migraciones desde el CD |

---

## Extensiones propias

| URL | Aplica a | Tipo | Para qué |
|---|---|---|---|
| `https://chequear.garrahan/StructureDefinition/question-term` | `Questionnaire.item` | `valueString` | Semilla clínica de autoría. Sin ella se pierde en cada guardado |
| `https://chequear.garrahan/StructureDefinition/questionnaire-version-id` | `QuestionnaireResponse` | `valueString` (identificador único) | Ancla la ficha a la versión con la que se completó |
| `…/questionnaire-term` | `Questionnaire` | `valueString` | Semilla de autoría del cuestionario entero |
| `…/question-variable-id`, `…/question-variable-version` | `Questionnaire.item` | `valueString`, `valueInteger` | Variable clínica de la que sale la pregunta (spec 014) |
| `…/option-snomed-display` | `answerOption.valueCoding` | `valueString` | Término SNOMED cuando la etiqueta visible es otra |
| `…/lock-holder`, `…/lock-expires` | `OperationOutcome` (409) | `valueString`, `valueInstant` | Quién tiene la sesión de edición y hasta cuándo |

## Perfiles declarados

| URL | Aplica a |
|---|---|
| `https://chequear.garrahan/StructureDefinition/ChequeARQuestionnaire` | `Questionnaire` |
| `https://chequear.garrahan/StructureDefinition/ChequeARQuestionnaireResponse` | `QuestionnaireResponse` |

Los dos existen como artefacto en `src/ChequeAR.Infrastructure/Fhir/Profiles/`.
