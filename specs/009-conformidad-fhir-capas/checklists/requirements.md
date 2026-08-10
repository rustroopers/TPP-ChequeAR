# Specification Quality Checklist: Conformidad FHIR real y saneamiento de capas

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-08-02
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

### Decisiones de redacción durante la validación

**"HL7 FHIR R5" y "SNOMED CT" se mantienen deliberadamente en la spec.** No son detalles de
implementación sino el **contrato funcional** de la feature: el requisito es la conformidad con
un estándar externo, y nombrarlo es lo que lo hace verificable. Nombrarlo de forma vaga
("un estándar de interoperabilidad en salud") volvería FR-001, FR-010 y SC-001 no comprobables.
Precedente en el repositorio: `specs/003-interoperabilidad-fhir/spec.md` hace lo mismo.

**Lo que sí se mantuvo fuera de la spec** y corresponde a `plan.md`: la plataforma de ejecución
y su versión, el SDK elegido para manipular recursos del estándar, el motor de base de datos y
su driver, los nombres de proyectos, clases y archivos, y las rutas concretas de los endpoints.
La spec habla de "el motor de base de datos", "la capa de lógica de negocio" y "la interfaz de
interoperabilidad".

**Las historias 6 y 7 son estructurales por naturaleza.** Se redactaron en términos de
resultados observables y verificables (un cambio de motor no obliga a tocar la capa de negocio;
las reglas se pueden probar sin levantar la capa web; una prueba automática detecta la
regresión) en lugar de describir la reorganización del código, que pertenece al plan.

### Riesgos ya identificados, a resolver en `plan.md`

- La disponibilidad de un SDK de manipulación de recursos del estándar compatible con la
  plataforma de ejecución actual es la restricción técnica dominante y condiciona todo lo demás.
  Debe verificarse **antes** de comprometer el resto del trabajo.
- FR-005 (preservación sin pérdida en el ida y vuelta) es el punto de mayor riesgo de regresión
  silenciosa. Requiere cobertura de pruebas antes de tocar la capa de presentación.
- Existe una prueba de seguridad vigente sobre la construcción de consultas de reportes que
  debe seguir pasando; condiciona cómo se puede reorganizar esa parte (FR-018, SC-009).

### Supuesto de mayor impacto en alcance

**Las versiones ya publicadas no se migran ni se revalidan** (ver Assumptions y Edge Cases).
Si esta decisión se revirtiera, aparecería un trabajo de migración de datos que hoy está
explícitamente fuera de alcance. Vale confirmarlo antes de `/speckit-tasks`.
