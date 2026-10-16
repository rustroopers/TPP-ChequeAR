Documento de Requisitos del Producto

## ChequeAR

Integrantes

* 110768 Chaia, Nicolas Khalil nchaia@fi.uba.ar
* 110816 Crudo, Gonzalo Nicolas, gncrudo@fi.uba.ar
* 110446 Merlinsky Camins, Mariano Gabriel mmerlinsky@fi.uba.ar
* 110873 Calderan, Facundo Andres fcalderan@fi.uba.ar

# Preguntas:

## Autenticación y Roles de Usuario (RBAC)

## ¿Se utilizará el Active Directory del hospital como proveedor de autenticación para que los médicos inicien sesión en ChequeAR? ¿O se requiere integración con algún otro sistema de login existente?

Se usara API de logueo del Hospital

## ¿Qué roles (ej. Médico Jefe, Médico de Planta, Administrador) tienen configurados actualmente para que nosotros los consumamos?

Administrador, jefe, medico, observador (solo consulta)

## ¿Qué permisos específicos de acceso, edición o publicación debe tener cada rol dentro del sistema?

Administrador (todos) – Jefe Medicos (carga y consulta) -

## ¿Se requiere un registro (log) de auditoría detallado sobre quién accede o modifica cada ficha médica?

Si

## Infraestructura y Arquitectura

* ¿En qué versión específica de .NET debe estar desarrollado el backend para asegurar la compatibilidad y el mantenimiento?

Version 4.7.1

* Dado que el sistema operará en servidores *on-premise*, ¿existe alguna restricción o preferencia para el despliegue (ej. contenedores Docker, IIS)?

IIS versin 10 sobre windows server 2016

## Interfaz Gráfica (UI/UX)

## ¿Nuestra interfaz gráfica (Motor de Renderizado) funcionará como una aplicación independiente o debe estar embebida dentro del sistema? En caso afirmativo, ¿dónde y cómo se realizaría esa integración?

Les vamos a pasar nuestro template para que usen.

## Gestor de Cuestionarios y Flujo de Trabajo

* ¿El editor debería permitir adjuntar imágenes o documentos a las preguntas (ej. fotos de una lesión) en esta primera etapa del MVP?

Si

* ¿Habrá un estado de "Borrador" y otro de "Publicado" para controlar qué cuestionarios están listos para usarse?

Podria existir, borrador si no entiendo mal seria, que se esta generando (por el administrador) y Publicado que ya esta listo para comenzar a utilizarse.

## Interoperabilidad y Pre-carga

* ¿Cuál es el identificador principal del paciente que usaremos para enlazar los datos (DNI, ID interno u otro)?

ID interno

* Para evitar la doble carga, ¿como podríamos consumir los datos que ya se tienen (ej. peso, antecedentes)?

Algunos por FHIR otros por consulta a una vista de la base de datos del Hospital (oracle). Donde deberian residir los datos que persistan de esta aplicación.

## Módulo de Reportes y Especialidad Piloto

## ¿El formato principal de exportación de datos estructurados será Excel, o se requieren otros como CSV o PDF?

Excel y csv

## ¿Qué patologías debemos usar como modelo para definir las primeras variables del MVP?

Enfermedad reumática (a definir) y Trasplante hepático