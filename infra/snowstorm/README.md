# Snowstorm — servidor terminológico SNOMED CT propio

Reemplaza al Ontoserver público de CSIRO (temporal, en inglés, con rate-limit) por una
instancia propia de [Snowstorm](https://github.com/IHTSDO/snowstorm), el servidor de
referencia de SNOMED International. Expone la operación FHIR `ValueSet/$expand` que consume
el autocomplete del editor de cuestionarios (`TerminologyClient`).

Una vez cargado el release, el editor deja de depender de un server externo: sin bloqueo por
IP, sin 429, y con la edición en español cuando cargues la argentina/internacional-español.

## Requisitos

- Docker + Docker Compose.
- **~8 GB de RAM libres** para el import de la International Edition (Elasticsearch pide 4g de
  heap). En una máquina justa, bajá `ES_HEAP=2g` en el `.env` (import más lento, pero anda).
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

Con la licencia aprobada, descargá el **Snapshot** RF2 (un `.zip`) desde:

- **International Edition** (inglés) — [MLDS](https://mlds.ihtsdotools.org).
  Archivo tipo `SnomedCT_InternationalRF2_PRODUCTION_<fecha>.zip`.
- **Edición nacional argentina / español** — Centro Nacional de Referencia SNOMED CT de
  Argentina (la que corresponda a tu licencia). Es la que habilita `DisplayLanguage=es`.

Recomendación: cargá primero la **International** para validar todo el pipeline (es la que más
docs tiene), y después sumá/reemplazá por la **argentina** para tener los términos en español.

> Snapshot vs Full: usá siempre **Snapshot** (el estado actual). El "Full" trae todo el
> histórico y es mucho más pesado — no lo necesitás para el autocomplete.

## 3. Importar el release

Snowstorm importa vía su API REST de administración. Con el `.zip` en tu disco:

```bash
# a) Crear el job de import sobre la rama MAIN
curl -i -X POST http://localhost:18081/imports \
  -H 'Content-Type: application/json' \
  -d '{"type":"SNAPSHOT","branchPath":"MAIN","createCodeSystemVersion":true}'
# → responde 201 con header  Location: http://localhost:18081/imports/<IMPORT_ID>

# b) Subir el archivo RF2 al job (arranca la carga; tarda 15-45 min según la máquina)
curl -X POST http://localhost:18081/imports/<IMPORT_ID>/archive \
  -F file=@/ruta/al/SnomedCT_InternationalRF2_PRODUCTION_xxx.zip

# c) Seguir el progreso (status pasa de RUNNING a COMPLETED)
curl http://localhost:18081/imports/<IMPORT_ID>
```

En Windows/PowerShell, para el paso (b) podés usar el mismo `curl.exe` (viene con Windows 10+),
o el explorador Swagger que Snowstorm publica en `http://localhost:18081/swagger-ui.html`
(endpoint **imports** → *Try it out*), que evita pelearse con el multipart.

Cuando el status sea `COMPLETED`, probá una expansión igual a la que hace el editor:

```bash
curl "http://localhost:18081/fhir/ValueSet/\$expand?url=http://snomed.info/sct?fhir_vs=ecl/%3C%3C404684003&filter=arthralgia&count=10&activeOnly=true"
```

Si devuelve `expansion.contains` con conceptos, está andando.

## 4. Apuntar la app al Snowstorm propio

En `src/ChequeAR.Api/Web.config`, en `<appSettings>`:

```xml
<add key="Terminology:BaseUrl" value="http://localhost:18081/fhir" />
<add key="Terminology:Ecl" value="(&lt;&lt;363787002) OR (&lt;&lt;404684003) OR (&lt;&lt;386053000)" />
<add key="Terminology:DisplayLanguage" value="en" />
```

- `BaseUrl`: pasa de `https://r4.ontoserver.csiro.au/fhir` a `http://localhost:18081/fhir`.
- `Ecl`: **no cambia** — mismo dominio de sugerencias (Observable entity / Clinical finding /
  Evaluation procedure).
- `DisplayLanguage`: dejalo en `en` mientras tengas sólo la International. Cambialo a `es`
  **después** de cargar la edición argentina/español, si no las descripciones en español no
  existen en el índice y el `$expand` cae de vuelta al inglés.

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
