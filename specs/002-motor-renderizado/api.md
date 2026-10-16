# API Contracts: Motor de Renderizado Dinámico

> **Desactualizado.** El contrato vigente es `specs/009-conformidad-fhir-capas/contracts/fhir-api.md`.
> `$populate` no existe (fuera de alcance) y los pacientes se buscan en `/fhir/r5/Patient`, no en `/api/patients`.

## Estado de implementación (2026-06-10)

| Endpoint | Estado | Notas |
|---|---|---|
| `GET /fhir/r5/QuestionnaireResponse` (search) | ✅ implementado | filtros por `subject`, `questionnaire`, `status` |
| `GET /fhir/r5/QuestionnaireResponse/{id}` | ✅ implementado | |
| `POST /fhir/r5/QuestionnaireResponse` | ✅ implementado | crea en estado `in-progress` |
| `POST /fhir/r5/QuestionnaireResponse/{id}/$finalize` | ✅ implementado | dispara validación FHIR |
| `POST /api/attachments` (multipart) | ✅ implementado | tipos: jpeg/png/pdf, ≤ 10 MB |
| `GET /api/patients?q=` | ✅ implementado (nuevo) | usa Oracle `VW_PACIENTE_DATOS_BASICOS`; R1 |
| `GET /api/patients/{hospitalId}` | ✅ implementado (nuevo) | R2/R6 |
| `GET /api/patients/{hospitalId}/encounters` | ✅ implementado (nuevo) | R7 timeline |
| `POST /fhir/r5/QuestionnaireResponse/$populate` (prefill) | ⏳ pendiente | `PrefillService` ya existe sin endpoint expuesto |

**Frontend que consume estos endpoints**: pantallas R1, R2, R3, R6, R7 — ver `specs/006-frontend-ux/api-integration.md`.

---


**Base path**: `/fhir/r5`
**Content-Type**: `application/fhir+json`

## Recursos

### `Questionnaire` (consumo, sólo lectura desde este módulo)

#### `GET /Questionnaire/{id}?_format=json`

Devuelve la versión activa lista para renderizar.

#### `GET /Questionnaire/{id}/_history/{vid}`

Devuelve una versión específica (necesario para abrir fichas históricas con su `Questionnaire` original).

---

### `QuestionnaireResponse`

#### `POST /QuestionnaireResponse`

Crea una respuesta nueva (autosave o finalizada).

**Body**: `QuestionnaireResponse` con:
- `questionnaire`: URL canónica con versión (ej. `Questionnaire/abc|3`).
- `status`: `in-progress` o `completed`.
- `subject`: `{"reference": "Patient/{hospitalId}"}`.
- `author`: opcional, default = usuario autenticado.
- `authored`: timestamp.
- `item`: respuestas anidadas por `linkId`.

**Response 201**: `QuestionnaireResponse` con `id`.
**Response 400**: estructura inválida (linkId desconocido, tipo incompatible).
**Response 422**: regla de negocio falla (rango, regex, `required` faltante en `completed`).

#### `PUT /QuestionnaireResponse/{id}`

Actualiza una respuesta existente (típicamente autosave).

**Headers**: `If-Match` con versionId.
**Response 200**: recurso actualizado.

#### `GET /QuestionnaireResponse?subject={hospitalId}&questionnaire={qid}&status={status}`

Lista respuestas filtradas.

**Response 200**: `Bundle`.

#### `GET /QuestionnaireResponse/{id}`

Devuelve una respuesta.

#### `POST /QuestionnaireResponse/{id}/$finalize`

Operation: transiciona de `in-progress` a `completed`. Valida que todos los `required` estén respondidos.

**Response 200** o `OperationOutcome` 422.

---

### Pre-carga (extension custom)

#### `GET /Questionnaire/{id}/$populate?subject=Patient/{hospitalId}`

Operation FHIR estándar (`$populate`) que devuelve un `QuestionnaireResponse` borrador con campos pre-rellenados desde el módulo 003.

**Response 200**: `QuestionnaireResponse` (no persistido).

Cada `item.answer` pre-cargado incluye:

```json
"extension": [{
  "url": "https://chequear.garrahan/ext/prefill-source",
  "valueCode": "fhir-observation" | "oracle-view"
}]
```

---

### Adjuntos

#### `POST /Attachment` (no estándar FHIR, endpoint propio)

Sube un archivo y devuelve un identificador.

**Body**: `multipart/form-data` con campo `file`.
**Response 201**: `{"id": "...", "url": "...", "contentType": "...", "size": ...}`.
**Restricciones**: ≤10 MB; `.jpg`, `.png`, `.pdf`.

Luego se referencia en `QuestionnaireResponse.item.answer.valueAttachment.url`.

## Códigos de error

| Código | Significado |
|---|---|
| 400 | Estructura inválida |
| 401/403 | Auth/Rol insuficiente |
| 404 | Recurso no existe |
| 409 | Conflicto de versión |
| 413 | Adjunto > 10 MB |
| 415 | Tipo de archivo no permitido |
| 422 | Reglas de negocio rechazan |
