---
name: "run-app"
description: "Levanta ChequeAR.Api en local: arranca el TNS Listener de Oracle, hace build con MSBuild (VS2022) y corre IIS Express en https://localhost:44301. Verifica Snowstorm. Sin Docker (Oracle es servicio nativo de Windows)."
argument-hint: "Opcional: 'solo-run' para saltear el build si ya compilaste"
user-invocable: true
disable-model-invocation: false
---

# Levantar ChequeAR en local

Objetivo: dejar la app corriendo en **https://localhost:44301** y confirmar que responde 200.

**Contexto de entorno:** Oracle corre como servicio nativo de Windows en las máquinas del equipo — **NO usar Docker** para la base. Docker Desktop suele estar apagado; eso es esperado. Snowstorm (terminológico SNOMED) corre aparte y es **opcional**: si no está, sólo se cae el autocomplete del editor de cuestionarios (503), el resto de la app funciona.

## Paso 1 — Oracle (TNS Listener)

El servicio `OracleServiceXE` suele estar corriendo, pero el **TNS Listener `OracleOraDB21Home1TNSListener` suele quedar detenido** — sin él nada conecta al 1521.

```powershell
Start-Service OracleOraDB21Home1TNSListener
(Get-Service OracleOraDB21Home1TNSListener).Status          # -> Running
Test-NetConnection localhost -Port 1521 -InformationLevel Quiet   # -> True
```

## Paso 2 — Build (saltear si el argumento es `solo-run`)

MSBuild de VS2022 Community. Restore primero, luego build del proyecto Api. **Usar forward slashes en los paths del .sln/.csproj** (en Git Bash los backslashes se comen).

```bash
MSBUILD="C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/ChequeAR.sln" -t:Restore -v:minimal -nologo
"$MSBUILD" "src/ChequeAR.Api/ChequeAR.Api.csproj" -v:minimal -nologo -p:Configuration=Debug
```

El warning `CS0108` de `AccountController.Profile()` es preexistente e inofensivo.

## Paso 3 — Correr IIS Express (en background)

```bash
"C:/Program Files/IIS Express/iisexpress.exe" \
  /config:"src/.vs/ChequeAR/config/applicationhost.config" /site:ChequeAR.Api
```

- Queda en **https://localhost:44301** (cert dev autofirmado).
- El fallo al registrar `http://localhost:50000/` ("está siendo utilizado por otro proceso") es **esperado y no importa** — el binding https es el que se usa.

## Paso 4 — Verificar que responde

El primer request compila ASP.NET (JIT) y puede tardar ~20-40s. Ignorar el cert autofirmado (Windows PowerShell 5.1 no tiene `-SkipCertificateCheck`, hay que confiar en todos los certs):

```powershell
add-type @'
using System.Net; using System.Security.Cryptography.X509Certificates;
public class TrustAll : ICertificatePolicy { public bool CheckValidationResult(ServicePoint s, X509Certificate c, WebRequest r, int p){return true;} }
'@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
(Invoke-WebRequest 'https://localhost:44301/' -UseBasicParsing -TimeoutSec 40).StatusCode   # -> 200
```

## Paso 5 — Verificar / levantar Snowstorm (opcional)

Endpoint configurado en `src/ChequeAR.Api/Web.config` → `Terminology:BaseUrl` = `http://localhost:18081/fhir`. Alimenta el autocomplete de códigos SNOMED del editor de cuestionarios. Es **opcional**: si no está, ese autocomplete responde 503 pero el resto de la app anda.

**Snowstorm SÍ corre en Docker** (`infra/snowstorm/docker-compose.yml`) — es la única pieza con Docker; Oracle no. El release argentino/castellano ya está importado y vive en el volumen `snowstorm_snowstorm-es-data` (596k conceptos), así que sólo hay que **encender** los contenedores, no reimportar.

1. **Arrancar Docker Desktop** si el daemon no responde (`docker version` falla):
   ```powershell
   Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
   # esperar en loop hasta que 'docker version' devuelva exit 0 (puede tardar 1-2 min)
   ```
2. **Levantar el stack** (reusa los contenedores existentes; NO usar `down -v`, borra el índice):
   ```bash
   cd infra/snowstorm && docker compose up -d
   ```
   Snowstorm tarda ~1-2 min extra tras que Elasticsearch quede `healthy`. Está listo cuando `http://localhost:18081/fhir/metadata` da 200.

3. **Verificar la expansión real del editor.** GOTCHA: Snowstorm responde `application/fhir+json`, que Windows PowerShell trata como binario → `Invoke-WebRequest .Content` vuelve como `byte[]` y `ConvertFrom-Json` falla en silencio (parece vacío aunque anda). Hay que decodificar UTF-8:
   ```powershell
   $r = Invoke-WebRequest 'http://localhost:18081/fhir/ValueSet/$expand?url=http://snomed.info/sct?fhir_vs=ecl/%3C%3C404684003&filter=dolor&count=5&displayLanguage=es' -UseBasicParsing -TimeoutSec 30
   $txt = if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { $r.Content }
   ($txt | ConvertFrom-Json).expansion.contains | Select-Object -First 5 code, display
   # -> conceptos en español (dolor, dolor anal, dolor óseo, ...). Si total>0, está andando.
   ```

Si el server responde pero el expand da `total=0`, el índice está vacío → hay que reimportar el release RF2 (ver `infra/snowstorm/README.md`, tarda ~50 min).
