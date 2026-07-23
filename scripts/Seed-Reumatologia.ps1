<#
.SYNOPSIS
    Seed de datos de demo para ChequeAR: cuestionarios de reumatología pediátrica
    y fichas cargadas sobre los pacientes reales del Hospital Garrahan.

.DESCRIPTION
    Llama a la API REST del server (misma que usa el front) para:
      1. Crear 5 cuestionarios de reumatología (no muy complejos) y publicarlos.
      2. Crear fichas sobre los pacientes REALES del hospital (VW_PACIENTE),
         eligiendo para cada patología pacientes de la edad adecuada y
         finalizando la mayoría.

    Idempotente: si un cuestionario con el mismo título ya existe, no lo recrea.

    El token JWT se emite localmente con el secreto del Web.config (dev-only),
    con rol admin, para poder llamar a los endpoints protegidos.

.PARAMETER BaseUrl
    URL del server. Default http://localhost:8080

.PARAMETER WebConfig
    Ruta al Web.config del que se lee el secreto/issuer/audience del JWT.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Seed-Reumatologia.ps1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$WebConfig
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

# Resolver ruta del Web.config de forma robusta ($PSScriptRoot puede venir vacío).
if ([string]::IsNullOrWhiteSpace($WebConfig)) {
    $root = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($root)) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $WebConfig = Join-Path $root '..\src\ChequeAR.Api\Web.config'
}

# ---------------------------------------------------------------------------
# 1) Token JWT (HMAC-SHA256) con el secreto/issuer/audience del Web.config
# ---------------------------------------------------------------------------
function Get-JwtConfig {
    param([string]$Path)
    [xml]$xml = Get-Content -Path $Path -Raw
    $get = { param($k) ($xml.configuration.appSettings.add | Where-Object { $_.key -eq $k }).value }
    return @{
        Secret   = (& $get 'Jwt:SigningSecret')
        Issuer   = (& $get 'Jwt:Issuer')
        Audience = (& $get 'Jwt:Audience')
    }
}

function ConvertTo-Base64Url([byte[]]$bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-AdminJwt {
    param($Cfg)
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $exp = $now + 3600
    $roleUri = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'
    $header = @{ alg = 'HS256'; typ = 'JWT' } | ConvertTo-Json -Compress
    $payloadObj = [ordered]@{
        sub = 'seed-admin'; name = 'Seed Script'; jti = [Guid]::NewGuid().ToString()
        iss = $Cfg.Issuer; aud = $Cfg.Audience; nbf = $now; iat = $now; exp = $exp
    }
    $payloadObj[$roleUri] = @('Administrador', 'Jefe', 'Medico')
    $payload = $payloadObj | ConvertTo-Json -Compress
    $h = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))
    $p = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
    $hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Cfg.Secret))
    $sig = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes("$h.$p")))
    return "$h.$p.$sig"
}

# ---------------------------------------------------------------------------
# 2) Helpers HTTP (UTF-8 explícito para acentos)
# ---------------------------------------------------------------------------
$script:Headers = $null

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)
    $uri = "$BaseUrl$Path"
    $params = @{ Uri = $uri; Method = $Method; Headers = $script:Headers; TimeoutSec = 60 }
    if ($null -ne $Body) {
        $json = ($Body | ConvertTo-Json -Depth 8 -Compress)
        $params.Body = [Text.Encoding]::UTF8.GetBytes($json)
        $params.ContentType = 'application/json; charset=utf-8'
    }
    return Invoke-RestMethod @params
}

# ---------------------------------------------------------------------------
# 3) Definición de cuestionarios de reumatología pediátrica (simples)
# ---------------------------------------------------------------------------
function New-Question {
    param([string]$LinkId, [string]$Text, [string]$Type, [bool]$Required, [string[]]$Options)
    $q = [ordered]@{ linkId = $LinkId; text = $Text; type = $Type; required = $Required }
    if ($Options) {
        $opts = @()
        $i = 0
        # code = value = display, igual que lo que manda el editor de cuestionarios.
        # Antes se generaba un slug (minusculas, no-alfanumerico -> '_') que perdia
        # informacion y colisionaba: 'Poliarticular FR+' y 'Poliarticular FR-' daban los dos
        # 'poliarticular_fr_', o sea dos subtipos clinicamente distintos bajo el mismo codigo,
        # imposibles de separar en un reporte. Los acentos tambien se perdian
        # ('Sistemica' -> 'sist_mica').
        foreach ($o in $Options) { $opts += [ordered]@{ value = $o; display = $o; code = $o; orderIndex = $i }; $i++ }
        $q.options = $opts
    }
    return $q
}

$questionnaires = @(
    [ordered]@{
        title         = 'AIJ - Evaluación inicial'
        specialtyCode = 'reuma'
        specialtyDisplay = 'Reumatología'
        description   = 'Artritis Idiopática Juvenil: evaluación reumatológica inicial.'
        questions     = @(
            (New-Question 'edad'        'Edad'                              'integer' $true  $null),
            (New-Question 'nro_artic'   'N° de articulaciones afectadas'    'integer' $true  $null),
            (New-Question 'rigidez_am'  'Rigidez matutina > 15 min'         'boolean' $true  $null),
            (New-Question 'eva_dolor'   'Dolor (EVA 0-10)'                  'integer' $true  $null),
            (New-Question 'subtipo'     'Subtipo'                           'choice'  $true  @('Oligoarticular', 'Poliarticular FR+', 'Poliarticular FR-', 'Sistémica', 'Entesitis', 'Psoriásica'))
        )
    },
    [ordered]@{
        title         = 'LES juvenil - Seguimiento'
        specialtyCode = 'reuma'
        specialtyDisplay = 'Reumatología'
        description   = 'Lupus Eritematoso Sistémico juvenil: control evolutivo.'
        questions     = @(
            (New-Question 'fatiga'      'Fatiga'                            'boolean' $true  $null),
            (New-Question 'rash_malar'  'Rash malar'                        'boolean' $true  $null),
            (New-Question 'artralgias'  'Artralgias'                        'boolean' $true  $null),
            (New-Question 'proteinuria' 'Proteinuria'                       'choice'  $true  @('Ausente', 'Leve', 'Moderada', 'Severa')),
            (New-Question 'sledai'      'SLEDAI'                            'integer' $false $null)
        )
    },
    [ordered]@{
        title         = 'Dolor musculoesquelético - Triaje reumatológico'
        specialtyCode = 'reuma'
        specialtyDisplay = 'Reumatología'
        description   = 'Triaje de dolor musculoesquelético en pediatría.'
        questions     = @(
            (New-Question 'localizacion' 'Localización del dolor'           'choice'  $true  @('Rodilla', 'Cadera', 'Muñeca', 'Tobillo', 'Columna', 'Generalizado')),
            (New-Question 'duracion'     'Duración'                         'choice'  $true  @('< 6 semanas', '6-12 semanas', '> 12 semanas')),
            (New-Question 'inflamacion'  'Inflamación articular'            'boolean' $true  $null),
            (New-Question 'antec_fam'    'Antecedentes familiares reumáticos' 'boolean' $false $null),
            (New-Question 'impacto'      'Impacto funcional (0-10)'         'integer' $true  $null)
        )
    },
    [ordered]@{
        title         = 'Esclerodermia juvenil - Evaluación'
        specialtyCode = 'reuma'
        specialtyDisplay = 'Reumatología'
        description   = 'Esclerosis sistémica / localizada juvenil: evaluación cutánea y sistémica.'
        questions     = @(
            (New-Question 'tipo'          'Tipo'                             'choice'  $true  @('Localizada (morfea)', 'Sistémica')),
            (New-Question 'rodnan'        'Score de Rodnan modificado'       'integer' $false $null),
            (New-Question 'raynaud'       'Fenómeno de Raynaud'              'boolean' $true  $null),
            (New-Question 'ulceras'       'Úlceras digitales'                'boolean' $true  $null),
            (New-Question 'disfagia'      'Disfagia'                         'boolean' $false $null)
        )
    },
    [ordered]@{
        title         = 'Vasculitis (Kawasaki/PAN) - Seguimiento'
        specialtyCode = 'reuma'
        specialtyDisplay = 'Reumatología'
        description   = 'Seguimiento de vasculitis pediátricas.'
        questions     = @(
            (New-Question 'entidad'       'Entidad'                          'choice'  $true  @('Kawasaki', 'PAN', 'Púrpura de Schönlein-Henoch', 'Takayasu')),
            (New-Question 'fiebre_dias'   'Días de fiebre'                   'integer' $false $null),
            (New-Question 'pcr'           'PCR (mg/L)'                       'decimal' $false $null),
            (New-Question 'compromiso'    'Compromiso de órgano'            'boolean' $true  $null),
            (New-Question 'tratamiento'   'Tratamiento actual'               'choice'  $false @('IGIV', 'Corticoides', 'Inmunosupresor', 'Ninguno'))
        )
    }
)

# ---------------------------------------------------------------------------
# 4) Pacientes: los REALES del hospital (VW_PACIENTE), no una lista inventada
# ---------------------------------------------------------------------------
# Se consultan por la misma API que usa el front, asi el seed ejercita el camino
# real de integracion. Antes el script traia 17 pacientes propios que no existian
# en VW_PACIENTE: sobrevivian solo como Patient "contained" dentro de cada
# respuesta (modo degradado), y convivian con los del hospital como dos
# poblaciones separadas.
function Get-HospitalPatients {
    $resp = Invoke-Api 'GET' '/api/patients?q=a'
    $list = @()
    foreach ($r in $resp.results) {
        $edad = 0
        if ($r.AgeLabel -match '^(\d+)') { $edad = [int]$Matches[1] }
        $list += [ordered]@{ hc = $r.HospitalId; name = $r.FullName; age = $edad }
    }
    return $list
}

# ---------------------------------------------------------------------------
# 5) Respuestas (encuentros). Cada answer usa el linkId del cuestionario.
#    tipos: int / bool / choice(string+code)
# ---------------------------------------------------------------------------
function New-Item {
    param([string]$LinkId, [string]$QType, $Value)
    $it = [ordered]@{ id = [Guid]::NewGuid().ToString(); linkId = $LinkId; questionType = $QType }
    switch ($QType) {
        'integer' { $it.valueInteger = [int]$Value }
        'decimal' { $it.valueDecimal = [decimal]$Value }
        'boolean' { $it.valueBoolean = [bool]$Value }
        # El renderer guarda la opcion elegida tal cual en valueCode y no escribe
        # valueString. El seed hacia las dos cosas y ademas slugificaba el codigo, asi que la
        # misma categoria quedaba con dos etiquetas distintas segun quien hubiera cargado la
        # ficha, y la distribucion de un reporte se partia en dos barras.
        'choice'  { $it.valueCode = [string]$Value }
        default   { $it.valueString = [string]$Value }
    }
    return $it
}

# Perfil etario de cada patologia, para que la ficha sea clinicamente verosimil:
# Kawasaki es sobre todo del lactante/preescolar y el LES juvenil del adolescente.
$agePreference = @{
    'AIJ - Evaluación inicial'                        = @{ min = 2;  max = 16 }
    'LES juvenil - Seguimiento'                       = @{ min = 10; max = 17 }
    'Dolor musculoesquelético - Triaje reumatológico' = @{ min = 5;  max = 17 }
    'Esclerodermia juvenil - Evaluación'              = @{ min = 6;  max = 17 }
    'Vasculitis (Kawasaki/PAN) - Seguimiento'         = @{ min = 1;  max = 8 }
}

# Respuestas por cuestionario. La edad no se inventa: sale del paciente real.
function New-Answers {
    param([string]$Title, [int]$Age, [int]$Seed)
    $rnd = [Random]::new($Seed)
    switch ($Title) {
        'AIJ - Evaluación inicial' {
            $subtipos = @('Oligoarticular', 'Poliarticular FR+', 'Poliarticular FR-', 'Sistémica', 'Entesitis', 'Psoriásica')
            $sub = $subtipos[$rnd.Next(0, $subtipos.Count)]
            # La oligoarticular afecta <=4 articulaciones por definicion; la poli, mas.
            $artic = if ($sub -eq 'Oligoarticular') { $rnd.Next(1, 5) } else { $rnd.Next(5, 12) }
            return @{ edad = $Age; nro_artic = $artic; rigidez_am = ($rnd.Next(0,10) -lt 7); eva_dolor = $rnd.Next(2, 9); subtipo = $sub }
        }
        'LES juvenil - Seguimiento' {
            $prot = @('Ausente', 'Leve', 'Moderada', 'Severa')[$rnd.Next(0,4)]
            # SLEDAI acompana al compromiso renal: a mayor proteinuria, mas actividad.
            $sledai = switch ($prot) { 'Ausente' { $rnd.Next(0,5) } 'Leve' { $rnd.Next(4,10) } 'Moderada' { $rnd.Next(8,16) } default { $rnd.Next(14,25) } }
            return @{ fatiga = ($rnd.Next(0,10) -lt 7); rash_malar = ($rnd.Next(0,10) -lt 5); artralgias = ($rnd.Next(0,10) -lt 8); proteinuria = $prot; sledai = $sledai }
        }
        'Dolor musculoesquelético - Triaje reumatológico' {
            $loc = @('Rodilla', 'Cadera', 'Muñeca', 'Tobillo', 'Columna', 'Generalizado')[$rnd.Next(0,6)]
            $dur = @('< 6 semanas', '6-12 semanas', '> 12 semanas')[$rnd.Next(0,3)]
            $infl = ($rnd.Next(0,10) -lt 5)
            # Con inflamacion el impacto funcional suele ser mayor.
            $imp = if ($infl) { $rnd.Next(4, 10) } else { $rnd.Next(1, 6) }
            return @{ localizacion = $loc; duracion = $dur; inflamacion = $infl; antec_fam = ($rnd.Next(0,10) -lt 4); impacto = $imp }
        }
        'Esclerodermia juvenil - Evaluación' {
            $sistemica = ($rnd.Next(0,10) -lt 4)
            $tipo = if ($sistemica) { 'Sistémica' } else { 'Localizada (morfea)' }
            # Rodnan alto, Raynaud, ulceras y disfagia son propios de la forma sistemica.
            return @{ tipo = $tipo
                      rodnan   = if ($sistemica) { $rnd.Next(12, 28) } else { $rnd.Next(1, 8) }
                      raynaud  = $sistemica
                      ulceras  = ($sistemica -and ($rnd.Next(0,10) -lt 6))
                      disfagia = ($sistemica -and ($rnd.Next(0,10) -lt 5)) }
        }
        default {
            # Vasculitis: Kawasaki en los mas chicos, con fiebre prolongada y PCR alta.
            $entidad = if ($Age -le 5) { 'Kawasaki' } else { @('PAN', 'Púrpura de Schönlein-Henoch', 'Takayasu')[$rnd.Next(0,3)] }
            $kawa = ($entidad -eq 'Kawasaki')
            return @{ entidad = $entidad
                      fiebre_dias = if ($kawa) { $rnd.Next(5, 12) } else { $rnd.Next(0, 4) }
                      pcr         = [Math]::Round($(if ($kawa) { $rnd.Next(600, 1600) / 10.0 } else { $rnd.Next(20, 400) / 10.0 }), 1)
                      compromiso  = ($rnd.Next(0,10) -lt 5)
                      tratamiento = if ($kawa) { 'IGIV' } else { @('Corticoides', 'Inmunosupresor', 'Ninguno')[$rnd.Next(0,3)] } }
        }
    }
}

# ---------------------------------------------------------------------------
# EJECUCIÓN
# ---------------------------------------------------------------------------
Write-Host "== ChequeAR :: Seed reumatología ==" -ForegroundColor Cyan
Write-Host "Base URL: $BaseUrl"

$cfg = Get-JwtConfig -Path $WebConfig
if ([string]::IsNullOrWhiteSpace($cfg.Secret)) { throw "No pude leer Jwt:SigningSecret de $WebConfig" }
$token = New-AdminJwt -Cfg $cfg
$script:Headers = @{ Authorization = "Bearer $token" }

# Sanity: server arriba
try { Invoke-RestMethod "$BaseUrl/fhir/r5/metadata" -TimeoutSec 20 | Out-Null }
catch { throw "El server no responde en $BaseUrl. Levantá IIS Express primero." }

# --- Cuestionarios (crear + publicar) ---
$qByTitle = @{}
foreach ($def in $questionnaires) {
    $existing = Invoke-Api 'GET' ("/fhir/r5/Questionnaire?title:contains=" + [Uri]::EscapeDataString($def.title) + "&_count=5")
    $match = $null
    if ($existing.total -gt 0) { $match = $existing.entry | Where-Object { $_.title -eq $def.title } | Select-Object -First 1 }

    if ($match) {
        Write-Host ("  = Ya existe: {0} (id {1})" -f $def.title, $match.Id) -ForegroundColor DarkYellow
        $qByTitle[$def.title] = @{ id = $match.Id; versionId = $match.CurrentVersionId }
        continue
    }

    $created = Invoke-Api 'POST' '/fhir/r5/Questionnaire' $def
    Write-Host ("  + Creado: {0} (id {1}, {2} preguntas)" -f $created.title, $created.id, $created.questions.Count) -ForegroundColor Green

    # Publicar SIN mandar fhirJson: el server serializa el cuestionario completo desde la
    # base (FhirQuestionnaireBuilder). Antes se mandaba un recurso mínimo con sólo
    # resourceType para saltear el validador, pero eso congelaba una versión sin preguntas
    # y la ficha se renderizaba vacía: la carga lee de la versión, no de la entidad viva.
    $publishBody = @{ changeNote = 'Versión inicial (seed de reumatología)' }
    $published = Invoke-Api 'POST' ("/fhir/r5/Questionnaire/$($created.id)/`$publish") $publishBody
    Write-Host ("    -> publicado, status={0}, versionId={1}" -f $published.status, $published.currentVersionId) -ForegroundColor DarkGreen
    $qByTitle[$def.title] = @{ id = $created.id; versionId = $published.currentVersionId }
}

# --- Respuestas / encuentros sobre pacientes REALES del hospital ---
$patients = Get-HospitalPatients
if ($patients.Count -eq 0) {
    Write-Host "  ! El hospital no devolvio pacientes: no se pueden cargar fichas." -ForegroundColor Red
    Write-Host "    Revisa VW_PACIENTE y Oracle:ConnectionString en el Web.config." -ForegroundColor Red
    return
}
Write-Host ("  Pacientes del hospital disponibles: {0}" -f $patients.Count) -ForegroundColor DarkCyan

# NO se manda Patient "contained": estos pacientes existen de verdad en VW_PACIENTE y
# el sistema los resuelve contra el hospital. Embeberlos enmascararia una caida de esa
# integracion, que es justo lo que queremos poder ver.
$respCount = 0
$seedN = 0
$usadas = @{}   # HC|titulo ya cargado, para no repetir la misma ficha al mismo paciente

foreach ($def in $questionnaires) {
    $q = $qByTitle[$def.title]
    if (-not $q) { Write-Host ("  ! Sin cuestionario para: {0}" -f $def.title) -ForegroundColor Red; continue }

    $typeByLink = @{}
    foreach ($qq in $def.questions) { $typeByLink[$qq.linkId] = $qq.type }

    # Pacientes cuya edad real encaja con la patologia.
    $rango = $agePreference[$def.title]
    $candidatos = @($patients | Where-Object { $_.age -ge $rango.min -and $_.age -le $rango.max })
    if ($candidatos.Count -eq 0) { $candidatos = @($patients) }

    # 4 fichas por cuestionario, alternando finalizadas y en curso.
    $cuantas = [Math]::Min(4, $candidatos.Count)
    for ($i = 0; $i -lt $cuantas; $i++) {
        $pat = $candidatos[($seedN * 3 + $i * 7) % $candidatos.Count]
        $clave = "$($pat.hc)|$($def.title)"
        if ($usadas.ContainsKey($clave)) { continue }
        $usadas[$clave] = $true

        $seedN++
        $answers = New-Answers $def.title $pat.age $seedN
        $finalize = ($i -lt 3)   # 3 de cada 4 quedan finalizadas

        $items = @()
        foreach ($k in $answers.Keys) { $items += (New-Item $k $typeByLink[$k] $answers[$k]) }

        $body = [ordered]@{
            questionnaireId        = $q.id
            questionnaireVersionId = $q.versionId
            patientHospitalId      = $pat.hc
            encounterId            = ("ENC-" + $pat.hc + "-" + (Get-Random -Minimum 1000 -Maximum 9999))
            items                  = $items
        }

        $resp = Invoke-Api 'POST' '/fhir/r5/QuestionnaireResponse' $body
        $respCount++
        $tag = ''
        if ($finalize) {
            Invoke-Api 'POST' ("/fhir/r5/QuestionnaireResponse/$($resp.id)/`$finalize") @{} | Out-Null
            $tag = ' [finalizada]'
        }
        Write-Host ("  + {0} ({1}, {2} anios) <- {3}{4}" -f $pat.name, $pat.hc, $pat.age, $def.title, $tag) -ForegroundColor Green
    }
}

# La vista vw_FlatResponses la crea la migración AddFlatResponsesView, no este script.
# Antes se creaba acá con sintaxis SQL Server (dbo., SqlConnection) contra una base
# Oracle, así que fallaba siempre y el reporte quedaba sin vista.

Write-Host ""
Write-Host ("Listo. Cuestionarios: {0} | Respuestas creadas: {1}" -f $qByTitle.Count, $respCount) -ForegroundColor Cyan
Write-Host "Probá en: $BaseUrl/Questionnaires   y   $BaseUrl/Encounters/Search"
