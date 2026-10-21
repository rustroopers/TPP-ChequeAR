# ChequeAR — Trabajo Profesional

Repositorio del sistema **ChequeAR**, desarrollado como Trabajo Profesional en la **Universidad de Buenos Aires – Facultad de Ingeniería**.

---

## Sobre el proyecto

ChequeAR es un sistema de gestión dinámica de fichas clínicas interoperables basadas en el estándar **HL7 FHIR R5**, pensado para las especialidades pediátricas del **Hospital Garrahan**. Permite diseñar cuestionarios clínicos versionados, codificar sus preguntas con SNOMED CT, completarlos por paciente y analizar los resultados con reportes, estadísticas y la evolución de cada parámetro en el tiempo.

El proyecto abarca el desarrollo completo de la plataforma: el modelo de datos sobre Oracle, la interfaz FHIR, la aplicación web y el despliegue en la nube, con foco en interoperabilidad, trazabilidad de los cambios y seguridad por rol y especialidad.

---

## Información académica

| | |
|---|---|
| **Universidad** | Universidad de Buenos Aires – Facultad de Ingeniería |
| **Carrera** | Ingeniería en Informática |
| **Materia** | Trabajo Profesional |
| **Año** | 2026 |

---

## Estructura del repositorio

Todo el sistema vive en este repositorio. Cada parte está en su carpeta:

| Carpeta | Descripción |
|---|---|
| [**src**](src/README.md) | Aplicación — .NET Framework 4.7.1, ASP.NET MVC 5 + Web API 2, Entity Framework 6 sobre Oracle, islas Vue |
| [**tests/integration**](tests/integration) | Tests de integración de repositorios y de las vistas del hospital, contra Oracle |
| [**infra**](infra) | Usuario de la base, mock de las vistas del hospital y servidores de terminología SNOMED CT (Snowstorm) |
| [**specs**](specs/README.md) | Especificaciones por módulo (Spec Kit): requerimientos, plan, tareas y decisiones de diseño |
| [**.github/workflows**](.github/workflows) | Integración continua y despliegue a Azure App Service |

La guía para desarrollar, compilar y correr los tests está en [`src/README.md`](src/README.md).

---

## Equipo 201 - 1er Cuatrimestre 2026

### Integrantes

| Nombre | Padrón | Rol | Contacto |
|---|---|---|---|
| Calderan, Facundo Andrés | 110873 | Desarrollador | fcalderan@fi.uba.ar |
| Chaia, Nicolás Khalil | 110768 | Desarrollador | nchaia@fi.uba.ar |
| Crudo, Gonzalo Nicolás | 110816 | Desarrollador | gncrudo@fi.uba.ar |
| Merlinsky Camins, Mariano Gabriel | 110446 | Desarrollador | mmerlinsky@fi.uba.ar |

### Director

| Nombre | Contacto |
|---|---|
| Lic. Carolo, Gustavo | gustavocarolo@gmail.com |
