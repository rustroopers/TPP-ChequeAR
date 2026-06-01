# API Contracts: Motor de Renderizado Dinámico

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
