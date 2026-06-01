# Research: Motor de Renderizado Dinámico

## Decisiones

### D1. ¿Dónde vive el renderizado?

**Decisión**: Lógica de UI en una librería JS vanilla (TypeScript) drop-in en el template del hospital. Backend sólo expone API REST/FHIR.

**Justificación**: El hospital nos pasa el template y exige no acoplarse a un framework específico (Angular/React) que ellos no usen. Vanilla TS minimiza dependencias y peso del bundle.

**Alternativas descartadas**:
- *SPA propia (React)*: incompatible con la integración solicitada.
- *Renderizado server-side*: el template tiene su propio routing/maquetado.

### D2. Validación en cliente vs servidor

**Decisión**: Doble. Cliente valida en línea para UX; servidor revalida `enableWhen` y reglas en cada `POST`/`$finalize`.

**Justificación**: Los Médicos no pueden enviar respuestas inválidas; el cliente es untrusted. La duplicación se mitiga compartiendo el mismo parser de reglas vía un esquema FHIR estándar.

### D3. Autosave

**Decisión**: Intervalo 30 s + flush en cambio de pestaña (`visibilitychange`). Cola local en `IndexedDB` si la red cae; reintento con backoff exponencial.

**Justificación**: Reduce pérdida de datos sin saturar el servidor. Mitiga riesgo de "Resistencia al cambio" (anteproyecto, criticidad 15) — el sistema debe sentirse confiable y rápido.

### D4. Pre-carga (`$populate`)

**Decisión**: Implementar la operation FHIR estándar `$populate`. La PrefillService delega a módulo 003 según `extension` definida en cada `Question`:
- `fhir-observation` → consulta `Observation` por código LOINC/SNOMED en la API FHIR del hospital.
- `oracle-view` → consulta la vista Oracle por nombre de columna.

**Justificación**: Estándar y extensible; cada pregunta declara su fuente sin lógica hardcoded.

### D5. Adjuntos: almacenamiento

**Decisión**: File share UNC del hospital (`\\fileserver\chequear\attachments\{yyyy}\{mm}\{guid}`). Tabla `Attachment` con metadata.

**Justificación**: El hospital ya opera infra de file shares; evita introducir un blob store nuevo (S3, MinIO) que requiera aprobación de IT.

**Riesgo**: necesitamos un job de antivirus posterior al upload — coordinar con el departamento de sistemas.

### D6. Compatibilidad de navegadores

**Decisión**: Soportar Chrome y Edge en sus versiones LTS desplegadas en el hospital. Sin polyfills de IE.

## Referencias

- HL7 FHIR R5 — `QuestionnaireResponse`: https://hl7.org/fhir/R5/questionnaireresponse.html
- HL7 FHIR R5 — `$populate`: https://hl7.org/fhir/R5/questionnaire-operation-populate.html
- Ley 26.529 (Derechos del Paciente) — Argentina

## Riesgos abiertos

| Riesgo | Mitigación |
|---|---|
| Latencia de la vista Oracle del hospital | Caché en memoria del PrefillService con TTL corto (60 s) |
| El template UI evoluciona y rompe nuestra integración | Versionar el bundle JS y validar contra una versión congelada del template en CI |
