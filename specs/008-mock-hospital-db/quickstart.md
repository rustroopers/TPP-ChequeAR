# Quickstart: Mock Hospital DB

> Receta de un comando para levantar la réplica del lado hospital en una máquina de desarrollo.

## Prerrequisitos

- Docker Engine ≥ 20.10 + Docker Compose v2 (`docker compose version`).
- ≥ 4 GB de RAM disponibles para Docker (Apple Silicon: subir a 6 GB por la emulación).
- Puerto local `1521` libre.

## Levantar el mock (primera vez)

```bash
cd infra/mock-hospital
cp .env.example .env       # opcional; defaults sirven para dev local
docker compose up -d --wait
```

- `--wait` bloquea hasta que el servicio esté `healthy`. Como el healthcheck exige `COUNT(*) FROM HOSP_PACIENTE >= 50`, esto sólo retorna cuando el seed terminó.
- Tiempo esperado: < 3 min en amd64, < 8 min en Apple Silicon (todo dentro del SC-001).

Salida esperada al final:

```
[+] Running 2/2
 ✔ Volume mock-hospital-data    Created
 ✔ Container mock-hospital      Healthy
```

## Verificar el dataset

```bash
docker compose exec mock-hospital sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 <<'SQL'
SELECT COUNT(*) AS pacientes FROM HOSPITAL_MOCK.VW_PACIENTE;
SELECT COUNT(*) AS episodios FROM HOSPITAL_MOCK.VW_EPISODIO;
SELECT COUNT(*) AS observaciones FROM HOSPITAL_MOCK.VW_OBSERVACION_LAB;
SQL
```

Debe imprimir conteos ≥ 50, ≥ 150, ≥ 500 respectivamente (SC-006).

Verificar que las tablas internas **no** son alcanzables desde `CHEQUEAR_RO`:

```bash
docker compose exec mock-hospital sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 <<'SQL'
SELECT COUNT(*) FROM HOSPITAL_MOCK.HOSP_PACIENTE;
SQL
# Esperado: ORA-00942: table or view does not exist
```

## Conectar ChequeAR al mock

En `src/ChequeAR.Api/Web.config` (o `App.config` del Application), apuntar la connection string `OracleHospitalView` a:

```
Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))
            (CONNECT_DATA=(SERVICE_NAME=XEPDB1)));User Id=CHEQUEAR_RO;Password=devdev;
```

Y registrar la allowlist en el contenedor DI:

```csharp
var allowlist = AllowlistLoader.Load("Oracle/allowlist.yaml");
container.Register<IOracleViewClient>(
    new OracleViewClient(connectionString, allowedViews: allowlist));
```

(El `AllowlistLoader` es trabajo de la fase de tasks; ver `tasks.md`.)

## Reset completo

```bash
cd infra/mock-hospital
docker compose down -v   # borra el volumen → próximo `up` re-sembra todo
docker compose up -d --wait
```

Como el seed es determinístico (`DBMS_RANDOM.SEED(20260625)`), el dataset resultante es idéntico al anterior (SC-002).

## Comandos útiles

| Acción | Comando |
|---|---|
| Ver logs del seed | `docker compose logs -f mock-hospital` |
| Conectarse como owner | `docker compose exec mock-hospital sqlplus HOSPITAL_MOCK/$HOSPITAL_MOCK_PASSWORD@//localhost:1521/XEPDB1` |
| Conectarse como app | `docker compose exec mock-hospital sqlplus CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1` |
| Parar sin perder datos | `docker compose stop` |
| Ver salud | `docker compose ps` |

## Troubleshooting

- **`unhealthy`** al cabo de 10 min: revisar `docker compose logs mock-hospital`. Si aparece `ORA-20001/20002/20003`, el seed falló alguna invariante (no llegó al conteo mínimo). Hacer `down -v` y volver a levantar.
- **Apple Silicon muy lento**: confirmar `platform: linux/amd64` en el compose. Subir RAM asignada a Docker Desktop.
- **Puerto 1521 ocupado**: editar `ports:` en el `docker-compose.yml` y reflejarlo en la connection string de ChequeAR.

## Tests de integración

```bash
cd tests/integration/MockHospital.Tests
dotnet test
```

Los tests asumen que el contenedor está corriendo y `healthy`. Si no, fallan rápido con un mensaje claro.
