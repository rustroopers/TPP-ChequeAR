# Tasks: Capa de Interoperabilidad y Almacenamiento

## Fase 0 – Setup

- [x] T001 [S1] Agregar dependencias Firely SDK, Oracle.ManagedDataAccess, Polly, YamlDotNet. *(Hl7.Fhir.R5, Oracle.ManagedDataAccess y YamlDotNet; sin Polly)*
- [ ] T002 [S1][P] Configurar HAPI Validator en CI (Docker). *(validador HL7 a mano)*
- [x] T003 [S1][P] Definir perfiles FHIR ChequeAR para `Questionnaire`, `QuestionnaireResponse`, `Observation`. *(Questionnaire y QuestionnaireResponse; Observation no se soporta)*

## Fase 1 – Servidor FHIR propio

- [x] T010 [S2] `MetadataController` que devuelve `CapabilityStatement`.
- [x] T011 [S2] Negotiator `application/fhir+json` (Web API formatter).
- [ ] T012 [S2] Tablas y entidades EF para almacenar `Observation` derivada y cache. *(fuera de alcance (2026-10-15): ChequeAR no produce Observation)*
- [ ] T013 [S2][P] Tests del CapabilityStatement contra el validador HL7. *(validado a mano)*

## Fase 2 – Cliente del hospital

- [x] T020 [S3] `HospitalFhirClient` con `HttpClientFactory` y auth configurable. *(queda en el código, sin uso: el hospital no expone API)*
- [ ] T021 [S3] Políticas Polly: timeout, retry, circuit-breaker. *(fuera de alcance (2026-10-15))*
- [ ] T022 [S3] Capa de caché `HospitalFhirCache` con SQL + job de limpieza. *(fuera de alcance (2026-10-15))*
- [ ] T023 [S3][P] Tests con WireMock.Net simulando 200/500/timeout. *(fuera de alcance (2026-10-15))*

## Fase 3 – Adaptador Oracle

- [x] T030 [S3] `OracleViewClient` con pool de conexiones managed.
- [ ] T031 [S3] `YamlMappingLoader` + `OracleToFhirMapper`. *(el mapeo vive en PatientService / MockHospitalFhirClient, sin YAML)*
- [ ] T032 [S4] Mapeos YAML iniciales: `patient.yaml`, `condition.yaml`, `observation.yaml`. *(ver T031)*
- [x] T033 [S4][P] Tests de integración contra Oracle XE en Docker. *(tests/integration/MockHospital.Tests contra Oracle local, sin Docker)*
- [ ] T034 [S4] Health-check periódico de columnas esperadas en cada vista.

## Fase 4 – Observation derivada y orquestación

- [ ] T040 [S4] Listener de evento "QR finalizada" (módulo 002) que genera `Observation` derivada cuando una pregunta tiene `linkage.outputCode`. *(fuera de alcance (2026-10-15))*
- [ ] T041 [S4] Endpoint `GET /Observation` que combina derivadas propias + pasa-through del hospital. *(fuera de alcance (2026-10-15))*
- [ ] T042 [S4][P] Tests E2E del flujo "completar ficha → buscar observación". *(fuera de alcance (2026-10-15))*

## Fase 5 – Transaction Bundle y hardening

- [ ] T050 [S5] Endpoint `POST /` para `Bundle` transaction. *(fuera de alcance (2026-10-15))*
- [x] T051 [S5] Validación FHIR en runtime con Firely Validator. *(parser estricto del SDK + ProfileRules: Firely Validator no corre en .NET Framework)*
- [ ] T052 [S5][P] Documentar `OperationDefinition` para `$publish`, `$populate`, `$finalize`, `$archive`, `$clone`.
- [ ] T053 [S5][P] Pruebas de carga (k6) con 200 req/s sobre lecturas FHIR.

## DoD

- 100% recursos del repo aprobados por validador en CI.
- Circuit-breaker probado con tests que simulan caídas del hospital.
- Documentación de mapeos YAML mantenida junto a su código.
