# Tasks: Seguridad y Control de Accesos

## Fase 0 – Acuerdos con el hospital

- [ ] T001 [S1] Documentar el contrato con la API del hospital (login, userinfo, formato de roles/especialidades).
- [ ] T002 [S1][P] Catálogo `Specialty` inicial alineado con las especialidades pediátricas del MVP (mínimo: reumato, hepato).

## Fase 1 – Auth core

- [ ] T010 [S2] `HospitalAuthClient` con Polly (retry + timeout).
- [ ] T011 [S2] `JwtIssuer` que emite access + refresh tokens.
- [ ] T012 [S2] `AuthController` con `/login`, `/refresh`, `/logout`, `/me`.
- [ ] T013 [S2] Configuración Owin Bearer Authentication.
- [ ] T014 [S2][P] Tests unitarios + integración con WireMock del hospital.

## Fase 2 – Autorización

- [ ] T020 [S2] Atributo `[Authorize(Roles=...)]` aplicado a todos los endpoints (cross-module).
- [ ] T021 [S2] Atributo `[ChequeARSpecialty]` que filtra por `specialties` claim.
- [ ] T022 [S2] Service-level checks defensivos (independientes del controller).
- [ ] T023 [S2][P] Auditoría en CI que escanea controllers sin `[Authorize]`.

## Fase 3 – Auditoría

- [ ] T030 [S3] Entidad `AuditEvent` + migración + partitioning mensual.
- [ ] T031 [S3] `AuditEventBuilder` para construir recursos FHIR R5.
- [ ] T032 [S3] `AuditDispatcher` con cola en memoria + flush periódico.
- [ ] T033 [S3] `AuditMiddleware` que captura requests/responses y dispara eventos.
- [ ] T034 [S3] Endpoint `GET /fhir/r5/AuditEvent` con permisos restringidos.
- [ ] T035 [S3][P] Eventos críticos (login, export, publish) persistidos sincrónicamente.

## Fase 4 – Tokens y sincronización

- [ ] T040 [S3] `RefreshTokenRepository` con hash y revocación.
- [ ] T041 [S3] Job nocturno: sync de usuarios deshabilitados + revocación de tokens.
- [ ] T042 [S3][P] `/auth/logout` que revoca el refresh token actual.

## Fase 5 – Hardening

- [ ] T050 [S5] Rate limiting en `/auth/login` (5/min por IP).
- [ ] T051 [S5][P] Escaneo de secretos en CI (`git-secrets` o GitHub native).
- [ ] T052 [S5] Configurar DPAPI / secrets en producción.
- [ ] T053 [S5][P] Pen-test interno sobre endpoints sensibles.
- [ ] T054 [S5] Validar coverage del módulo ≥ 80%.

## DoD

- 100% de endpoints con atributo de autorización (verificado en CI).
- Tests E2E que demuestran AuditEvent generado para cada operación sensible.
- Documentación del contrato hospital ↔ ChequeAR firmada por ambas partes.
