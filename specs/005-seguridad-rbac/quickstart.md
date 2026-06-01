# Quickstart: Seguridad y Control de Accesos

## Prerrequisitos

- Setup base del proyecto.
- WireMock.Net o un mock simple del endpoint del hospital corriendo en local.

## Configuración

```json
{
  "HospitalAuth": {
    "LoginUrl": "https://auth-mock.local/login",
    "UserInfoUrl": "https://auth-mock.local/userinfo",
    "TimeoutMs": 3000
  },
  "Jwt": {
    "Issuer": "https://chequear.garrahan",
    "Audience": "chequear-api",
    "AccessTokenTtlMinutes": 60,
    "RefreshTokenTtlHours": 8,
    "SigningSecret": "ENV(CHEQUEAR_JWT_SECRET)"
  },
  "Audit": {
    "DispatcherQueueSize": 10000,
    "FlushIntervalMs": 1000
  }
}
```

Generar secret de dev:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
$env:CHEQUEAR_JWT_SECRET = "<paste>"
```

## Mock del hospital

```powershell
docker run -d -p 9000:9000 --name auth-mock `
  -v ${PWD}\tools\auth-mock\mappings:/home/wiremock/mappings `
  wiremock/wiremock
```

Mapping mínimo (`tools/auth-mock/mappings/login.json`):

```json
{
  "request": { "method": "POST", "url": "/login" },
  "response": {
    "status": 200,
    "jsonBody": {
      "user": { "id": "dev-medico", "displayName": "Dr. Dev" },
      "roles": ["Medico"],
      "specialties": ["hepato"]
    }
  }
}
```

## Smoke test

```powershell
# 1. Login
$res = curl -s -X POST https://localhost:5001/auth/login `
  -H "Content-Type: application/json" `
  -d '{"username":"dev","password":"x"}' | ConvertFrom-Json
$token = $res.accessToken

# 2. Acceso autorizado
curl https://localhost:5001/auth/me -H "Authorization: Bearer $token"

# 3. Acceso denegado por rol
curl -X POST https://localhost:5001/fhir/r5/Questionnaire `
  -H "Authorization: Bearer $token"
# -> 403 (Médico no puede crear)

# 4. Auditoría (como admin)
$adminToken = ...
curl "https://localhost:5001/fhir/r5/AuditEvent?agent.who=dev-medico" `
  -H "Authorization: Bearer $adminToken"
```

## Tests

```powershell
dotnet test tests/unit/Auth.Tests
dotnet test tests/integration/Auth.IntegrationTests   # WireMock corriendo
```

## Troubleshooting

| Síntoma | Causa | Solución |
|---|---|---|
| `401` con token "válido" | Reloj desincronizado o secret rotado | NTP + revisar secret en config |
| AuditEvent no aparece | Dispatcher saturado | Aumentar `DispatcherQueueSize`, revisar logs Serilog |
| Hospital responde lento | Timeout | Aumentar `TimeoutMs` con cuidado; preferir caché local |
