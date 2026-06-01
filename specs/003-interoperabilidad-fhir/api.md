# API Contracts: Capa de Interoperabilidad y Almacenamiento

## API expuesta por ChequeAR (servidor FHIR)

**Base**: `/fhir/r5` · **Content-Type**: `application/fhir+json`

### `GET /metadata`

Devuelve el `CapabilityStatement` del servidor FHIR.

**Response 200**: `CapabilityStatement` declarando recursos soportados (`Questionnaire`, `QuestionnaireResponse`, `AuditEvent`, `Observation`, `Attachment` propio), operations (`$publish`, `$archive`, `$clone`, `$populate`, `$finalize`) y parámetros de búsqueda.

### Recursos propios

Endpoints FHIR estándar:

- `GET/POST /Questionnaire` (módulo 001)
- `GET/POST /QuestionnaireResponse` (módulo 002)
- `GET /AuditEvent` (módulo 005)
- `GET /Observation` — sólo lectura; deriva de QR finalizadas o pasa-through del hospital.

### `POST /` (transaction Bundle)

Acepta `Bundle` tipo `transaction` para escrituras atómicas multi-recurso.

**Response 200**: `Bundle` tipo `transaction-response`.

## Cliente: API FHIR del hospital (consumo)

ChequeAR consume estos recursos del hospital:

- `GET /Patient/{hospitalId}` — datos demográficos.
- `GET /Observation?subject=Patient/{id}&code={loinc}&_sort=-date&_count=1` — última observación.
- `GET /Condition?subject=Patient/{id}` — antecedentes.
- `GET /Encounter?subject=Patient/{id}&status=in-progress` — encuentro activo.

**Configuración**:

```json
{
  "HospitalFhir": {
    "BaseUrl": "https://fhir.garrahan.local/fhir",
    "Auth": "Bearer | mTLS | ClientCredentials",
    "TimeoutMs": 5000,
    "Retries": 3,
    "CircuitBreaker": { "FailuresBeforeOpen": 5, "BreakDurationMs": 30000 }
  }
}
```

### Errores del cliente

| Caso | Comportamiento |
|---|---|
| 404 hospital | Devuelve null/`OperationOutcome` informativo, NO 500 |
| 5xx hospital | Reintento (3) con backoff exponencial; luego circuit breaker |
| Timeout | Aborta la operación caller, registra en log |

## Adaptador Oracle (interno, no expuesto)

Servicio interno `IOracleViewClient` consumido por `PrefillService` y endpoints de lectura.

```csharp
public interface IOracleViewClient {
    Task<IReadOnlyList<OracleRow>> QueryAsync(string viewName, IReadOnlyDictionary<string,object> filters);
}
```

**Vistas iniciales del MVP**:

- `VW_PACIENTE_DATOS_BASICOS` → mapea a `Patient`.
- `VW_PACIENTE_ANTECEDENTES` → mapea a `Bundle<Condition>`.
- `VW_PACIENTE_MEDICION` → mapea a `Bundle<Observation>`.

**Mapeo declarativo** en `Infrastructure/OracleMappings/*.yaml`:

```yaml
view: VW_PACIENTE_ANTECEDENTES
resourceType: Condition
identifierColumn: ID_PACIENTE
mappings:
  - column: DIAGNOSTICO_CIE10
    target: code.coding[0].code
    system: http://hl7.org/fhir/sid/icd-10
  - column: FECHA_DIAGNOSTICO
    target: onsetDateTime
```

## CapabilityStatement (resumen)

```json
{
  "resourceType": "CapabilityStatement",
  "status": "active",
  "fhirVersion": "5.0.0",
  "format": ["application/fhir+json"],
  "rest": [{
    "mode": "server",
    "resource": [
      { "type": "Questionnaire", "interaction": [{"code":"read"},{"code":"search-type"},{"code":"create"},{"code":"update"},{"code":"delete"}], "operation": [{"name":"publish","definition":"https://chequear.garrahan/OperationDefinition/publish"}] },
      { "type": "QuestionnaireResponse", "interaction": [{"code":"read"},{"code":"search-type"},{"code":"create"},{"code":"update"}], "operation": [{"name":"finalize","definition":"https://chequear.garrahan/OperationDefinition/finalize"}] },
      { "type": "AuditEvent", "interaction": [{"code":"read"},{"code":"search-type"}] }
    ]
  }]
}
```
