<#
.SYNOPSIS
    Seed de datos de demo para ChequeAR: cuestionarios de reumatología pediátrica
    y pacientes/respuestas simulados del Hospital Garrahan.

.DESCRIPTION
    Llama a la API REST del server (misma que usa el front) para:
      1. Crear 5 cuestionarios de reumatología (no muy complejos) y publicarlos.
      2. Crear respuestas (encuentros) para 17 pacientes pediátricos simulados,
         finalizando algunas.
      3. Crear la vista SQL vw_FlatResponses (reportería / exportación a Excel).

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
        foreach ($o in $Options) { $opts += [ordered]@{ value = $o; display = $o; code = ($o.ToLower() -replace '[^a-z0-9]+', '_'); orderIndex = $i }; $i++ }
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
# 4) Pacientes pediátricos simulados del Garrahan
# ---------------------------------------------------------------------------
$patients = @(
    [ordered]@{ hc = '458821'; name = 'Gómez, Martina';       dni = '54231987'; sex = 'F'; birth = '2016-03-12' },
    [ordered]@{ hc = '461203'; name = 'Fernández, Thiago';    dni = '51120345'; sex = 'M'; birth = '2013-09-01' },
    [ordered]@{ hc = '449517'; name = 'Rodríguez, Valentina'; dni = '55780112'; sex = 'F'; birth = '2018-07-25' },
    [ordered]@{ hc = '470655'; name = 'Sosa, Benjamín';       dni = '49903221'; sex = 'M'; birth = '2011-01-18' },
    [ordered]@{ hc = '455390'; name = 'Díaz, Catalina';       dni = '56612009'; sex = 'F'; birth = '2019-11-30' },
    [ordered]@{ hc = '462818'; name = 'Ramírez, Joaquín';     dni = '52014477'; sex = 'M'; birth = '2014-05-22' },
    [ordered]@{ hc = '451077'; name = 'Torres, Emma';         dni = '55329064'; sex = 'F'; birth = '2017-02-08' },
    [ordered]@{ hc = '473500'; name = 'Acosta, Lautaro';      dni = '48870512'; sex = 'M'; birth = '2010-08-14' },
    [ordered]@{ hc = '459940'; name = 'Molina, Renata';       dni = '54990318'; sex = 'F'; birth = '2016-12-03' },
    [ordered]@{ hc = '466231'; name = 'Silva, Bautista';      dni = '50662190'; sex = 'M'; birth = '2012-06-27' },
    [ordered]@{ hc = '448319'; name = 'Romero, Isabella';     dni = '56100845'; sex = 'F'; birth = '2019-04-11' },
    [ordered]@{ hc = '471884'; name = 'Herrera, Dante';       dni = '49215703'; sex = 'M'; birth = '2011-10-05' },
    [ordered]@{ hc = '453662'; name = 'Castro, Olivia';       dni = '55471229'; sex = 'F'; birth = '2017-09-19' },
    [ordered]@{ hc = '468075'; name = 'Vega, Ciro';           dni = '50338816'; sex = 'M'; birth = '2013-03-30' },
    [ordered]@{ hc = '457413'; name = 'Ríos, Mía';            dni = '55884471'; sex = 'F'; birth = '2018-01-24' },
    [ordered]@{ hc = '464590'; name = 'Núñez, Felipe';        dni = '50079924'; sex = 'M'; birth = '2012-11-16' },
    [ordered]@{ hc = '452201'; name = 'Ferreyra, Delfina';    dni = '55602138'; sex = 'F'; birth = '2017-07-02' }
)

# ---------------------------------------------------------------------------
# 5) Respuestas (encuentros) por paciente. Cada answer usa el linkId del cuestionario.
#    tipos: int / bool / choice(string+code)
# ---------------------------------------------------------------------------
function New-Item {
    param([string]$LinkId, [string]$QType, $Value)
    $it = [ordered]@{ id = [Guid]::NewGuid().ToString(); linkId = $LinkId; questionType = $QType }
    switch ($QType) {
        'integer' { $it.valueInteger = [int]$Value }
        'decimal' { $it.valueDecimal = [decimal]$Value }
        'boolean' { $it.valueBoolean = [bool]$Value }
        'choice'  { $it.valueString = [string]$Value; $it.valueCode = ($Value.ToLower() -replace '[^a-z0-9]+', '_') }
        default   { $it.valueString = [string]$Value }
    }
    return $it
}

# Plan de encuentros: patientIndex, questionnaireKey (title), answers(linkId->value), finalize
$encounters = @(
    @{ p = 0; q = 'AIJ - Evaluación inicial';                        finalize = $true;  a = @{ edad = 9;  nro_artic = 3; rigidez_am = $true;  eva_dolor = 6; subtipo = 'Oligoarticular' } },
    @{ p = 1; q = 'AIJ - Evaluación inicial';                        finalize = $true;  a = @{ edad = 12; nro_artic = 7; rigidez_am = $true;  eva_dolor = 8; subtipo = 'Poliarticular FR-' } },
    @{ p = 2; q = 'LES juvenil - Seguimiento';                       finalize = $true;  a = @{ fatiga = $true; rash_malar = $true; artralgias = $true; proteinuria = 'Leve'; sledai = 8 } },
    @{ p = 3; q = 'LES juvenil - Seguimiento';                       finalize = $false; a = @{ fatiga = $true; rash_malar = $false; artralgias = $true; proteinuria = 'Moderada'; sledai = 12 } },
    @{ p = 4; q = 'Dolor musculoesquelético - Triaje reumatológico'; finalize = $true;  a = @{ localizacion = 'Rodilla'; duracion = '6-12 semanas'; inflamacion = $true; antec_fam = $true; impacto = 5 } },
    @{ p = 0; q = 'Dolor musculoesquelético - Triaje reumatológico'; finalize = $false; a = @{ localizacion = 'Tobillo'; duracion = '< 6 semanas'; inflamacion = $false; antec_fam = $false; impacto = 2 } },
    @{ p = 5;  q = 'AIJ - Evaluación inicial';                        finalize = $true;  a = @{ edad = 11; nro_artic = 2; rigidez_am = $false; eva_dolor = 4; subtipo = 'Oligoarticular' } },
    @{ p = 6;  q = 'AIJ - Evaluación inicial';                        finalize = $true;  a = @{ edad = 8;  nro_artic = 5; rigidez_am = $true;  eva_dolor = 7; subtipo = 'Poliarticular FR+' } },
    @{ p = 7;  q = 'AIJ - Evaluación inicial';                        finalize = $false; a = @{ edad = 14; nro_artic = 1; rigidez_am = $false; eva_dolor = 3; subtipo = 'Entesitis' } },
    @{ p = 8;  q = 'LES juvenil - Seguimiento';                       finalize = $true;  a = @{ fatiga = $true;  rash_malar = $true;  artralgias = $false; proteinuria = 'Severa';  sledai = 16 } },
    @{ p = 9;  q = 'LES juvenil - Seguimiento';                       finalize = $true;  a = @{ fatiga = $false; rash_malar = $false; artralgias = $true;  proteinuria = 'Ausente'; sledai = 2 } },
    @{ p = 10; q = 'Dolor musculoesquelético - Triaje reumatológico'; finalize = $true;  a = @{ localizacion = 'Cadera';   duracion = '> 12 semanas'; inflamacion = $true;  antec_fam = $false; impacto = 6 } },
    @{ p = 11; q = 'Dolor musculoesquelético - Triaje reumatológico'; finalize = $false; a = @{ localizacion = 'Columna';  duracion = '6-12 semanas'; inflamacion = $false; antec_fam = $true;  impacto = 4 } },
    @{ p = 12; q = 'Esclerodermia juvenil - Evaluación';              finalize = $true;  a = @{ tipo = 'Localizada (morfea)'; rodnan = 4;  raynaud = $false; ulceras = $false; disfagia = $false } },
    @{ p = 13; q = 'Esclerodermia juvenil - Evaluación';              finalize = $true;  a = @{ tipo = 'Sistémica';           rodnan = 18; raynaud = $true;  ulceras = $true;  disfagia = $true } },
    @{ p = 14; q = 'Vasculitis (Kawasaki/PAN) - Seguimiento';         finalize = $true;  a = @{ entidad = 'Kawasaki'; fiebre_dias = 6; pcr = 82.5; compromiso = $true;  tratamiento = 'IGIV' } },
    @{ p = 15; q = 'Vasculitis (Kawasaki/PAN) - Seguimiento';         finalize = $false; a = @{ entidad = 'Púrpura de Schönlein-Henoch'; fiebre_dias = 0; pcr = 12.3; compromiso = $false; tratamiento = 'Ninguno' } },
    @{ p = 16; q = 'AIJ - Evaluación inicial';                        finalize = $true;  a = @{ edad = 7;  nro_artic = 4; rigidez_am = $true;  eva_dolor = 5; subtipo = 'Sistémica' } }
)

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

    # publicar (el validador es laxo: alcanza con resourceType)
    $publishBody = @{ fhirJson = ('{"resourceType":"Questionnaire","title":"' + $def.title + '"}') }
    $published = Invoke-Api 'POST' ("/fhir/r5/Questionnaire/$($created.id)/`$publish") $publishBody
    Write-Host ("    -> publicado, status={0}, versionId={1}" -f $published.status, $published.currentVersionId) -ForegroundColor DarkGreen
    $qByTitle[$def.title] = @{ id = $created.id; versionId = $published.currentVersionId }
}

# --- Respuestas / encuentros ---
$respCount = 0
foreach ($enc in $encounters) {
    $pat = $patients[$enc.p]
    $q = $qByTitle[$enc.q]
    if (-not $q) { Write-Host ("  ! Sin cuestionario para: {0}" -f $enc.q) -ForegroundColor Red; continue }

    # tipo por linkId, tomado de la definición del cuestionario
    $def = $questionnaires | Where-Object { $_.title -eq $enc.q } | Select-Object -First 1
    $typeByLink = @{}
    foreach ($qq in $def.questions) { $typeByLink[$qq.linkId] = $qq.type }

    $items = @()
    foreach ($k in $enc.a.Keys) {
        $items += (New-Item $k $typeByLink[$k] $enc.a[$k])
    }

    # FHIR con Patient "contained": el sistema (modo degradado) lee de acá
    # nombre / fecha de nacimiento / sexo cuando el hospital no está disponible.
    $gender = switch ($pat.sex) { 'F' { 'female' } 'M' { 'male' } default { 'unknown' } }
    $fhir = @{
        resourceType = 'QuestionnaireResponse'
        status       = ($(if ($enc.finalize) { 'completed' } else { 'in-progress' }))
        contained    = @(
            @{
                resourceType = 'Patient'
                id           = 'p'
                name         = @(@{ text = $pat.name })
                birthDate    = $pat.birth
                gender       = $gender
                identifier   = @(@{ system = 'urn:garrahan:hc'; value = $pat.hc }, @{ system = 'urn:garrahan:dni'; value = $pat.dni })
            }
        )
        subject      = @{ reference = '#p'; display = $pat.name }
        authored     = (Get-Date).ToString('o')
    } | ConvertTo-Json -Depth 8 -Compress

    $body = [ordered]@{
        questionnaireId        = $q.id
        questionnaireVersionId = ($(if ($q.versionId) { $q.versionId } else { [Guid]::Empty.ToString() }))
        patientHospitalId      = $pat.hc
        encounterId            = ("ENC-" + $pat.hc + "-" + (Get-Random -Minimum 1000 -Maximum 9999))
        status                 = ($(if ($enc.finalize) { 'InProgress' } else { 'InProgress' }))
        fhirJson               = $fhir
        items                  = $items
    }

    $resp = Invoke-Api 'POST' '/fhir/r5/QuestionnaireResponse' $body
    $respCount++
    $tag = ''
    if ($enc.finalize) {
        Invoke-Api 'POST' ("/fhir/r5/QuestionnaireResponse/$($resp.id)/`$finalize") @{} | Out-Null
        $tag = ' [finalizada]'
    }
    Write-Host ("  + Respuesta: {0} <- {1}{2}" -f $pat.name, $enc.q, $tag) -ForegroundColor Green
}

# --- Vista de reportería vw_FlatResponses (para /Reports y exportación) ---
# EF Code First crea las tablas pero no esta vista; la (re)creamos acá.
try {
    [xml]$cfgXml = Get-Content -Path $WebConfig -Raw
    $connStr = ($cfgXml.configuration.connectionStrings.add | Where-Object { $_.name -eq 'ChequeAR' }).connectionString
    $createView = @'
CREATE VIEW dbo.vw_FlatResponses AS
SELECT
    r.Id               AS ResponseId,
    r.QuestionnaireId,
    r.PatientHospitalId,
    r.AuthoredAt,
    i.LinkId,
    COALESCE(i.ValueDecimal, CAST(i.ValueInteger AS decimal(18,4))) AS ValueDecimal,
    i.ValueString,
    i.ValueDate,
    i.ValueBoolean,
    i.ValueCode,
    COALESCE(
        i.ValueString,
        i.ValueCode,
        CAST(i.ValueInteger AS nvarchar(50)),
        CONVERT(nvarchar(50), i.ValueDecimal),
        CONVERT(nvarchar(30), i.ValueDate, 23),
        CASE WHEN i.ValueBoolean IS NULL THEN NULL WHEN i.ValueBoolean = 1 THEN N'Sí' ELSE N'No' END
    ) AS ValueText
FROM dbo.QuestionnaireResponse r
INNER JOIN dbo.QuestionnaireResponseItems i ON i.ResponseId = r.Id;
'@
    $conn = New-Object System.Data.SqlClient.SqlConnection $connStr
    $conn.Open()
    $drop = $conn.CreateCommand(); $drop.CommandText = "IF OBJECT_ID('dbo.vw_FlatResponses','V') IS NOT NULL DROP VIEW dbo.vw_FlatResponses;"; [void]$drop.ExecuteNonQuery()
    $cr = $conn.CreateCommand(); $cr.CommandText = $createView; [void]$cr.ExecuteNonQuery()
    $conn.Close()
    Write-Host "  + Vista vw_FlatResponses creada." -ForegroundColor Green
}
catch {
    Write-Host ("  ! No pude crear vw_FlatResponses: {0}" -f $_.Exception.Message) -ForegroundColor Red
}

Write-Host ""
Write-Host ("Listo. Cuestionarios: {0} | Respuestas creadas: {1}" -f $qByTitle.Count, $respCount) -ForegroundColor Cyan
Write-Host "Probá en: $BaseUrl/Questionnaires   y   $BaseUrl/Encounters/Search"
