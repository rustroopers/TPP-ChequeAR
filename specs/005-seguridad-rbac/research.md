# Research: Seguridad y Control de Accesos

## Decisiones

### D1. Auth provider

**Decisión**: Delegada 100% a la API del Hospital Garrahan; ChequeAR sólo proxy + emisión de JWT propio.

**Justificación**: Explícito en GP 201 ("Se usará API de logueo del Hospital"). Evita gestionar contraseñas — reduce superficie de ataque y cumplimiento Ley 25.326.

**Alternativas descartadas**:
- *Active Directory directo*: el hospital ya envuelve AD detrás de su API.
- *IdentityServer propio*: replica funcionalidad existente sin valor.

### D2. JWT vs cookies de sesión

**Decisión**: JWT firmado HS256, TTL 60 min, refresh token 8 h.

**Justificación**: La librería UI del hospital se integra mejor con bearer tokens. JWT permite validación stateless en la API. TTL corto + revocación de refresh mitiga el principal contra de JWT (no revocable).

### D3. Algoritmo de firma

**Decisión**: HS256 con secret rotable.

**Justificación**: Sin necesidad de exponer claves públicas; menor complejidad que RS256 para emisor único. Considerar migración a RS256 si en el futuro el hospital quiere validar tokens emitidos por ChequeAR.

### D4. Modelo de roles vs especialidades

**Decisión**: Roles globales + claim `specialties[]` filtrando datos en repositorios.

**Justificación**: Confirmado en GP 201 (Administrador/Jefe/Médico/Observador). Las especialidades segmentan datos sin multiplicar roles. La filtración se hace en la capa de servicio, no sólo UI.

### D5. Auditoría: dónde y cómo

**Decisión**: `AuditEvent` FHIR R5 almacenado en tabla append-only particionada por mes. Escritura asincrónica vía `AuditDispatcher` (cola en memoria con flush periódico + persistencia inmediata para eventos críticos como login/export).

**Justificación**: Cumple FR-006 sin agregar latencia perceptible. Particionamiento permite retención de años sin degradar queries por usuario/fecha.

**Alternativas descartadas**:
- *Logs Serilog → archivos*: difícil de consultar y no estandarizado.
- *Tabla simple sin partitioning*: degrada con > 1M filas.

### D6. Retención de auditoría

**Decisión**: Mínimo 6 años (alineado con regulaciones argentinas para historia clínica). Particiones mensuales; backups offline.

### D7. Secrets management

**Decisión**: Variables de entorno protegidas; en producción, DPAPI (Windows Data Protection API) para encriptar `appsettings.production.json`.

**Justificación**: Sin secrets vault en infra del hospital; DPAPI es nativo de Windows y aceptable para la criticidad del MVP.

### D8. Filtros declarativos vs imperativos

**Decisión**: Atributos C# (`[Authorize]`, `[ChequeARSpecialty]`) en controllers + checks defensivos en servicios.

**Justificación**: Atributos son auditables en CI (escaneo de código). Checks en servicios protegen contra controllers que olviden el atributo.

## Referencias

- Ley 25.326 — Protección de Datos Personales
- Ley 26.529 — Derechos del Paciente
- Resolución MS 1480/2011 — Investigación en Salud Humana
- HL7 FHIR R5 — `AuditEvent`: https://hl7.org/fhir/R5/auditevent.html
- OWASP ASVS — controles para AuthN/AuthZ
- IETF RFC 7519 — JWT

## Riesgos abiertos

| Riesgo | Mitigación |
|---|---|
| API del hospital cambia formato de respuesta | `HospitalAuthClient` versionado + contract tests |
| Robo de refresh token | TokenHash en DB, posibilidad de revocación masiva |
| Dispatcher AuditEvent pierde eventos en crash | Eventos críticos (login, export, $publish) se persisten sincrónicamente |
| Falta de coordinación con sistemas del hospital sobre roles | Definir contrato escrito antes de S2; mocks para desarrollo |
