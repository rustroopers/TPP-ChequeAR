# Quickstart: Gestor y Editor de Cuestionarios

Guía para levantar el módulo en local y ejecutar el flujo end-to-end mínimo.

## Prerrequisitos

- Windows 10/11 o Windows Server 2016+
- Visual Studio 2022 con workloads ".NET desktop development" y "ASP.NET y desarrollo web"
- .NET Framework 4.7.1 Developer Pack
- SQL Server 2019 Express LocalDB (o instancia full)
- Docker Desktop (para tests de integración)
- Git

## Setup

```powershell
git clone <repo-url>
cd ChequeAR

# Restaurar paquetes
nuget restore ChequeAR.sln

# Aplicar migraciones EF
Update-Database -Project ChequeAR.Infrastructure -StartupProject ChequeAR.Api
```

`appsettings.local.json`:

```json
{
  "ConnectionStrings": {
    "ChequeAR": "Server=(localdb)\\MSSQLLocalDB;Database=ChequeAR;Trusted_Connection=True;"
  },
  "HospitalAuth": {
    "Authority": "https://auth-mock.local",
    "ClientId": "chequear-dev"
  },
  "Fhir": {
    "BaseUrl": "/fhir/r5"
  }
}
```

## Levantar

Desde Visual Studio: F5 sobre `ChequeAR.Api`. Por defecto escucha en `https://localhost:5001`.

## Smoke test

```powershell
# Token de desarrollo (modo bypass para entorno local)
$token = "Bearer dev-admin"

# 1. Crear cuestionario
curl -X POST https://localhost:5001/fhir/r5/Questionnaire `
  -H "Authorization: $token" `
  -H "Content-Type: application/fhir+json" `
  -d '@samples/questionnaire-trasplante.json'
# -> 201 Created, devuelve id

# 2. Listar borradores
curl "https://localhost:5001/fhir/r5/Questionnaire?status=draft" `
  -H "Authorization: $token"

# 3. Publicar
curl -X POST "https://localhost:5001/fhir/r5/Questionnaire/{id}/`$publish" `
  -H "Authorization: $token"
# -> 200 OK, status=active
```

## Tests

```powershell
# Unit
dotnet test tests/unit/ChequeAR.Domain.Tests

# Integration (requiere Docker)
docker compose -f tests/integration/docker-compose.yml up -d
dotnet test tests/integration/ChequeAR.Api.IntegrationTests

# Validación FHIR contra perfil
dotnet run --project tools/FhirValidator -- samples/questionnaire-trasplante.json
```

## Troubleshooting

| Síntoma | Causa | Solución |
|---|---|---|
| `500` al publicar | Validador FHIR rechaza recurso | Revisar `OperationOutcome` en la respuesta |
| `Update-Database` falla | LocalDB no inicializado | `sqllocaldb start MSSQLLocalDB` |
| Tests de integración cuelgan | Docker no corriendo | Iniciar Docker Desktop |
