# -*- coding: utf-8 -*-
"""Genera el libro Excel con el material de gestión del informe final de ChequeAR.

Hojas:
  1. Backlog    — Product Backlog completo (épicas + User Stories con SP)
  2. Cronograma — Sprints Scrum + hitos (con el Gantt embebido)
  3. Riesgos    — Matriz de riesgos (P × I) del anteproyecto + fase de integración

Uso:  python docs/informe/generar_graficos.py   # primero, para el Gantt
      python docs/informe/build_xlsx.py
Salida: docs/informe/ChequeAR_backlog_cronograma_riesgos.xlsx
"""
import os
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter
from openpyxl.drawing.image import Image as XLImage

BASE = os.path.dirname(__file__)
OUT = os.path.join(BASE, "ChequeAR_backlog_cronograma_riesgos.xlsx")

# ---- Paleta ----
AZUL = "1D4ED8"; AZUL_CLARO = "DBEAFE"; GRIS_HDR = "1E293B"
VERDE = "DCFCE7"; NARANJA = "FFEDD5"; GRIS_CLARO = "F1F5F9"
BLANCO = "FFFFFF"

thin = Side(style="thin", color="CBD5E1")
BORDER = Border(left=thin, right=thin, top=thin, bottom=thin)
HDR_FONT = Font(bold=True, color=BLANCO, size=11)
HDR_FILL = PatternFill("solid", fgColor=GRIS_HDR)
TITLE_FONT = Font(bold=True, size=15, color=GRIS_HDR)
WRAP = Alignment(wrap_text=True, vertical="top")
CENTER = Alignment(horizontal="center", vertical="center")
CENTER_WRAP = Alignment(horizontal="center", vertical="top", wrap_text=True)


def style_header(ws, row, ncols):
    for c in range(1, ncols + 1):
        cell = ws.cell(row=row, column=c)
        cell.font = HDR_FONT
        cell.fill = HDR_FILL
        cell.alignment = Alignment(horizontal="center", vertical="center", wrap_text=True)
        cell.border = BORDER
    ws.row_dimensions[row].height = 30


# ==========================================================================
#  Datos: ÉPICAS
# ==========================================================================
# code, nombre, modulo (del anteproyecto), estado
EPICAS = [
    ("E1", "Gestor y Editor de Cuestionarios", "Módulo 1 — Gestor y Editor", "Entregado"),
    ("E2", "Motor de Renderizado Dinámico y Fichas", "Módulo 2 — Motor de Renderizado", "Entregado"),
    ("E3", "Interoperabilidad y Almacenamiento (FHIR R5)", "Módulo 3 — Interoperabilidad", "Entregado"),
    ("E4", "Análisis y Exportación", "Módulo 4 — Análisis y Exportación", "Entregado"),
    ("E5", "Seguridad y Control de Accesos", "Módulo 5 — Seguridad", "Entregado"),
    ("E6", "Frontend / UX", "Transversal", "Entregado"),
    ("E7", "Terminología clínica SNOMED CT", "Módulo 3 — Interoperabilidad", "Entregado"),
    ("E8", "Mock de base del Hospital (provisorio)", "Soporte al desarrollo", "Entregado"),
    ("E9", "Completado asincrónico de fichas", "Módulo 2 — Motor de Renderizado", "Entregado"),
    ("E10", "Evolución longitudinal de parámetros", "Módulo 4 — Análisis y Exportación", "Entregado"),
    ("E11", "Calidad, CI/CD y hardening", "Transversal", "Entregado"),
    ("E12", "Integración real con el Hospital Garrahan", "Módulo 3 — Interoperabilidad", "Pendiente"),
]
EP_NOMBRE = {c: n for c, n, _, _ in EPICAS}

# ==========================================================================
#  Datos: USER STORIES
#  (id, epica, rol, quiero, para, prioridad, sp, sprint, estado)
# ==========================================================================
US = [
    # E1
    ("US1.1", "E1", "Editor clínico", "crear un cuestionario paramétrico con secciones y preguntas", "estructurar la recolección de datos por especialidad", "P1", 8, "S5", "Hecho"),
    ("US1.2", "E1", "Editor clínico", "definir tipos de pregunta (texto, numérico, opción, fecha, adjunto)", "cubrir todos los casos clínicos", "P1", 8, "S5", "Hecho"),
    ("US1.3", "E1", "Editor clínico", "configurar validaciones de respuesta (largo máximo, patrón, rangos)", "garantizar la calidad del dato capturado", "P2", 3, "S8", "Hecho"),
    ("US1.4", "E1", "Editor clínico", "definir lógica condicional (enableWhen) entre preguntas", "mostrar preguntas según respuestas previas", "P1", 8, "S7", "Hecho"),
    ("US1.5", "E1", "Editor clínico", "versionar y publicar cuestionarios con concurrencia optimista", "evitar pisar cambios de otra sesión", "P1", 8, "S7", "Hecho"),
    ("US1.6", "E1", "Editor clínico", "clonar un cuestionario y aplicar reglas de borrado", "reutilizar sin empezar de cero", "P2", 5, "S7", "Hecho"),
    ("US1.7", "E1", "Editor clínico", "reordenar preguntas y secciones por drag & drop", "organizar la ficha con agilidad", "P2", 5, "S8", "Hecho"),
    ("US1.8", "E1", "Editor clínico", "importar preguntas de otro cuestionario de la misma especialidad", "acelerar el armado de nuevas fichas", "P2", 5, "S10", "Hecho"),
    ("US1.9", "E1", "Editor clínico", "resolver conflictos de edición concurrente (merge)", "trabajar en equipo sin pérdida de datos", "P2", 5, "S9", "Hecho"),
    ("US1.10", "E1", "Editor clínico", "asociar especialidades y registrar notas de versión", "trazar el cambio entre versiones publicadas", "P3", 3, "S7", "Hecho"),
    # E2
    ("US2.1", "E2", "Profesional de salud", "cargar una ficha renderizada dinámicamente desde el cuestionario", "completar datos del paciente sin doble carga", "P1", 8, "S7", "Hecho"),
    ("US2.2", "E2", "Profesional de salud", "que la ficha quede anclada a la versión con la que se empezó", "mantener consistencia longitudinal", "P1", 5, "S7", "Hecho"),
    ("US2.3", "E2", "Profesional de salud", "adjuntar archivos y visualizar los ya subidos", "documentar estudios asociados", "P2", 5, "S7", "Hecho"),
    ("US2.4", "E2", "Profesional de salud", "ver la ficha en modo lectura con su versión de origen", "auditar sin alterar el dato", "P2", 3, "S7", "Hecho"),
    ("US2.5", "E2", "Profesional de salud", "que se validen las preguntas obligatorias al finalizar", "evitar fichas incompletas", "P1", 5, "S7", "Hecho"),
    ("US2.6", "E2", "Profesional de salud", "ver placeholders y textos guía en los campos", "entender qué se espera en cada pregunta", "P3", 2, "S8", "Hecho"),
    # E3
    ("US3.1", "E3", "Sistema", "serializar cuestionarios/respuestas como recursos FHIR R5 (Firely SDK)", "interoperar bajo el estándar internacional", "P1", 8, "S9", "Hecho"),
    ("US3.2", "E3", "Sistema", "exponer una API de lectura que devuelve recursos FHIR R5 reales", "integrar con sistemas externos", "P1", 8, "S9", "Hecho"),
    ("US3.3", "E3", "Sistema", "escribir vía FHIR con superficie única y sin pérdida de datos (round-trip)", "garantizar integridad en la interoperabilidad", "P1", 8, "S9", "Hecho"),
    ("US3.4", "E3", "Sistema", "representar pacientes y encuentros como recursos del estándar", "modelar el dominio clínico en FHIR", "P1", 5, "S9", "Hecho"),
    ("US3.5", "E3", "Sistema", "validar contra perfiles publicados y el validador oficial de HL7", "asegurar conformidad real", "P1", 8, "S9", "Hecho"),
    ("US3.6", "E3", "Sistema", "devolver todos los errores de la interfaz como OperationOutcome", "dar respuestas estándar y trazables", "P2", 5, "S9", "Hecho"),
    ("US3.7", "E3", "Equipo de desarrollo", "sanear las capas: reglas de negocio fuera de controllers e independientes del motor de BD", "mantenibilidad y testabilidad", "P1", 13, "S9", "Hecho"),
    # E4
    ("US4.1", "E4", "Analista clínico", "filtrar una cohorte por condiciones legibles sobre las fichas", "segmentar la población de estudio", "P1", 8, "S7", "Hecho"),
    ("US4.2", "E4", "Analista clínico", "guardar y reutilizar consultas de reportes", "repetir análisis sin rehacer filtros", "P2", 5, "S8", "Hecho"),
    ("US4.3", "E4", "Analista clínico", "ver estadísticas reales y previsualización sobre la cohorte filtrada", "interpretar resultados en pantalla", "P1", 8, "S8", "Hecho"),
    ("US4.4", "E4", "Analista clínico", "exportar estadísticas a Excel con metadata y gráficos nativos (torta/barra)", "compartir informes fuera del sistema", "P1", 8, "S10", "Hecho"),
    ("US4.5", "E4", "Analista clínico", "alternar entre barras y torta para preguntas categóricas", "elegir la mejor visualización", "P3", 3, "S10", "Hecho"),
    ("US4.6", "E4", "Analista clínico", "ocultar preguntas sin datos y precargar el nombre al exportar", "resumir y agilizar la exportación", "P3", 3, "S10", "Hecho"),
    ("US4.7", "E4", "Analista clínico", "usar rango de fechas con calendario y filtros con debounce", "filtrar cómodamente sin recargas", "P2", 5, "S8", "Hecho"),
    ("US4.8", "E4", "Analista clínico", "exportar a Excel la cohorte filtrada (versión base)", "contar con una salida temprana de datos", "P1", 5, "S5", "Hecho"),
    # E5
    ("US5.1", "E5", "Administrador", "controlar el acceso por roles (RBAC)", "restringir funcionalidades críticas según el perfil", "P1", 8, "S9", "Hecho"),
    ("US5.2", "E5", "Auditor", "registrar eventos de guardar/editar/eliminar (reportes, fichas, cuestionarios)", "trazabilidad de las acciones", "P1", 8, "S9", "Hecho"),
    ("US5.3", "E5", "Auditor", "ver el diff de versiones y acciones legibles con zona horaria ARG", "investigar cambios con claridad", "P2", 5, "S9", "Hecho"),
    ("US5.4", "E5", "Auditor", "ver el historial 'Detalle' por ficha (quién/cuándo completó cada cambio)", "trazabilidad fina del completado", "P2", 5, "S9", "Hecho"),
    ("US5.5", "E5", "Auditor", "filtrar la auditoría", "encontrar eventos rápidamente", "P3", 3, "S9", "Hecho"),
    # E6
    ("US6.1", "E6", "Usuario", "un shell de aplicación con sidebar, rutas y acciones rápidas", "navegar el sistema con fluidez", "P1", 8, "S3", "Hecho"),
    ("US6.2", "E6", "Usuario", "islas Vue montadas sobre MVC/Razor", "tener interactividad sin una SPA completa", "P1", 8, "S3", "Hecho"),
    ("US6.3", "E6", "Usuario", "pantallas de carga (spinner, top progress bar) y placeholders", "recibir feedback visual", "P2", 5, "S3", "Hecho"),
    ("US6.4", "E6", "Usuario", "formato de fecha dd/MM/yyyy, paginación correcta y UI en trato de usted", "coherencia con el uso local", "P2", 5, "S7", "Hecho"),
    ("US6.5", "E6", "Usuario", "una UI limpia en auditoría/reportes/pacientes (badges, chips, combobox)", "leer la información sin ruido visual", "P3", 8, "S8", "Hecho"),
    # E7
    ("US7.1", "E7", "Equipo de desarrollo", "levantar una infra de Snowstorm propia con edición SNOMED CT argentina", "contar con terminología soberana y sin dependencia externa", "P1", 8, "S8", "Hecho"),
    ("US7.2", "E7", "Editor clínico", "asociar un concepto SNOMED CT a una pregunta", "codificar clínicamente el dato", "P1", 8, "S8", "Hecho"),
    ("US7.3", "E7", "Editor clínico", "asociar un concepto SNOMED CT a la ficha completa", "codificar el instrumento de recolección", "P2", 5, "S9", "Hecho"),
    # E8
    ("US8.1", "E8", "Equipo de desarrollo", "un mock Oracle del lado Hospital (US1–US4)", "desarrollar sin depender de la base real", "P1", 8, "S4", "Hecho"),
    ("US8.2", "E8", "Equipo de desarrollo", "un seed de pacientes y fichas reales (Reumatología)", "probar con datos realistas", "P2", 5, "S6", "Hecho"),
    # E9
    ("US9.1", "E9", "Profesional de salud", "continuar o finalizar una ficha parcialmente completada", "trabajar en varias sesiones", "P1", 8, "S9", "Hecho"),
    ("US9.2", "E9", "Profesional de salud", "guardar el avance con solo las obligatorias completas", "no perder trabajo entre sesiones", "P1", 5, "S9", "Hecho"),
    ("US9.3", "E9", "Sistema", "auditar por sesión y controlar el cambio de versión durante el completado", "trazar el completado asincrónico", "P2", 5, "S9", "Hecho"),
    ("US9.4", "E9", "Profesional de salud", "ver estados reales en 'Fichas recientes' y bloqueo de Finalizar si falta completar", "tener claridad del flujo", "P2", 3, "S9", "Hecho"),
    # E10
    ("US10.1", "E10", "Profesional de salud", "un gráfico de evolución longitudinal de un parámetro por paciente", "seguir la evolución clínica en el tiempo", "P1", 8, "S9", "Hecho"),
    ("US10.2", "E10", "Profesional de salud", "filtros de línea de tiempo con guarda de rangos invertidos", "acotar el período de análisis", "P2", 5, "S9", "Hecho"),
    # E11
    ("US11.1", "E11", "Equipo de desarrollo", "un pipeline de build + tests unitarios con -warnaserror (GitHub Actions)", "integración continua sin regresiones", "P1", 5, "S10", "Hecho"),
    ("US11.2", "E11", "Equipo de desarrollo", "logging centralizado con Serilog", "observabilidad de la aplicación", "P2", 5, "S10", "Hecho"),
    ("US11.3", "E11", "Equipo de desarrollo", "sacar los secretos del Web.config versionado", "reducir la superficie de exposición", "P1", 3, "S10", "Hecho"),
    ("US11.4", "E11", "Equipo de desarrollo", "acotar el bypass de login a modo debug y no exponer detalle de excepciones", "endurecer la seguridad", "P1", 5, "S10", "Hecho"),
    ("US11.5", "E11", "Equipo de desarrollo", "cobertura de tests > 75 % en la capa Application", "cumplir el estándar de calidad de la tesis", "P1", 5, "S9", "Hecho"),
    # E12 — futuro
    ("US12.1", "E12", "Equipo de desarrollo", "conectar contra la base Oracle real del Hospital (reemplazar el mock)", "operar con datos productivos", "P1", 13, "S11", "Pendiente"),
    ("US12.2", "E12", "Equipo de desarrollo", "mapear el modelo de pacientes/encuentros del Hospital a los recursos FHIR", "interoperar de verdad end-to-end", "P1", 13, "S11", "Pendiente"),
    ("US12.3", "E12", "Equipo de desarrollo", "acordar y validar el contrato FHIR con el sistema del Hospital (endpoints, perfiles)", "garantizar conformidad de extremo a extremo", "P1", 8, "S11", "Pendiente"),
    ("US12.4", "E12", "Seguridad", "integrar la autenticación/autorización institucional (SSO / roles reales)", "cumplir la política del Hospital", "P1", 8, "S12", "Pendiente"),
    ("US12.5", "E12", "Equipo de desarrollo", "pruebas de integración end-to-end (Playwright) en preproducción del Hospital", "validar antes de producción", "P1", 8, "S13", "Pendiente"),
    ("US12.6", "E12", "Equipo de desarrollo", "ejecutar una prueba piloto acotada (3–4 semanas) con profesionales", "medir uso, performance y satisfacción (CSAT)", "P2", 5, "S13", "Pendiente"),
    ("US12.7", "E12", "Equipo de desarrollo", "un plan de migración/carga inicial y despliegue en la infraestructura del Hospital", "poner el sistema en marcha", "P2", 5, "S14", "Pendiente"),
]

# ==========================================================================
#  Datos: CRONOGRAMA
#  (sprint, periodo, fase, foco, epicas, sp, estado)
# ==========================================================================
CRONO = [
    ("—", "Marzo 2026", "Anteproyecto", "Elaboración y aprobación del informe pre-proyecto; acta de acuerdo y aval del director (20-abr firmada).", "—", "—", "Hecho"),
    ("S1", "01–15 abr 2026", "Setup / diseño", "Kickoff, relevamiento con stakeholders del Garrahan, investigación HL7 FHIR R5 y SNOMED CT, elección de stack (.NET, Package by Layer).", "E3", 8, "Hecho"),
    ("S2", "16–30 abr 2026", "Setup / diseño", "Definición de arquitectura en capas, specs 001–005, setup del repositorio (ramas dev/testing/main) y esqueleto de la solución.", "E1–E5", 13, "Hecho"),
    ("S3", "01–15 may 2026", "Desarrollo", "Modelo de dominio (POCOs), shell de frontend + islas Vue, pantallas P1.", "E6", 21, "Hecho"),
    ("S4", "16–31 may 2026", "Desarrollo", "Mock de la base del Hospital (Oracle EF), wiring MVC↔Application, controllers API y DI propia.", "E8, E6", 24, "Hecho"),
    ("S5", "01–15 jun 2026", "Desarrollo", "Gestor de cuestionarios (alta, tipos de pregunta), primera exportación a Excel y seed de Reumatología.", "E1, E4", 34, "Hecho"),
    ("S6", "16–30 jun 2026", "Desarrollo", "Refactor a Oracle EF real, mock provisorio del Hospital (US1–US4), cuestionarios agregados.", "E8, E1", 30, "Hecho"),
    ("S7", "01–15 jul 2026", "Desarrollo", "La app consume solo bases reales (sin mocks); editor completo (enableWhen, adjuntos, versiones, clonado); motor de fichas; reportes ejecutables.", "E1, E2, E4", 50, "Hecho"),
    ("S8", "16–31 jul 2026", "Desarrollo", "SNOMED CT con Snowstorm propio; validación de respuestas; estadísticas reales; guardar/reutilizar consultas; limpieza de UI.", "E7, E4, E1, E6", 50, "Hecho"),
    ("S9", "01–15 ago 2026", "Desarrollo", "Conformidad FHIR R5 real (Firely, validador oficial HL7); saneamiento de capas; completado asincrónico; evolución longitudinal; auditoría con diff.", "E3, E9, E10, E5", 60, "Hecho"),
    ("S10", "16–31 ago 2026", "Desarrollo", "Exportación a Excel con gráficos nativos y metadata; SNOMED en ficha; importar preguntas; hardening (Serilog, secretos, bypass); CI con -warnaserror.", "E4, E7, E11", 50, "Hecho"),
    ("S11", "01–15 sep 2026", "Integración", "Spec 011; acuerdo del contrato FHIR con el Hospital; conexión a la Oracle real; mapeo de pacientes y encuentros.", "E12", 26, "En curso"),
    ("S12", "16–30 sep 2026", "Integración", "Autenticación institucional (SSO/roles reales); reemplazo del mock por la base real; primeras pruebas de integración.", "E12", 16, "Pendiente"),
    ("S13", "01–15 oct 2026", "Integración", "Pruebas end-to-end (Playwright) en preproducción; ajustes de conformidad end-to-end; prueba piloto acotada.", "E12", 13, "Pendiente"),
    ("S14", "16–31 oct 2026", "Estabilización", "Plan de migración/despliegue; congelamiento; redacción y armado del informe final.", "E12 + doc", 5, "Pendiente"),
    ("🎓", "01 nov 2026", "Presentación", "Defensa del Trabajo Profesional de Ingeniería en Informática.", "—", "—", "Pendiente"),
]

HITOS = [
    ("Marzo 2026", "Aprobación del anteproyecto (informe pre-proyecto)."),
    ("20-abr-2026", "Acta de acuerdo y aval del director firmada."),
    ("Junio 2026", "Primer incremento funcional: gestor de cuestionarios + exportación a Excel."),
    ("02-ago-2026", "Conformidad FHIR R5 validada contra el validador oficial de HL7."),
    ("26-ago-2026", "Cierre de funcionalidad sobre el mock + hardening de seguridad y CI/CD."),
    ("~sep 2026", "Contrato FHIR acordado y conexión a la base real del Hospital."),
    ("~oct 2026", "Integración end-to-end validada en preproducción + prueba piloto."),
    ("31-oct-2026", "Informe final terminado y congelamiento de código."),
    ("01-nov-2026", "Presentación del Trabajo Profesional."),
]

# ==========================================================================
#  Datos: RIESGOS  (los 4 primeros vienen del anteproyecto)
#  (riesgo, descripcion, P, I, mitigacion, estado)
# ==========================================================================
RIESGOS = [
    ("Curva de aprendizaje HL7 FHIR", "Complejidad en el mapeo de recursos del estándar.", 4, 4,
     "Investigación previa de la especificación y uso de librerías probadas (Firely SDK).", "Mitigado — conformidad lograda el 02-ago-2026"),
    ("Resistencia al cambio", "Que los médicos prefieran seguir usando texto libre.", 3, 5,
     "Involucrar a usuarios finales del Garrahan en el diseño de la UI para que sea más rápida que escribir.", "En seguimiento — se valida en la prueba piloto"),
    ("Cambios en los requerimientos", "Modificaciones en la estructura de las fichas.", 4, 3,
     "Diseñar el sistema de forma modular y dinámica: cambiar cuestionarios no requiere tocar el código.", "Mitigado — editor paramétrico en producción"),
    ("Interoperabilidad fallida", "Dificultad para exportar los datos a otros sistemas.", 2, 5,
     "Validar cada recurso generado con las herramientas de validación oficial de HL7.", "Mitigado — validación oficial integrada"),
    ("Acceso tardío a la base/red del Hospital", "La conexión a la infraestructura real del Garrahan puede demorarse.", 4, 4,
     "Trabajar contra el mock (008) en paralelo; solicitar credenciales de preproducción cuanto antes.", "Abierto — fase de integración (S11)"),
    ("Modelo real distinto al del mock", "Diferencias entre el modelo de datos real del Hospital y el asumido en el mock.", 3, 4,
     "Capa de mapeo aislada en Infrastructure; el contrato FHIR actúa como frontera estable.", "Abierto — fase de integración"),
    ("Política de seguridad institucional (SSO)", "Requisitos de autenticación/autorización del Hospital más estrictos de lo previsto.", 3, 3,
     "Diseñar la autenticación con proveedor intercambiable desde el inicio de S11.", "Abierto — fase de integración"),
    ("Ventana corta antes del 1-nov", "Poco margen entre la integración y la presentación.", 3, 4,
     "S14 reservado a estabilización e informe, sin nueva funcionalidad.", "Abierto — mitigado por planificación"),
    ("Protección de datos sensibles", "Cumplimiento de la Ley 25.326 y 26.529 en la prueba piloto.", 2, 5,
     "Ejecutar el piloto exclusivamente con datos sintéticos o anonimizados, según protocolos del Garrahan.", "Abierto — control previo al piloto"),
]


# ==========================================================================
#  Construcción del libro
# ==========================================================================
def color_estado(estado):
    e = estado.lower()
    if e.startswith("hecho") or e == "entregado":
        return VERDE
    if "curso" in e:
        return NARANJA
    return GRIS_CLARO


def build():
    wb = Workbook()

    # ---------- Hoja 1: BACKLOG ----------
    ws = wb.active
    ws.title = "Backlog"
    ws.sheet_view.showGridLines = False

    ws["A1"] = "ChequeAR — Product Backlog"
    ws["A1"].font = TITLE_FONT
    ws["A2"] = ("Historias de usuario por épica, con prioridad (P1/P2/P3), estimación en puntos de "
                "historia (SP, Planning Poker), sprint y estado. Reconstruido del historial de commits y del anteproyecto.")
    ws["A2"].font = Font(italic=True, color="475569", size=10)
    ws.merge_cells("A2:I2")

    # --- Resumen de épicas ---
    r = 4
    ws.cell(r, 1, "Resumen por épica").font = Font(bold=True, size=12, color=GRIS_HDR)
    r += 1
    hdr = ["Épica", "Nombre", "Módulo (anteproyecto)", "SP", "US", "Estado"]
    for c, h in enumerate(hdr, 1):
        ws.cell(r, c, h)
    style_header(ws, r, len(hdr))
    ep_row0 = r + 1
    total_done = total_pend = 0
    for i, (code, nombre, modulo, estado) in enumerate(EPICAS):
        rr = ep_row0 + i
        sp = sum(u[6] for u in US if u[1] == code)
        n = sum(1 for u in US if u[1] == code)
        if estado == "Entregado":
            total_done += sp
        else:
            total_pend += sp
        vals = [code, nombre, modulo, sp, n, estado]
        for c, v in enumerate(vals, 1):
            cell = ws.cell(rr, c, v)
            cell.border = BORDER
            cell.alignment = CENTER if c in (1, 4, 5) else WRAP
        ws.cell(rr, 1).font = Font(bold=True)
        ws.cell(rr, 6).fill = PatternFill("solid", fgColor=color_estado(estado))
    tot_row = ep_row0 + len(EPICAS)
    ws.cell(tot_row, 1, "TOTAL")
    ws.cell(tot_row, 3, "Entregado: {} SP  ·  Pendiente: {} SP".format(total_done, total_pend))
    ws.cell(tot_row, 4, total_done + total_pend)
    ws.cell(tot_row, 5, len(US))
    for c in range(1, 7):
        ws.cell(tot_row, c).font = Font(bold=True)
        ws.cell(tot_row, c).fill = PatternFill("solid", fgColor=AZUL_CLARO)
        ws.cell(tot_row, c).border = BORDER

    # --- Detalle de user stories ---
    r = tot_row + 3
    ws.cell(r, 1, "Detalle de historias de usuario").font = Font(bold=True, size=12, color=GRIS_HDR)
    r += 1
    hdr = ["ID", "Épica", "Como…", "Quiero…", "Para…", "Prioridad", "SP", "Sprint", "Estado"]
    for c, h in enumerate(hdr, 1):
        ws.cell(r, c, h)
    style_header(ws, r, len(hdr))
    first = r + 1
    for i, u in enumerate(US):
        rr = first + i
        idc, ep, rol, quiero, para, prio, sp, sprint, estado = u
        vals = [idc, ep, rol, quiero, para, prio, sp, sprint, estado]
        for c, v in enumerate(vals, 1):
            cell = ws.cell(rr, c, v)
            cell.border = BORDER
            if c in (1, 2, 6, 7, 8):
                cell.alignment = CENTER
            else:
                cell.alignment = WRAP
        ws.cell(rr, 1).font = Font(bold=True)
        ws.cell(rr, 9).fill = PatternFill("solid", fgColor=color_estado(estado))
        if i % 2 == 1:
            for c in (3, 4, 5):
                ws.cell(rr, c).fill = PatternFill("solid", fgColor=GRIS_CLARO)
    ws.freeze_panes = "A{}".format(first)

    widths = [9, 8, 18, 46, 40, 10, 6, 8, 12]
    for c, w in enumerate(widths, 1):
        ws.column_dimensions[get_column_letter(c)].width = w

    # ---------- Hoja 2: CRONOGRAMA ----------
    ws2 = wb.create_sheet("Cronograma")
    ws2.sheet_view.showGridLines = False
    ws2["A1"] = "ChequeAR — Cronograma (Scrum, sprints de 2 semanas)"
    ws2["A1"].font = TITLE_FONT
    ws2["A2"] = ("Anteproyecto en marzo 2026 · inicio del desarrollo en abril · presentación el 1 de noviembre. "
                 "La fase de integración con el Hospital Garrahan corresponde a los sprints S11–S14.")
    ws2["A2"].font = Font(italic=True, color="475569", size=10)
    ws2.merge_cells("A2:G2")

    r = 4
    hdr = ["Sprint", "Período", "Fase", "Foco / Objetivos", "Épicas", "SP (est.)", "Estado"]
    for c, h in enumerate(hdr, 1):
        ws2.cell(r, c, h)
    style_header(ws2, r, len(hdr))
    first2 = r + 1
    for i, row in enumerate(CRONO):
        rr = first2 + i
        for c, v in enumerate(row, 1):
            cell = ws2.cell(rr, c, v)
            cell.border = BORDER
            if c in (1, 2, 3, 5, 6, 7):
                cell.alignment = CENTER_WRAP
            else:
                cell.alignment = WRAP
        ws2.cell(rr, 1).font = Font(bold=True)
        ws2.cell(rr, 7).fill = PatternFill("solid", fgColor=color_estado(row[6]))

    widths2 = [8, 16, 15, 66, 12, 10, 12]
    for c, w in enumerate(widths2, 1):
        ws2.column_dimensions[get_column_letter(c)].width = w

    # Hitos
    r = first2 + len(CRONO) + 2
    ws2.cell(r, 1, "Hitos").font = Font(bold=True, size=12, color=GRIS_HDR)
    r += 1
    for c, h in enumerate(["Fecha", "Hito"], 1):
        ws2.cell(r, c, h)
    style_header(ws2, r, 2)
    for i, (fecha, hito) in enumerate(HITOS):
        rr = r + 1 + i
        ws2.cell(rr, 1, fecha).alignment = CENTER_WRAP
        ws2.cell(rr, 2, hito).alignment = WRAP
        for c in (1, 2):
            ws2.cell(rr, c).border = BORDER
        ws2.cell(rr, 1).font = Font(bold=True)
    hitos_end = r + len(HITOS)

    # Gantt embebido (si existe el PNG)
    gantt_png = os.path.join(BASE, "img", "gantt_cronograma.png")
    if os.path.exists(gantt_png):
        img = XLImage(gantt_png)
        scale = 900 / img.width
        img.width = int(img.width * scale)
        img.height = int(img.height * scale)
        ws2.add_image(img, "A{}".format(hitos_end + 3))

    # ---------- Hoja 3: RIESGOS ----------
    ws3 = wb.create_sheet("Riesgos")
    ws3.sheet_view.showGridLines = False
    ws3["A1"] = "ChequeAR — Matriz de riesgos"
    ws3["A1"].font = TITLE_FONT
    ws3["A2"] = ("Probabilidad (P) e Impacto (I) en escala 1–5; Criticidad = P × I. "
                 "Los cuatro primeros provienen del anteproyecto; el resto surge de la fase de integración con el Hospital.")
    ws3["A2"].font = Font(italic=True, color="475569", size=10)
    ws3.merge_cells("A2:G2")

    r = 4
    hdr = ["Riesgo", "Descripción", "P (1-5)", "I (1-5)", "Criticidad (P×I)", "Estrategia de mitigación", "Estado"]
    for c, h in enumerate(hdr, 1):
        ws3.cell(r, c, h)
    style_header(ws3, r, len(hdr))
    first3 = r + 1
    ordenados = sorted(RIESGOS, key=lambda x: x[2] * x[3], reverse=True)
    for i, (riesgo, desc, p, imp, mit, estado) in enumerate(ordenados):
        rr = first3 + i
        crit = p * imp
        vals = [riesgo, desc, p, imp, crit, mit, estado]
        for c, v in enumerate(vals, 1):
            cell = ws3.cell(rr, c, v)
            cell.border = BORDER
            if c in (3, 4, 5):
                cell.alignment = CENTER
            elif c == 7:
                cell.alignment = CENTER_WRAP
            else:
                cell.alignment = WRAP
        ws3.cell(rr, 1).font = Font(bold=True)
        # Color de criticidad
        if crit >= 15:
            cf = "FECACA"      # rojo claro
        elif crit >= 10:
            cf = "FEF08A"      # amarillo
        else:
            cf = "DCFCE7"      # verde
        ws3.cell(rr, 5).fill = PatternFill("solid", fgColor=cf)
        est = estado.lower()
        ws3.cell(rr, 7).fill = PatternFill(
            "solid", fgColor=(VERDE if est.startswith("mitigado") else NARANJA if "abierto" in est else AZUL_CLARO))

    widths3 = [30, 40, 9, 9, 15, 46, 34]
    for c, w in enumerate(widths3, 1):
        ws3.column_dimensions[get_column_letter(c)].width = w
    ws3.freeze_panes = "A{}".format(first3)

    wb.save(OUT)
    print("Libro Excel generado en", OUT)
    print("Backlog: {} US · {} SP entregados · {} SP pendientes".format(
        len(US), total_done, total_pend))


if __name__ == "__main__":
    build()
