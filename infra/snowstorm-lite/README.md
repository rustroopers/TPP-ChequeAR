# Snowstorm Lite en Oracle Cloud (Always Free)

Servidor terminológico SNOMED CT para la instancia de ChequeAR en Azure. Usa
[Snowstorm Lite](https://github.com/IHTSDO/snowstorm-lite) (Lucene, sin Elasticsearch), que una
vez indexado corre con ~500 MB y entra holgado en una VM Ampere A1 gratuita. Para desarrollo
local sigue estando `infra/snowstorm/` (Snowstorm completo en Docker).

## Cómo queda protegido

```
App Service "chequear" (West US 3)
   │ HTTPS + header X-ChequeAR-Key, cert validado por huella SHA-256
   ▼
OCI Security List  →  443 sólo desde las 44 IPs de salida del App Service, 22 sólo desde la IP admin
   ▼
VM snowstorm-lite (Ubuntu 24.04 arm64, iptables del host con 22 y 443)
   └ nginx (red del host): allowlist de IPs + key + sólo GET /fhir/metadata y /fhir/ValueSet/$expand
        └ snowstorm-lite en 127.0.0.1:8080 (dashboard, /fhir-admin y MCP nunca salen)
```

- **Por qué IP + key**: las IPs de salida de un App Service F1 son compartidas con otros
  clientes de Azure del mismo scale unit. La IP sola no identifica a ChequeAR.
- **Por qué cert autofirmado**: no hay dominio. ChequeAR valida el certificado por huella
  (`Terminology:CertSha256`, ver `TerminologyHttp`), que es más estricto que confiar en una CA.
  El cert dura 10 años; si se regenera, hay que actualizar la huella en Azure.

## Recursos en OCI (región sa-santiago-1)

| Recurso | Nombre |
|---|---|
| VCN 10.0.0.0/16 | `vcn-snowstorm` |
| Internet Gateway | `igw-snowstorm` |
| Subnet pública 10.0.0.0/24 | `subnet-snowstorm` |
| Security List | `sl-snowstorm` (la default de la VCN quedó vacía) |
| VM | `snowstorm-lite`, VM.Standard.A1.Flex |

Clave SSH: `~/.ssh/chequear_snowstorm` en la máquina del admin.

## Puesta en marcha

1. Copiar esta carpeta a la VM y correr el setup (genera `.env` y el cert, levanta el stack):
   ```bash
   scp -i ~/.ssh/chequear_snowstorm -r infra/snowstorm-lite ubuntu@<IP>:~/
   ssh -i ~/.ssh/chequear_snowstorm ubuntu@<IP> './snowstorm-lite/setup.sh'
   ```
2. Subir el release y cargarlo **desde la VM, contra localhost** (tarda unos minutos):
   ```bash
   scp -i ~/.ssh/chequear_snowstorm D:/SnomedCT_Argentina-EditionRelease_PRODUCTION_20260820T120000Z.zip ubuntu@<IP>:~/
   ssh -i ~/.ssh/chequear_snowstorm ubuntu@<IP>
   source snowstorm-lite/.env
   curl -u admin:$ADMIN_PASSWORD --form file=@SnomedCT_Argentina-EditionRelease_PRODUCTION_20260820T120000Z.zip \
     --form version-uri="http://snomed.info/sct/11000221109/version/20260820" \
     http://127.0.0.1:8080/fhir-admin/load-package
   rm SnomedCT_Argentina-*.zip      # la licencia no permite dejarlo dando vueltas
   ```
3. En Azure → App Service `chequear` → Variables de entorno:
   - `Terminology:BaseUrl` = `https://<IP>/fhir`
   - `Terminology:CertSha256` = la huella que imprime `setup.sh`
   - `Terminology:ApiKey` = `TERM_KEY` del `.env` de la VM

## Verificación

| Desde | Request | Esperado |
|---|---|---|
| Cualquier PC | `curl -k https://<IP>/fhir/metadata` | timeout (Security List) |
| Kudu de Azure, sin header | `curl -k https://<IP>/fhir/metadata` | 403 |
| Kudu, con header | `curl -k -H "X-ChequeAR-Key: …" https://<IP>/fhir-admin/` | 404 |
| Kudu, con header | `$expand` de "artralgia" | 200 en español |

## Operación

- **Recuperación de instancias ociosas**: en cuentas Free, Oracle reclama VMs Always Free con
  CPU/red/memoria < 20% durante 7 días. Snowstorm Lite casi sin uso cae ahí. Lo evita pasar la
  cuenta a Pay As You Go (sigue gratis dentro de los límites Always Free).
- **IPs de Azure cambiaron** (cambio de plan/región): regenerar `nginx/azure-allow.conf` y las
  reglas 443 de `sl-snowstorm`.
- **Rotar la key**: cambiar `TERM_KEY` en `.env`, `docker compose up -d`, y el App Setting.
- **IP admin cambió** (IP residencial dinámica): editar la regla 22 de `sl-snowstorm`.
- **Nueva edición SNOMED**: repetir el paso 2. No hay backups: el índice se reconstruye del zip.
