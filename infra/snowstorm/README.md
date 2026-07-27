# Snowstorm — servidor terminológico SNOMED CT propio

Reemplaza al Ontoserver público de CSIRO (temporal, en inglés, con rate-limit) por una
instancia propia de [Snowstorm](https://github.com/IHTSDO/snowstorm), el servidor de
referencia de SNOMED International. Expone la operación FHIR `ValueSet/$expand` que consume
el autocomplete del editor de cuestionarios (`TerminologyClient`).

Una vez cargado el release, el editor deja de depender de un server externo: sin bloqueo por
IP, sin 429, y con la edición en español cuando cargues la argentina/internacional-español.

## Requisitos

- Docker + Docker Compose.
- **RAM**: `ES_HEAP` puede bajarse a 2g sin drama (Elasticsearch nunca fue el cuello de botella
  en la práctica). `SNOWSTORM_HEAP` en cambio necesita **4g** para poder importar la edición
  argentina completa (~510k axiomas) — con 2g el import avanza igual pero explota más tarde,
  al construir el índice semántico (ver Troubleshooting). Total recomendado: ES 2g + Snowstorm
  4g ≈ 6-7g para los contenedores.
- ~10 GB de disco para el índice de Elasticsearch.
- El **release RF2** de SNOMED CT (ver más abajo). Necesitás la licencia aprobada para
  descargarlo — ya la tenés.

## 1. Levantar el stack

```bash
cd infra/snowstorm
cp .env.example .env         # ajustá ES_HEAP si tenés poca RAM
docker compose up -d
```

Esperá a que Elasticsearch quede `healthy` y Snowstorm termine de arrancar (mirá los logs con
`docker compose logs -f snowstorm`; está listo cuando loguea `Started SnowstormApplication`).

Verificá:

```bash
curl http://localhost:18081/fhir/metadata        # CapabilityStatement del server FHIR
```

En este punto el server está **vacío**: no hay ningún concepto cargado todavía.

## 2. Conseguir el release RF2

Con la licencia aprobada, descargá el release desde el MLDS (sección AR) o desde el Centro
Nacional de Terminología. El paquete argentino es una **"Edition" combinada**
(`SnomedCT_Argentina-EditionRelease_PRODUCTION_<fecha>.zip`): ya trae fusionadas la
International Edition + la extensión AR + el refset de idioma castellano (`450828004`) en un
solo `.zip` autocontenido — **no** hace falta importar la International por separado primero.
(Si en algún momento el Centro Nacional publicara una extensión-only en vez del edition release
combinado, ahí sí habría que importar primero la International y la extensión AR después, en
un branch hijo.)

> Snapshot vs Full: usá siempre la carpeta **Snapshot** del zip (el import la detecta sola). El
> `Full/` trae todo el histórico y es mucho más pesado — no lo necesitás para el autocomplete.

## 3. Importar el release

Snowstorm importa vía su API REST de administración. Con el `.zip` en tu disco:

```bash
# a) Crear el job de import sobre la rama MAIN
curl -i -X POST http://localhost:18081/imports \
  -H 'Content-Type: application/json' \
  -d '{"type":"SNAPSHOT","branchPath":"MAIN","createCodeSystemVersion":true}'
# → responde 201 con header  Location: http://localhost:18081/imports/<IMPORT_ID>

# b) ANTES de subir el archivo: subir max_terms_count en todos los índices (ver
#    Troubleshooting — la edición AR tiene más axiomas que el default de Snowstorm y el
#    import revienta si no se hace esto)
curl -s "http://localhost:9200/_cat/indices?h=index" | while read idx; do
  curl -X PUT "http://localhost:9200/$idx/_settings" \
    -H 'Content-Type: application/json' \
    -d '{"index.max_terms_count": 2000000}'
done

# c) Subir el archivo RF2 al job (arranca la carga; con la edición AR completa tardó
#    ~50 min en esta máquina con SNOWSTORM_HEAP=4g)
curl -X POST http://localhost:18081/imports/<IMPORT_ID>/archive \
  -F file=@/ruta/al/SnomedCT_Argentina-EditionRelease_PRODUCTION_xxx.zip

# d) Seguir el progreso (status pasa de RUNNING a COMPLETED)
curl http://localhost:18081/imports/<IMPORT_ID>
```

En Windows/PowerShell, para el paso (c) podés usar el mismo `curl.exe` (viene con Windows 10+),
o el explorador Swagger que Snowstorm publica en `http://localhost:18081/swagger-ui.html`
(endpoint **imports** → *Try it out*), que evita pelearse con el multipart.

Cuando el status sea `COMPLETED`, probá una expansión igual a la que hace el editor:

```bash
curl "http://localhost:18081/fhir/ValueSet/\$expand?url=http://snomed.info/sct?fhir_vs=ecl/%3C%3C404684003&filter=dolor&count=10&activeOnly=true&displayLanguage=es"
```

Si devuelve `expansion.contains` con conceptos en español, está andando.

### Troubleshooting: el import falla con "all shards failed"

Si el import muere con `search_phase_execution_exception: all shards failed` justo después de
loguear `Deserialised ~510,000 axioms...` / `Collect changed axiom is-a fragments`, **no es un
problema de RAM ni de Elasticsearch** (va a estar `green`, sin errores en sus logs). Es que
Snowstorm arma una query tipo `terms` con un ID por cada axioma para construir el índice
semántico, y sus índices vienen con `index.max_terms_count: 500000` de default — la edición AR
tiene ~510k axiomas, apenas por encima del límite, y Elasticsearch rechaza la query (el error
real queda swallowed por el cliente Elastic 8.x, por eso no se ve el motivo real en los logs de
Snowstorm). Fix: el paso (b) de arriba, subir `max_terms_count` en todos los índices **antes**
de subir el archivo. Como los índices se recrean cada vez que hacés `docker compose down -v`,
hay que repetir ese paso en cada import desde cero.

Si el import ya quedó a mitad de camino (branch `MAIN` con `locked: true` y no se puede
desbloquear ni con `POST /branches/MAIN/actions/unlock` ni con
`POST /admin/MAIN/actions/rollback-partial-commit` — este último también puede tirar timeout
con datasets grandes), no vale la pena pelearla: `docker compose down -v` + `up -d` de nuevo y
reimportar es más rápido que reparar el estado a mano.

## 4. Apuntar la app al Snowstorm propio

En `src/ChequeAR.Api/Web.config`, en `<appSettings>`:

```xml
<add key="Terminology:BaseUrl" value="http://localhost:18081/fhir" />
<add key="Terminology:Ecl" value="(&lt;&lt;363787002) OR (&lt;&lt;404684003) OR (&lt;&lt;386053000)" />
<add key="Terminology:DisplayLanguage" value="es" />
```

- `BaseUrl`: pasa de `https://r4.ontoserver.csiro.au/fhir` a `http://localhost:18081/fhir`.
- `Ecl`: **no cambia** — mismo dominio de sugerencias (Observable entity / Clinical finding /
  Evaluation procedure).
- `DisplayLanguage`: `es`, porque la edición AR (edition release combinado) ya trae el refset de
  idioma castellano. Ya está así en el `Web.config` del repo.

Reiniciá la app (IIS/IIS Express) para que tome la config nueva.

## Operación

```bash
docker compose stop         # apagar sin perder el índice
docker compose start        # volver a levantar (el release queda cargado en el volumen)
docker compose down         # apagar y borrar contenedores (el volumen se conserva)
docker compose down -v      # ⚠️ borra también el índice: habría que re-importar el release
```

El índice vive en el volumen `snowstorm-es-data`, así que sólo importás el release una vez.

## Notas

- **No expongas 9200 ni 18081 a internet.** Es un server interno; el `TerminologyClient`
  lo consume server-side justamente para no exponerlo al navegador.
- Si más adelante querés blindarlo, relanzá `snowstorm` con
  `--snowstorm.rest-api.readonly=true` (ya no permite imports, sólo consulta).
- La versión de Elasticsearch está pineada (8.11.1) porque Snowstorm es estricto con la
  compatibilidad. No la subas sin chequear la matriz de compatibilidad de Snowstorm.
- El heap y los `--add-opens` de Snowstorm se pasan por `JDK_JAVA_OPTIONS`, no por
  `JAVA_OPTS`: la imagen invoca `java` con un entrypoint fijo que ignora `JAVA_OPTS`.
  `JDK_JAVA_OPTIONS` es la variable estándar que el propio lanzador `java` lee solo,
  sin importar el entrypoint. Sin los `--add-opens` (`java.util`, `java.lang`,
  `java.time`), Snowstorm no arranca: Spring Data Elasticsearch necesita reflection
  sobre clases del JDK para el mapping context, y desde Java 16 el module system lo
  bloquea por default (`InaccessibleObjectException` en `elasticsearchMappingContext`).
