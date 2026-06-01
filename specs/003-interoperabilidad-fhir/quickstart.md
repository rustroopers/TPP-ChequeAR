# Quickstart: Capa de Interoperabilidad y Almacenamiento

## Prerrequisitos

- Setup base módulo 001.
- Docker Desktop (para Oracle XE de tests).
- Acceso (opcional) a una instancia FHIR mock del hospital o uso de `WireMock.Net`.

## Configuración

`appsettings.local.json`:

```json
{
  "HospitalFhir": {
    "BaseUrl": "http://localhost:18080/fhir",
    "Auth": { "Type": "Bearer", "Token": "mock-dev-token" },
    "TimeoutMs": 5000,
    "Retries": 3
  },
  "Oracle": {
    "ConnectionString": "User Id=chequear_ro;Password=...;Data Source=localhost:1521/XEPDB1;",
    "PoolSize": 10
  },
  "FhirValidator": {
    "ProfilesPath": "src/ChequeAR.Infrastructure/Fhir/Profiles"
  }
}
```

## Levantar dependencias locales

```powershell
# Oracle XE de prueba
docker run -d --name oracle-xe -p 1521:1521 `
  -e ORACLE_PASSWORD=oracle gvenzl/oracle-xe:21

# Mock FHIR del hospital con HAPI
docker run -d --name hapi-fhir -p 18080:8080 hapiproject/hapi:latest
```

Crear las vistas/tablas de prueba:

```powershell
sqlplus chequear_ro/oracle@localhost:1521/XEPDB1 @tools/oracle-seed.sql
```

## Smoke test

```powershell
# CapabilityStatement
curl https://localhost:5001/fhir/r5/metadata -H "Accept: application/fhir+json"

# Lectura de paciente vía Oracle adapter
curl "https://localhost:5001/fhir/r5/Patient/PAC-001" `
  -H "Authorization: Bearer dev-medico"

# Lectura de Observation desde hospital (mock)
curl "https://localhost:5001/fhir/r5/Observation?subject=Patient/PAC-001&code=29463-7&_count=1" `
  -H "Authorization: Bearer dev-medico"
```

## Validación FHIR en CI

```powershell
dotnet run --project tools/FhirValidator -- `
  --profile-dir src/ChequeAR.Infrastructure/Fhir/Profiles `
  --resources tests/fixtures/fhir/**/*.json
```

Salida esperada: `0 errors, 0 warnings`.

## Troubleshooting

| Síntoma | Causa probable | Solución |
|---|---|---|
| `503` en consultas al hospital | Circuit-breaker abierto | Esperar `BreakDurationMs` o revisar conectividad |
| Mapping YAML no se aplica | Archivo fuera de `OracleMappings/` | Validar path y reiniciar el AppPool |
| Validación FHIR falla por extension | Perfil ChequeAR desactualizado | Re-generar perfil desde `Profiles/source/` |
