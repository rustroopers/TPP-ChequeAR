# Quickstart: Módulo de Análisis y Exportación

## Prerrequisitos

- Setup completo de los módulos 001 y 002.
- Dataset semilla (`tools/seed-reports.ps1`) con ≥100 `QuestionnaireResponse` finalizadas distribuidas en ≥2 cuestionarios.
- Hangfire dashboard accesible en `/hangfire`.

## Configuración

```json
{
  "Reports": {
    "MaxSyncRows": 10000,
    "MaxSyncBytes": 5242880,
    "ArtifactRetentionDays": 7,
    "ArtifactsPath": "C:\\ChequeAR\\Artifacts"
  },
  "Hangfire": {
    "ConnectionString": "Server=...;Database=ChequeAR_Jobs;..."
  }
}
```

## Smoke test

```powershell
# 1. Seed
.\tools\seed-reports.ps1 -ResponseCount 500

# 2. Consulta básica
curl -X POST https://localhost:5001/api/reports/query `
  -H "Authorization: Bearer dev-jefe" `
  -H "Content-Type: application/json" `
  -d '{
    "questionnaireId": "<qid>",
    "filters": {"logic":"AND","predicates":[{"linkId":"edad","op":">","value":5}]},
    "page": 1,
    "pageSize": 10
  }'

# 3. Exportar Excel (síncrono)
curl -X POST https://localhost:5001/api/reports/export `
  -H "Authorization: Bearer dev-jefe" `
  -H "Content-Type: application/json" `
  -o reporte.xlsx `
  -d '{ ..., "format": "xlsx" }'

# 4. Job asincrónico (forzar con > 10k filas)
curl -X POST https://localhost:5001/api/reports/export -d '{..., "format":"csv"}'
# -> { "jobId": "abc" }

# 5. Estado
curl https://localhost:5001/api/reports/jobs/abc

# 6. Descargar
curl https://localhost:5001/api/reports/artifacts/<id> -o reporte.csv
```

## Tests

```powershell
dotnet test tests/integration/Reports.IntegrationTests
# Incluye snapshot del XLSX generado: cambios deliberados requieren actualizar el snapshot
```

## Troubleshooting

| Síntoma | Solución |
|---|---|
| Excel mal codificado | Verificar BOM UTF-8 en CSV y separador `;` |
| Job en `pending` indefinido | Revisar dashboard Hangfire, posible storage con problemas |
| Permission denied al guardar artifact | Conceder write al usuario del AppPool sobre `ArtifactsPath` |
