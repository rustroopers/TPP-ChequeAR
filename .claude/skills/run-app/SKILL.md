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

MSBuild de la VS instalada. **No hardcodear el path**: la carpeta lleva el numero de version
(`2022`, `18`, ...) y cambia cuando VS se actualiza. Resolverlo siempre con `vswhere`, que vive en
una ruta fija que Microsoft no mueve entre versiones. **Usar forward slashes en los paths del
.sln/.csproj** (en Git Bash los backslashes se comen).

```bash
VSWHERE="/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"
MSBUILD=$("$VSWHERE" -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe')
[ -n "$MSBUILD" ] || { echo "MSBuild no encontrado: falta el workload 'Desarrollo de escritorio' o VS no esta instalado"; exit 1; }

"$MSBUILD" "src/ChequeAR.sln" -t:Restore -v:minimal -nologo
"$MSBUILD" "src/ChequeAR.Api/ChequeAR.Api.csproj" -v:minimal -nologo -p:Configuration=Debug
```

- El `-find` lleva **backslashes** (es un patron de Windows, lo interpreta vswhere, no bash) y va en
  comillas simples para que bash no se los coma. El path que devuelve tambien viene con backslashes:
  se ejecuta bien siempre que se lo entrecomille (`"$MSBUILD"`), no hace falta convertirlo.
- Equivalente en PowerShell, si se prefiere:
  `& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"`

El warning `CS0108` de `AccountController.Profile()` es preexistente e inofensivo.

## Paso 2b — Migraciones EF pendientes (sólo si la app tira "model backing... has changed")

Si al abrir una pantalla que consulta datos aparece **"The model backing the 'ChequeARDbContext' context has changed since the database was created"**, es que hay una migración Code First sin aplicar en el Oracle de dev (típico después de un pull que trae migraciones nuevas). Se aplica con **`ef6.exe`** (EF6 6.5.1 ya no trae `migrate.exe`), copiándolo al `bin` de la Api para que resuelva EntityFramework + el provider de Oracle desde ahí.

```bash
# ef6.exe vive en el paquete NuGet restaurado (elegir la versión que referencia Infrastructure: 6.5.1)
EF6="$USERPROFILE/.nuget/packages/entityframework/6.5.1/tools/net45/win-x86/ef6.exe"
cp "$EF6" src/ChequeAR.Api/bin/ef6.exe

cd src/ChequeAR.Api/bin
# 1) DRY-RUN primero (--script): no toca la DB, confirma que el provider Oracle resuelve y muestra el DDL.
# 2) Sacar --script para aplicar de verdad. Termina en "Running Seed method." si salió OK.
./ef6.exe database update \
  --assembly ChequeAR.Infrastructure.dll \
  --migrations-config ChequeAR.Infrastructure.Migrations.Configuration \
  --config ../Web.config \
  --connection-string "User Id=CHEQUEAR;Password=Chequear_dev1;Data Source=localhost:1521/XEPDB1;" \
  --connection-provider "Oracle.ManagedDataAccess.Client" --verbose
```

- El `--config ../Web.config` es para la sección `<entityFramework>` que registra el provider `Oracle.ManagedDataAccess.Client`; la connection string se pasa explícita (misma que `name=ChequeAR` en el Web.config).
- Verificar que quedó aplicada: correr el mismo comando con `--script` → debe decir **"No pending explicit migrations."**
- **Limpieza**: borrar `src/ChequeAR.Api/bin/ef6.exe` (y `ef6.pdb` si se copió) para no dejarlo suelto en el repo.
- Equivale a `Update-Database` / `DbMigrator.Update()` del Package Manager Console de Visual Studio; usar cualquiera de los dos si se prefiere.

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
