# ChequeAR

Sistema de cuestionarios clínicos para el Hospital Garrahan, con interoperabilidad HL7 FHIR R5.

## Estructura

Solución .NET en `src/`, organizada en capas con dependencias en una sola dirección
(`Domain ← Application ← Infrastructure ← Api`) e inversión hacia la infraestructura: los
repositorios implementan interfaces declaradas en `ChequeAR.Application/Interfaces`.

| Proyecto | Rol |
|---|---|
| `ChequeAR.Domain` | Entidades y enums. POCOs puros, sin paquetes |
| `ChequeAR.Application` | Servicios de negocio, contratos, mapeo FHIR, reportes |
| `ChequeAR.Infrastructure` | EF6 + Oracle, repositorios, clientes HTTP, autenticación, almacenamiento |
| `ChequeAR.Api` | Web API 2 + MVC 5 + Razor, islas Vue en `Scripts/islands/` |
| `ChequeAR.Tests` | xUnit + Moq. Referencia **sólo** Domain y Application — esa barrera es deliberada |

## Especificaciones

El proyecto usa [Spec Kit](https://github.com/github/spec-kit). Cada módulo tiene su carpeta en
`specs/` con `spec.md`, `plan.md` y `tasks.md`.

<!-- SPECKIT START -->
**Feature activa**: [`specs/009-conformidad-fhir-capas/plan.md`](specs/009-conformidad-fhir-capas/plan.md)
<!-- SPECKIT END -->

## Levantar la app

Usar la skill `/run-app`: arranca el TNS Listener de Oracle (servicio nativo de Windows, sin
Docker), compila con MSBuild de VS2022 y levanta IIS Express en `https://localhost:44301`.
