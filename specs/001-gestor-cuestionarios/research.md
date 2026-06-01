# Research: Gestor y Editor de Cuestionarios

## Decisiones técnicas

### D1. Representación: ¿modelo relacional puro o JSON-only?

**Decisión**: Modelo híbrido. Edición en estructura relacional; al publicar, snapshot completo serializado a JSON FHIR (`QuestionnaireVersion.FhirJson`).

**Justificación**: La edición frecuente con joins y validaciones se beneficia del modelo relacional. Las respuestas (`QuestionnaireResponse`) referencian una versión publicada, que debe ser inmutable y fiel al estándar FHIR — el snapshot JSON garantiza esto sin riesgo de drift.

**Alternativas descartadas**:
- *JSON puro en SQL Server*: simple pero lento para búsquedas (`title:contains`, filtro por especialidad).
- *Solo relacional con reconstrucción al vuelo*: riesgo de divergencia tras refactors del modelo.

### D2. Versionado

**Decisión**: Versionado por publicación. `Borrador` mutable; al publicar se crea `QuestionnaireVersion` inmutable. Editar un `Publicado` crea automáticamente un nuevo `Borrador` derivado y, al publicarlo, nueva versión.

**Justificación**: Compatible con `meta.versionId` de FHIR. Las respuestas siempre referencian su versión exacta — fundamental para trazabilidad clínica.

### D3. Lógica condicional

**Decisión**: Adoptar la sintaxis `enableWhen` y `enableBehavior` de FHIR R5 sin extenderla. Se persiste como JSON en `Question.EnableWhenJson`.

**Justificación**: Cualquier consumidor FHIR (incluyendo posibles integraciones futuras) la entiende sin traducción. Cubre los casos del MVP (mostrar/ocultar por valor de respuesta).

### D4. Validador FHIR

**Decisión**: HAPI-FHIR Validator .NET (Hl7.Fhir.R5) embebido + perfil propio `ChequeARQuestionnaire`.

**Justificación**: Mitiga el riesgo "Interoperabilidad fallida" del anteproyecto (criticidad 10). Se ejecuta en `$publish` y en CI.

### D5. Tipo `attachment`

**Decisión**: Soportar en MVP por requerimiento explícito de GP 201. Persistir archivos en un blob storage del hospital (path configurable) y referenciar por URL en `QuestionnaireResponse.item.answer.valueAttachment`.

**Riesgo**: límite de tamaño y antivirus. Validar extensiones (`.jpg`, `.png`, `.pdf`) y tope 10 MB en MVP.

### D6. Concurrencia en edición

**Decisión**: Optimistic locking con `If-Match` (ETag = `meta.versionId`). Si hay conflicto, 409.

## Referencias consultadas

- HL7 FHIR R5 — `Questionnaire`: https://hl7.org/fhir/R5/questionnaire.html
- HAPI FHIR .NET: https://github.com/FirelyTeam/firely-net-sdk
- REDCap (estado del arte) — Harris et al., 2009
- Patrón Package by Layer en .NET: Microsoft docs
- ISO 27799 — gestión de seguridad de información en salud (auditoría)

## Riesgos abiertos

| Riesgo | Mitigación |
|---|---|
| Curva FHIR R5 (anteproyecto, criticidad 16) | Spike de 1 sprint sobre `Questionnaire` y `QuestionnaireResponse` antes de codificar el módulo. |
| Cambios frecuentes en estructura | Versionado + lógica condicional minimizan necesidad de "tipos nuevos". |
| Antivirus bloquea adjuntos | Coordinar con sistemas del hospital scan post-upload. |
