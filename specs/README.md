# Specs

| Spec | Estado |
|---|---|
| [001 Gestor de cuestionarios](001-gestor-cuestionarios/spec.md) | Implementada |
| [002 Motor de renderizado](002-motor-renderizado/spec.md) | Implementada en parte: pre-carga (`$populate`) y cola offline fuera de alcance |
| [003 Interoperabilidad FHIR](003-interoperabilidad-fhir/spec.md) | Implementada en parte: no hay API del hospital; la integración es por vistas `VW_*` |
| [004 Análisis y exportación](004-analisis-exportacion/spec.md) | Implementada; exportación asincrónica y hoja "Acerca de" fuera de alcance |
| [005 Seguridad y RBAC](005-seguridad-rbac/spec.md) | Implementada; login local |
| [006 Frontend y UX](006-frontend-ux/spec.md) | Implementada en parte |
| [007 i18n](007-i18n/spec.md) | Descartada: sólo es-AR |
| [008 Mock del hospital](008-mock-hospital-db/spec.md) | Implementada, sobre Oracle local |
| [009 Conformidad FHIR y capas](009-conformidad-fhir-capas/spec.md) | Implementada |
| [010 Evolución de parámetros por paciente](010-evolucion-parametros-paciente/spec.md) | Implementada |
| 011 Especialidades | Sin spec: entró en el PR #26 (`SpecialtyCatalogService`, `/api/specialties`) |
| 012 Variables clínicas | Sin spec: entró en el PR #26 |
| [013 Completado asincrónico](013-completado-asincronico-cuestionarios/README.md) | Implementada; documentada en un README, sin spec formal |
| [014 Versionado de variables](014-versionado-variables/spec.md) | Reemplazada por la 015: las variables ya no tienen versiones |
| [015 Reemplazo de variables](015-variables-reemplazo/spec.md) | Implementada |

Los contratos de 001–005 quedaron viejos; el vigente para la interfaz FHIR es
[`009-conformidad-fhir-capas/contracts/fhir-api.md`](009-conformidad-fhir-capas/contracts/fhir-api.md).
Las reglas generales del proyecto están en [`.specify/memory/constitution.md`](../.specify/memory/constitution.md).

Fuera de alcance por decisión del 2026-10-15: pre-carga `$populate`, resiliencia y autenticación
contra el hospital, exportación asincrónica, cola offline del autosave, hoja "Acerca de" del
XLSX, subsecciones, CIE-10 y validación con el `Validator` de Firely.
