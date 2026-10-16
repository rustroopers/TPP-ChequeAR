# API Contracts: Gestor y Editor de Cuestionarios

> **Desactualizado.** El contrato vigente es `specs/009-conformidad-fhir-capas/contracts/fhir-api.md`.
> `$clone` está implementado; `$count` y `_history` no existen.

## Estado de implementación (2026-06-10)

| Endpoint | Estado | Notas |
|---|---|---|
| `GET /fhir/r5/Questionnaire` (search) | ✅ implementado | `QuestionnaireController.Search` |
| `GET /fhir/r5/Questionnaire/{id}` | ✅ implementado | con `Include(Versions, Questions)` |
| `POST /fhir/r5/Questionnaire` | ✅ implementado | rol Admin/Jefe |
| `PUT /fhir/r5/Questionnaire/{id}` | ✅ implementado | `If-Match` obligatorio (428 / 409) |
| `POST /fhir/r5/Questionnaire/{id}/$publish` | ✅ implementado | `If-Match` obligatorio (428 / 409); validación FHIR + reglas de estructura |
| `POST /fhir/r5/Questionnaire/{id}/$archive` | ✅ implementado | |
| `DELETE /fhir/r5/Questionnaire/{id}` | ✅ implementado | soft delete |
| `GET /fhir/r5/Questionnaire/{id}/_history/{vid}` | ⏳ pendiente | |
| `POST /fhir/r5/Questionnaire/{id}/$clone` | ⏳ pendiente | |
| `GET /fhir/r5/Questionnaire/$count` | ✅ implementado | método repo, falta endpoint REST público |

**Frontend que consume estos endpoints**: pantallas Q1, Q2, Q5, Q6, Q7, Q9 — ver `specs/006-frontend-ux/api-integration.md`.

---


**Base path**: `/fhir/r5`
**Content-Type**: `application/fhir+json`
**Auth**: Bearer token emitido por la API de logueo del Hospital (ver módulo 005).

## Recursos FHIR expuestos

### `Questionnaire`

#### `GET /Questionnaire`

Lista cuestionarios con búsqueda por parámetros FHIR estándar.

**Query params**:
- `status` — `draft` | `active` | `retired` (mapea a Borrador/Publicado/Archivado)
- `title:contains` — búsqueda parcial por título
- `context` — especialidad (Coding)
- `_count`, `_offset` — paginación
- `_lastUpdated` — filtrado por fecha

**Response 200**: `Bundle` tipo `searchset` con entradas `Questionnaire`.

#### `GET /Questionnaire/{id}`

Devuelve la versión actual (HEAD).

**Response 200**: `Questionnaire`.
**Response 404**: `OperationOutcome` si no existe.

#### `GET /Questionnaire/{id}/_history/{vid}`

Devuelve una versión específica (inmutable).

#### `POST /Questionnaire`

Crea un cuestionario en estado `draft`.

**Body**: `Questionnaire` (sin `id`).
**Auth**: requiere rol `Administrador` o `Jefe`.
**Response 201**: `Questionnaire` con `id`, `meta.versionId=1`, `status=draft`.
**Response 400**: `OperationOutcome` si el recurso no valida contra el perfil ChequeAR.

#### `PUT /Questionnaire/{id}`

Actualiza un borrador. Si el cuestionario está `active`, crea una nueva versión.

**Headers**: `If-Match: "W/\"{versionId}\""` (optimistic locking).
**Response 200**: recurso actualizado.
**Response 409**: conflicto de versión.

#### `POST /Questionnaire/{id}/$publish`

Operation: transiciona de `draft` (o `retired`) a `active`. Valida que tenga ≥1 ítem, linkId únicos, condiciones completas que apunten a preguntas y opciones existentes, y pasa el validador. Un `retired` sin cambios pendientes se reactiva sin congelar una versión nueva.

**Headers**: `If-Match: W/"{versionId}"` obligatorio: se congela lo que está en la base, así que hay que demostrar que se vio la última versión del registro.

**Response 200**: `Questionnaire` con `status=active` (y `ETag`).
**Response 409**: alguien guardó en el medio (`OperationOutcome` con versión esperada y encontrada).
**Response 422**: `OperationOutcome` con errores de validación.
**Response 428**: falta `If-Match`.

#### `POST /Questionnaire/{id}/$archive`

Operation: transiciona a `retired` (no eliminable si tiene `QuestionnaireResponse` asociadas).

#### `POST /Questionnaire/{id}/$clone`

Operation: duplica como nuevo `draft`.

**Response 201**: nuevo `Questionnaire`.

#### `DELETE /Questionnaire/{id}`

Solo permitido si el recurso está en `draft` y no tiene respuestas.

**Response 204**.

## Códigos de error comunes

| Código | Causa |
|--------|-------|
| 400 | JSON malformado o validación FHIR fallida |
| 401 | Token ausente o inválido |
| 403 | Rol insuficiente |
| 404 | Recurso no encontrado |
| 409 | Conflicto de versión / publicación inválida |
| 422 | Reglas de negocio rechazan la operación |

## Auditoría

Toda operación de escritura genera un evento `AuditEvent` (FHIR) con: usuario, acción, recurso afectado, IP, timestamp. Ver módulo 005.

## Ejemplo: crear cuestionario mínimo

```http
POST /fhir/r5/Questionnaire HTTP/1.1
Content-Type: application/fhir+json
Authorization: Bearer eyJ...

{
  "resourceType": "Questionnaire",
  "title": "Seguimiento Trasplante Hepático",
  "status": "draft",
  "useContext": [{
    "code": {"system": "http://terminology.hl7.org/CodeSystem/usage-context-type", "code": "focus"},
    "valueCodeableConcept": {"coding": [{"system": "http://snomed.info/sct", "code": "313039003"}]}
  }],
  "item": [{
    "linkId": "1",
    "type": "group",
    "text": "Datos basales",
    "item": [
      {"linkId": "1.1", "type": "decimal", "text": "Peso (kg)", "required": true},
      {"linkId": "1.2", "type": "date", "text": "Fecha de trasplante"}
    ]
  }]
}
```
