# API Contracts: Módulo de Análisis y Exportación

> **Desactualizado** en: formato de los grupos de filtros (ver `ReportQuery`), `sortBy` (no existe) y
> códigos de error (las consultas inválidas devuelven 422, no 400/413). No hay exportación asincrónica.

## Estado de implementación (2026-06-10)

| Endpoint | Estado | Notas |
|---|---|---|
| `POST /api/reports/query` | ✅ implementado | `ReportExecutor.ExecuteAsync` |
| `POST /api/reports/export` | ✅ implementado | CSV + XLSX (XLSX stub a ClosedXML pendiente) |
| `GET /api/reports/saved` | ✅ implementado (nuevo) | listado por usuario + especialidad |
| `GET /api/reports/saved/{id}` | ✅ implementado (nuevo) | |
| `POST /api/reports/saved` | ✅ implementado (nuevo) | |
| `PUT /api/reports/saved/{id}` | ✅ implementado (nuevo) | check ownership |
| `DELETE /api/reports/saved/{id}` | ✅ implementado (nuevo) | check ownership |
| `GET /api/reports/preview-columns?questionnaireId=` | ✅ implementado (nuevo) | devuelve linkIds + types para E2 |
| `GET /api/reports/saved/{id}/run` | ⏳ pendiente | atajo a `/query` con la cohorte guardada |
| `GET /api/reports/jobs/{jobId}` | ⏳ pendiente | export asincrónico (Hangfire) |
| `GET /api/reports/artifacts/{artifactId}` | ⏳ pendiente | descarga diferida |

**Frontend que consume estos endpoints**: pantallas E1, E2, E3, E4, E5, E6 — ver `specs/006-frontend-ux/api-integration.md`.

---


**Base path**: `/api/reports` (no es FHIR; es API de aplicación)
**Content-Type**: `application/json`

## Endpoints

### `POST /api/reports/query`

Ejecuta una consulta ad-hoc sobre respuestas finalizadas.

**Body**:

```json
{
  "questionnaireId": "abc-123",
  "questionnaireVersions": [3, 4],
  "filters": {
    "logic": "AND",
    "predicates": [
      { "linkId": "bilirrubina", "op": ">", "value": 5 },
      { "logic": "OR", "predicates": [
        { "linkId": "sexo", "op": "=", "value": "M" },
        { "linkId": "edad", "op": "between", "value": [5, 12] }
      ]}
    ]
  },
  "dateRange": { "from": "2026-01-01", "to": "2026-06-01" },
  "specialties": ["reumato", "hepato"],
  "page": 1,
  "pageSize": 50,
  "sortBy": "authoredAt",
  "sortDir": "desc"
}
```

**Response 200**:

```json
{
  "total": 1234,
  "page": 1,
  "pageSize": 50,
  "columns": [
    { "linkId": "id", "label": "Id Respuesta", "type": "string" },
    { "linkId": "bilirrubina", "label": "Bilirrubina (mg/dL)", "type": "decimal" }
  ],
  "rows": [ ... ],
  "stats": {
    "bilirrubina": { "n": 1234, "mean": 6.2, "median": 5.8, "stddev": 1.4 }
  }
}
```

**Response 400**: filtros inválidos (linkId desconocido, op incompatible con tipo).

### `POST /api/reports/export`

Inicia una exportación. Si el resultado es chico (`< 5 MB` y `< 10k filas`), responde sincrónicamente con el archivo. Si no, encola un job y responde 202.

**Body**: igual que `/query` + `"format": "xlsx" | "csv"`.

**Response 200** (sincrónico): `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` o `text/csv; charset=utf-8`.
**Response 202** (asincrónico): `{ "jobId": "...", "statusUrl": "/api/reports/jobs/{jobId}" }`.

### `GET /api/reports/jobs/{jobId}`

Consulta el estado de un export asincrónico.

**Response 200**:

```json
{
  "jobId": "...",
  "status": "pending" | "running" | "completed" | "failed",
  "progress": 0.42,
  "downloadUrl": "/api/reports/artifacts/{artifactId}",
  "expiresAt": "2026-06-08T12:00:00Z"
}
```

### `GET /api/reports/artifacts/{artifactId}`

Descarga el archivo generado. TTL de 7 días.

### `GET/POST/PUT/DELETE /api/reports/saved`

CRUD de consultas guardadas.

```json
POST /api/reports/saved
{
  "name": "Trasplante hepático – Bilirrubina alta",
  "scope": "specialty" | "private",
  "query": { /* mismo body que /query */ }
}
```

### `GET /api/reports/saved/{id}/run`

Ejecuta una consulta guardada (atajo).

### `GET /api/reports/preview-columns?questionnaireId=...`

Devuelve las columnas posibles (linkIds) y sus tipos para construir filtros en la UI.

## Permisos

| Rol | query | export | saved.write | saved.share |
|---|---|---|---|---|
| Administrador | ✅ | ✅ | ✅ | ✅ |
| Jefe | ✅ | ✅ | ✅ | ✅ |
| Médico | ✅ (su especialidad) | ✅ | ✅ | sólo own |
| Observador | ✅ | ✅ | ❌ | ❌ |

## Códigos de error

| Código | Causa |
|---|---|
| 400 | Filtros inválidos |
| 403 | Sin permiso sobre la especialidad |
| 413 | Resultado demasiado grande para sincronía → reintentar con export job |
| 422 | Operador incompatible con tipo de pregunta |
