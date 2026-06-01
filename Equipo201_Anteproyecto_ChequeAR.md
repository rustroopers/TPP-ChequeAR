Trabajo Profesional de Ingeniería en Informática

Sistema de gestión dinámica de fichas clínicas interoperables basadas en el estándar HL7

FHIR para especialidades pediátricas.

ChequeAR

Integrantes

●  110768 Chaia, Nicolas Khalil nchaia@fi.uba.ar

●  110816 Crudo, Gonzalo Nicolas, gncrudo@fi.uba.ar

●  110446 Merlinsky Camins, Mariano Gabriel mmerlinsky@fi.uba.ar

●  110873 Calderan, Facundo Andres fcalderan@fi.uba.ar

Director

Lic. Carolo Gustavo  gustavocarolo@gmail.com

Acta acuerdo y aval del director

El texto de la misma debe respetar la siguiente estructura (omitir las cursivas):

En Buenos Aires a 20 de abril del año 2026 se reúnen Lic. Gustavo Carolo con el/los

estudiante/s de la carrera de Ingeniería en Informática: Facundo Andrés Calderan

(dni:45685351), Nicolas Khalil Chaia (dni: 45616837), Gonzalo Nicolas Crudo (dni: 45819701) y

Mariano Gabriel Merlinsky Camins (dni:44486126).  El objetivo de la reunión es acordar el tema

de Trabajo Profesional de Ingeniería en Informática.

Luego de haber conversado sobre las áreas de interés de el/los estudiante/s, se acuerda

establecer como Tema de Trabajo Profesional en Ingeniería Informática: Sistema de gestión

dinámica de fichas clínicas interoperables basadas en el estándar HL7 FHIR para

especialidades pediátricas.

Lic. Gustavo Carolo acepta dirigir el Trabajo Profesional Sistema de gestión dinámica de fichas

clínicas interoperables basadas en el estándar HL7 FHIR para especialidades pediátricas, que

van a desarrollar el/los estudiante/s de la carrera de Ingeniería en Informática: Facundo Andrés

Calderan (dni: 45685351), Nicolas Khalil Chaia (dni: 45616837), Gonzalo Nicolas Crudo (dni:

45819701) y Mariano Gabriel Merlinsky Camins (dni:44486126).

Tanto el Director como los estudiantes declaran conocer la Resolución de Consejo Directivo

1124 de 2025, que define la normativa para los Trabajos Integradores Finales para carreras de

grado a partir de 2026, y las obligaciones de ambas partes establecidas en la misma. En

particular, para el cumplimiento del necesario contacto periódico, se comprometen a reunirse

con una periodicidad igual o menor a las dos semanas, sea de manera presencial o remota, y

acuerdan respetar la forma de trabajo definida en esta propuesta.

Firma y aclaración del director: <firma y aclaración>

Firmas y aclaraciones de los estudiantes:

Gonzalo Nicolas Crudo           Mariano Gabriel Merlinsky Camins

Nicolas Khalil Chaia

Facundo Andrés Calderan

Resumen

El proyecto ChequeAR surge de la necesidad de estandarizar y agilizar la carga de datos

clínicos en entornos hospitalarios de alta complejidad. Actualmente, la transición de texto libre a

datos estructurados representa un desafío significativo en la práctica médica diaria. Este

trabajo propone el desarrollo de una aplicación web que permita a los profesionales de la salud

crear, gestionar y completar cuestionarios dinámicos asociados a patologías específicas, sin

reemplazar la evolución narrativa, sino estructurando la información vital que ésta contiene.

Mediante el uso del estándar internacional HL7 FHIR, la solución garantiza la interoperabilidad

de la información entre especialistas y sistemas. El sistema permitirá definir fichas compuestas

por grupos de parámetros configurables, facilitando no solo la carga asistencial sino también la

posterior exportación de informes condicionales para análisis estadístico y clínico. La iniciativa

se enmarca en el contexto del Hospital Garrahan y toma como alcance inicial sus

especialidades pediátricas.

Palabras clave

●  Hospital Garrahan

●  Historia clínica electrónica

●  HL7 FHIR

●  Salud Digital

●  Ficha Médica

Abstract

The ChequeAR project arises from the need to standardize and streamline clinical data entry in

high-complexity hospital environments. The current transition from free-text notes to structured

data represents a significant challenge in everyday medical practice. This work proposes the

development of a web application that allows healthcare professionals to create, manage, and

fill in dynamic questionnaires associated with specific pathologies, without replacing narrative

evolution notes but rather structuring the critical information they contain.

By leveraging the international HL7 FHIR standard, the solution guarantees the interoperability

of clinical information across specialists and systems. The platform will enable the definition of

clinical forms composed of configurable parameter groups, easing not only point-of-care data

entry but also the subsequent export of conditional reports for statistical and clinical analysis.

The initiative is framed within the operational context of Hospital Garrahan, with its pediatric

specialties as the initial scope.

Keywords

●  Hospital Garrahan

●  Electronic Health Records

●  HL7 FHIR

●  Digital Health

●  Medical Record

Introducción

En la actualidad, la recopilación de datos clínicos de los pacientes se realiza mayoritariamente

mediante campos de texto libre. Si bien esta práctica permite al profesional detallar la

evolución, genera un ecosistema de información no estructurada y ambigua. La falta de un

consenso formal sobre qué variables específicas deben registrarse según la patología resulta

en fichas médicas inconsistentes y dispersas en múltiples sistemas, lo cual dificulta la

interconsulta y el análisis automatizado de la información.

Para dar respuesta a esta problemática en el contexto del Hospital Garrahan, surge la iniciativa

de desarrollar ChequeAR, un sistema informático intrahospitalario enfocado en la gestión de

formularios clínicos dinámicos. Su objetivo principal no es la eliminación de las evoluciones en

texto libre, sino la reducción y estructuración de la información vital que estos contienen. El

sistema permitirá que los referentes de cada especialidad consensúen y configuren sus propios

cuestionarios paramétricos, adaptándolos a sus necesidades operativas. Al centralizar y

estructurar estas métricas bajo normativas internacionales (como el estándar HL7 FHIR), se

agiliza no solo el análisis estadístico interno, sino que se garantiza una interoperabilidad segura

para compartir datos clínicos con otros profesionales e instituciones.

Para presentar formalmente este Trabajo Profesional, el presente documento se estructura de

la siguiente manera: en primer lugar, se expone el estado del arte y se detalla a fondo la

problemática detectada. A continuación, se define la solución tecnológica propuesta,

acompañada de una evaluación preliminar de sus impactos económicos, sociales y

ambientales. Finalmente, se describe la metodología de trabajo ágil adoptada, incluyendo el

plan de actividades, las métricas de calidad y el análisis de riesgos que guiarán el ciclo de

desarrollo del proyecto.

Estado del Arte

La evolución de los sistemas de información hospitalaria ha evidenciado que la simple

digitalización de documentos no es suficiente para garantizar una atención eficiente.

Actualmente, el estado del arte en informática médica se centra en la transición del registro

desestructurado (texto libre) hacia modelos de datos altamente estructurados e interoperables.

Este cambio de paradigma busca resolver la fragmentación de la información y habilitar un

seguimiento clínico preciso y parametrizado.

Sistemas de Captura Electrónica de Datos (EDC)

En el ámbito de la salud y la investigación clínica, los sistemas EDC lideran las soluciones para

la recolección de datos. La herramienta de referencia a nivel global es REDCap (Research

Electronic Data Capture), una plataforma web que permite a los usuarios construir y gestionar

bases de datos y encuestas online. REDCap destaca por su capacidad de generar formularios

dinámicos con lógicas de ramificación y validación de datos sin necesidad de programar.

Sin embargo, herramientas robustas como REDCap están fuertemente orientadas a la

investigación de ensayos clínicos, presentando alta complejidad a la hora de aprender su

funcionamiento y surgiendo flujos de trabajo que pueden resultar poco ágiles para la carga

diaria de evoluciones médicas y el seguimiento continuo en consultorios. Además, su

integración nativa con sistemas hospitalarios preexistentes o flujos de trabajo específicos de

una institución suele ser compleja.

Modelado Dinámico y Estructuración Clínica

Desde la perspectiva de la ingeniería de software, el manejo de información clínica presenta el

desafío de la variabilidad: cada especialidad médica o patología requiere el seguimiento de

conjuntos de datos distintos. El estado del arte en arquitecturas de software para salud propone

el uso de bases de datos no relacionales que permiten a los sistemas adaptarse

dinámicamente. Estas arquitecturas facilitan que usuarios con permisos administrativos puedan

crear, modificar y seccionar formularios paramétricos en tiempo real.

El contexto institucional y la oportunidad de innovación

En el Hospital Garrahan, una práctica común para el seguimiento de pacientes implica que los

profesionales dependan de procesadores de texto, planillas de cálculo de uso personal o

campos de texto libre dentro de sistemas genéricos. Esta metodología genera información

ambigua, donde los datos críticos de los pacientes no siguen una estructura uniforme,

dificultando la interconsulta entre especialistas y limitando la trazabilidad a lo largo del tiempo.

En base a este relevamiento, si bien existen plataformas globales de creación de formularios, la

mayoría carece de la flexibilidad necesaria para adaptarse ágilmente al flujo de trabajo clínico

diario de distintas especialidades pediátricas sin convertirse en una carga administrativa. Es

entonces que este proyecto se posiciona como una solución innovadora y necesaria para la

institución: el desarrollo de un sistema centralizado de formularios dinámicos y modulares,

diseñado específicamente para estructurar el relevamiento de parámetros de distintas

especialidades médicas. Esta plataforma sentará las bases de datos estructuradas necesarias

para que, en un futuro, la institución pueda implementar análisis estadísticos avanzados.

Problema detectado y/o faltante

El principal desafío que enfrentan los profesionales médicos en el contexto del Hospital

Garrahan reside en la falta de un sistema centralizado y estructurado para el registro y

seguimiento de las fichas clínicas. Actualmente, la información suele almacenarse de forma

aislada en computadoras personales o documentos dispersos, dependiendo casi

exclusivamente de campos de texto libre. Esta práctica genera un ecosistema de datos

fragmentado y poco uniforme que impacta negativamente en la interoperabilidad, la calidad del

seguimiento y la investigación médica. Se detallan las principales problemáticas identificadas:

●  Ambigüedad y falta de estandarización: El uso de evoluciones médicas basadas

únicamente en texto libre impide que la información siga una estructura uniforme. Esto

genera ambigüedad, ya que dificulta que distintos profesionales interpreten los datos de la

misma forma, obstaculizando las interconsultas y el trabajo interdisciplinario.

●  Inconsistencia en el relevamiento clínico: Al carecer de formularios estructurados con

parámetros validados, no se garantiza la captura de todos los indicadores necesarios para

una patología específica. La variabilidad en el criterio de cada profesional al priorizar datos

genera vacíos de información críticos e inconsistencias en el seguimiento.

●  Dificultad en la sistematización y Análisis de Datos: La información almacenada en texto

libre no es estructurada ni fácilmente procesable. La búsqueda de datos históricos precisos

se convierte en un proceso manual que demanda gran cantidad de tiempo, limitando

severamente la capacidad de generar estadísticas, reportes de investigación o implementar

futuras herramientas de Inteligencia Artificial para la extracción automática de datos.

La ausencia de una plataforma integral y estructurada genera una brecha crítica entre la

captura del dato médico y su aprovechamiento clínico, comprometiendo la agilidad de la

atención, el análisis retrospectivo y la escalabilidad del sistema a largo plazo.

Solución propuesta

Se propone el desarrollo de ChequeAR, un sistema informático integral diseñado para

complementar y estructurar la carga de datos clínicos que actualmente se realiza mediante

texto libre. El sistema se concibe bajo una arquitectura modular, compuesto por los siguientes

módulos principales:

●  Gestor y Editor de Cuestionarios: Una herramienta administrativa que permitirá a los

médicos jefes o coordinadores de cada especialidad definir, tipificar y agrupar variables

clínicas. Este módulo permitirá configurar rangos de validación, alertas y lógicas de

dependencia entre preguntas sin necesidad de modificar el código fuente.

●  Motor de Renderizado Dinámico: Interfaz de usuario (Frontend) encargada de

interpretar los metadatos de los cuestionarios creados y transformarlos en una ficha de

carga ágil, intuitiva y amigable para el profesional de la salud durante su práctica diaria.

●  Capa de Interoperabilidad y Almacenamiento: El núcleo de datos del sistema estará

basado en el estándar internacional HL7 FHIR. Se utilizarán recursos específicos para

modelar los formularios y las respuestas. Asimismo, esta capa facilitará la integración

con los sistemas actuales del hospital, permitiendo pre-cargar variables existentes del

paciente para evitar la doble carga de información.

●  Módulo de Análisis y Exportación: Una funcionalidad orientada a la investigación que

permitirá consultar la base de datos no relacional y generar informes parametrizados.

Los profesionales podrán filtrar cohortes de pacientes basándose en condiciones

lógicas sobre las fichas cargadas y exportar los resultados para su análisis estadístico.

●  Seguridad y Control de Accesos: Implementación de un sistema de roles y permisos

para garantizar la privacidad de los datos y restringir funcionalidades críticas,

asegurando que solo el personal autorizado pueda crear o alterar la estructura de las

fichas clínicas.

Stack Tecnológico

Para el desarrollo del sistema se adoptará una arquitectura cliente-servidor, con el backend

construido sobre el framework .NET en una versión a convenir con el equipo técnico del

hospital. Esta elección no es arbitraria: el Hospital Garrahan dispone de un departamento de

sistemas con experiencia consolidada en dicha tecnología, lo que garantizará el correcto

mantenimiento y soporte una vez finalizado el proyecto y transferido el producto. A su vez, el

sistema correrá sobre la infraestructura de servidores que el hospital ya posee.

La interfaz del sistema se expondrá como una API REST construida sobre el estándar HL7

FHIR. La combinación de ambos no es casual: FHIR fue diseñado desde sus bases sobre el

modelo REST, por lo que cada recurso clínico del estándar —pacientes, observaciones, turnos,

entre otros— tiene una representación natural como endpoint HTTP. Las respuestas se

entregarán en formato application/fhir+json, lo que asegura compatibilidad con

cualquier sistema del ecosistema hospitalario que adhiera al mismo estándar, simplificando

eventuales integraciones futuras sin necesidad de desarrollar capas de traducción adicionales.

En cuanto a la organización interna del código, se adoptará el patrón Package by Layer. Bajo

este esquema, el proyecto se estructura en capas horizontales con responsabilidades bien

delimitadas: la capa de presentación concentra los controllers encargados de recibir y

responder las solicitudes HTTP; la capa de lógica de negocio agrupa los services donde reside

el comportamiento central de la aplicación; la capa de acceso a datos contiene los repositorios

que abstraen la interacción con la base de datos; y la capa de dominio define las entidades que

modelan los conceptos del problema. La comunicación entre capas sigue una dirección única,

lo que aísla los cambios y evita que modificaciones en la infraestructura o en la persistencia

afecten la lógica de negocio. Esta estructura, además de ser ampliamente conocida en el

ecosistema .NET, facilita el testing independiente de cada componente.

Evaluación preliminar de impacto económico, social y ambiental

La implementación de un sistema centralizado para el registro estructurado y seguimiento de

variables clínicas genera impactos positivos y desafíos significativos en distintos ámbitos del

ecosistema de salud.

Impacto económico

Desde una perspectiva económica, la transición de registros basados en texto libre y

documentos fragmentados hacia una base de datos estructurada optimiza drásticamente los

tiempos administrativos y médicos. La capacidad de generar cuestionarios dinámicos y contar

con un repositorio único de variables estandarizadas reduce las horas dedicadas a la búsqueda

manual de historiales y la interpretación de datos ambiguos.

Impacto social

La estandarización de la información clínica mejora la interoperabilidad entre los distintos

especialistas del hospital, reduciendo errores de interpretación, facilitando las interconsultas y

elevando la calidad general de la atención médica.

Impacto ambiental

A nivel de infraestructura tecnológica, el sistema será desplegado y alojado en los servidores

locales que el Hospital Garrahan ya posee. Esta decisión arquitectónica optimiza la utilización

de los recursos computacionales existentes y evita la adquisición de nuevo hardware.

Metodología

El proyecto seguirá una metodología de trabajo ágil, utilizando la metodología SCRUM para

organizar el proyecto entre todos los integrantes, debido a su naturaleza iterativa e incremental.

La idea es dividir el desarrollo en sprints de 2 semanas, y programar releases parciales cada 4

sprints. En cada sprint se seleccionarán un conjunto de historias de usuario a priorizar.

En términos de control de flujo, utilizaremos un tablero Kanban en la plataforma Notion, con

columnas que reflejan los posibles estadíos en los cuales se encuentra cada tarea.

En términos de control de versiones, se utilizará GitHub, donde se tendrá un repositorio con el

proyecto. Se tendrán tres ramas en el proyecto (development, testing y main), indicando el

estado del código que se encuentra en cada ambiente. La ejecución de tests y el despliegue

del código estarán automatizados mediante pipelines de CI/CD implementados en GitHub

Actions.

Definition of Done

Para que una historia de usuario se considere completa debe:

●

●

●

●

Cumplir con todos sus criterios de aceptación.

Contar con pruebas unitarias y/o de integración.

Verificar el cumplimiento de las pruebas.

Que un integrante del equipo de desarrollo distinto al que realizó la tarea apruebe el

Pull Request a la rama pertinente.

La ejecución de tests y despliegue del código será automatizado mediante pipelines de CI/CD

que se ejecutarán en GitHub Actions.

Roles

Los roles que cumplirá cada integrante ligado al proyecto serán:

●

●

Desarrolladores: Facundo Calderan, Gonzalo Crudo, Nicolas Chaia, Mariano Merlinsky

Product Owner: (Stakeholders Hospital Garrahan) Gustavo Carolo, Fernando Sanz,

Alberto Hernandez

Experimentación, validación y control de calidad

La validación del sistema es una etapa crucial y se abordará con un enfoque iterativo e

incremental, integrado al flujo de desarrollo. La estrategia se estructura en tres niveles

complementarios: validación técnica, validación funcional con usuarios y una prueba piloto en

entorno clínico acotado.

1.  Validación técnica

La confirmación técnica se integra de forma conjunta y continua al desarrollo mediante

una cascada de pruebas automatizadas ejecutadas en el pipeline de GitHub Actions. En

el primer nivel, se realizan pruebas unitarias sobre la lógica interna de dominio

(condiciones, validadores, serializadores FHIR) apuntando a una cobertura mínima del

75%. En el siguiente nivel, tendremos pruebas de integración donde se validarán los

endpoints de la API REST contra bases de datos de testing aisladas en contenedores

Docker. El último nivel, tendremos pruebas end-to-end con Playwright que cubrirán los

flujos críticos como la creación de un cuestionario, carga de una respuesta y la

exportación de los datos.

En lo que respecta al estándar FHIR, todo recurso generado por el sistema será

validado contra el validador oficial de HL7 para garantizar la correcta integración.

Como complemento a las pruebas automatizadas, cada Historia de Usuario contará con

criterios de aceptación predefinidos que deberán cumplirse estrictamente para

considerar una funcionalidad como finalizada.

2.  Validación funcional con usuarios

Es importante garantizar la usabilidad del sistema por lo que en cada Stakeholder

Review se presentará el incremento desarrollado generando minutas que alimentarán el

Product Backlog para mejorar el producto.

Adicionalmente, se organizarán sesiones de usabilidad guiada con los profesionales

piloto. En ellas podremos obtener el tiempo de completitud de tareas, tasa de errores y

satisfacción subjetiva medida mediante cuestionarios CSAT (Customer Satisfaction

Score).

3.  Prueba piloto

En las últimas iteraciones del proyecto se realizará una prueba piloto acotada de 3 a 4

semanas a definir junto con los stakeholders. Durante el piloto, un grupo reducido de

profesionales utilizará el sistema en paralelo a sus herramientas habituales. Se

recolectarán indicadores de uso (fichas cargadas, usuarios activos), de performance

(tiempos de carga y respuesta), de calidad del dato (completitud, consistencia) y

feedback cualitativo mediante entrevistas breves.

La prueba se ejecutará exclusivamente con datos sintéticos o anonimizados, en

cumplimiento de la Ley 25.326 de Protección de Datos Personales y los protocolos del

Hospital Garrahan.

Plan de actividad

Para el desarrollo de ChequeAR, se adoptará una metodología basada en el marco de trabajo

ágil Scrum, permitiendo un enfoque iterativo e incremental que asegure la adaptabilidad frente

a los requerimientos del Hospital Garrahan. El proyecto se estructurará en ciclos de trabajo

denominados Sprints, con una duración fija de 2 (dos) semanas cada uno, previendo la

finalización y entrega del Trabajo Profesional en noviembre de 2026.

Ceremonias y Sincronización:

●  Daily Scrum: Reuniones diarias de 15 minutos entre los cuatro integrantes del equipo

para sincronizar tareas, dar visibilidad al estado de los ítems del Sprint y realizar una

detección temprana de bloqueos.

●  Weekly Team Sync & Retrospective: Sesión semanal interna dedicada a la alineación

técnica y a la mejora continua del proceso de trabajo, analizando qué aspectos del ciclo

previo pueden optimizarse.

●  Stakeholder Review: Reunión semanal con los referentes del Hospital Garrahan para

demostrar el progreso funcional, validar los entregables intermedios y ajustar las

prioridades del negocio. Generando una minuta sobre la reunión.

Gestión del Alcance y Tiempos:

El control del proyecto se basará en los siguientes instrumentos:

●  Gestión del Alcance: Se mantendrá un Product Backlog como artefacto vivo que

centralice todos los requisitos, funcionalidades y mejoras técnicas. La priorización de

este listado se revisará dinámicamente junto al cliente para asegurar que el valor

entregado sea máximo.

●  Planificación de Sprints: Al inicio de cada iteración, se seleccionará un subconjunto de

Historias de Usuario del Backlog. Para las estimaciones de esfuerzo, se utilizará la

técnica de Planning Poker, buscando un equilibrio entre la capacidad del equipo y la

dificultad técnica de las tareas.

Riesgos Identificados

Riesgo

Probabilidad (1-5)

Impacto (1-5)  Criticidad (I*P)  Estrategia de Mitigación

4

5

3

5

16

15

12

10

Investigación previa de la
especificación y uso de librerías
probadas.

Involucrar a usuarios finales (médicos
del Garrahan) en el diseño de la UI
para asegurar que sea más rápida
que escribir.

Diseñar el sistema de forma modular
y dinámica (metodología ágil) para
que el cambio de cuestionarios no
requiera cambios en el código.

Validar cada recurso generado con
herramientas de validación oficial de
HL7.

Curva de aprendizaje
HL7 FHIR: Complejidad
en el mapeo de
recursos.

Resistencia al cambio:
Que los médicos
prefieran seguir usando
texto libre.

Cambios en los
requerimientos:
Modificaciones en la
estructura de las fichas.

Interoperabilidad fallida:
Dificultad para exportar
los datos a otros
sistemas.

4

3

4

2

Referencias

●  HL7 International. FHIR Release 5 Specification. https://hl7.org/fhir/

●  HL7 International. Resource Questionnaire & QuestionnaireResponse.

●  Harris,  P.A.  et  al.  Research  Electronic  Data  Capture  (REDCap)  —  A  metadata-driven

methodology  and  workflow  process  for  providing  translational  research  informatics

support. Journal of Biomedical Informatics, 2009.

●  Schwaber, K.; Sutherland, J. The Scrum Guide, 2020.

●  Ley 25.326 — Protección de Datos Personales. República Argentina.

●  Ley 26.529 — Derechos del Paciente. República Argentina.

●  Resolución CD 1124/2025 — FIUBA, Trabajos Integradores Finales.

●  Cohn, M. (2006). Agile Estimating and Planning, Boston, EE. UU.: Pearson Education.

●  Project Management Institute (PMI). (2017). A Guide to the Project Management Body of

Knowledge  (PMBOK  Guide)  (6th  ed.).  Pennsylvania,  EE.  UU.:  Project  Management

Institute

●  Rubin,  K.  S.  (2013).  Essential  Scrum:  A  Practical  Guide  to  the  Most  Popular  Agile

Process. Boston, EE. UU: Addison-Wesley Professional

●  Documentación oficial de HAPI FHIR: https://hapifhir.io/

