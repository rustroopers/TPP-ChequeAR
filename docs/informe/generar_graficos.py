# -*- coding: utf-8 -*-
"""Genera los gráficos del informe final de ChequeAR (PNG, 300 dpi).

Uso:  python docs/informe/generar_graficos.py
Salida: docs/informe/img/*.png

Línea de tiempo: anteproyecto en marzo 2026, inicio de desarrollo en abril,
presentación el 1 de noviembre. Sprints Scrum de 2 semanas.
"""
import os
from datetime import date
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Patch
import matplotlib.dates as mdates

OUT = os.path.join(os.path.dirname(__file__), "img")
os.makedirs(OUT, exist_ok=True)

AZUL = "#2563eb"; VERDE = "#16a34a"; NARANJA = "#ea580c"
GRIS = "#94a3b8"; TEAL = "#0d9488"; ROJO = "#dc2626"
plt.rcParams.update({"font.size": 10, "axes.edgecolor": "#cbd5e1",
                     "axes.grid": True, "grid.color": "#e2e8f0", "grid.linewidth": 0.6})

# (etiqueta, inicio, fin, fase)  fase: previa / setup / build / integ
SPRINTS = [
    ("Anteproyecto (informe pre-proyecto)", date(2026, 3, 1),  date(2026, 3, 31), "previa"),
    ("S1 · Kickoff y relevamiento",         date(2026, 4, 1),  date(2026, 4, 15), "setup"),
    ("S2 · Arquitectura y specs",           date(2026, 4, 16), date(2026, 4, 30), "setup"),
    ("S3 · Dominio y frontend shell",       date(2026, 5, 1),  date(2026, 5, 15), "build"),
    ("S4 · Mock Hospital y wiring",         date(2026, 5, 16), date(2026, 5, 31), "build"),
    ("S5 · Cuestionarios y 1ra exportación", date(2026, 6, 1),  date(2026, 6, 15), "build"),
    ("S6 · Oracle EF real y mock",          date(2026, 6, 16), date(2026, 6, 30), "build"),
    ("S7 · Datos reales y editor completo", date(2026, 7, 1),  date(2026, 7, 15), "build"),
    ("S8 · SNOMED CT y reportes con stats", date(2026, 7, 16), date(2026, 7, 31), "build"),
    ("S9 · FHIR R5, asincronía y auditoría", date(2026, 8, 1),  date(2026, 8, 15), "build"),
    ("S10 · Reportes avanzados y hardening", date(2026, 8, 16), date(2026, 8, 31), "build"),
    ("S11 · Integración: contrato FHIR",    date(2026, 9, 1),  date(2026, 9, 15), "integ"),
    ("S12 · Integración: auth y mock real", date(2026, 9, 16), date(2026, 9, 30), "integ"),
    ("S13 · Integración: E2E y piloto",     date(2026, 10, 1), date(2026, 10, 15), "integ"),
    ("S14 · Estabilización e informe",      date(2026, 10, 16), date(2026, 10, 31), "integ"),
]

# Épicas (sin i18n). (etiqueta, SP, entregada?)
EPICAS = [
    ("E1 Gestor y Editor de Cuestionarios", 58, True),
    ("E3 Interoperabilidad FHIR R5", 55, True),
    ("E4 Análisis y Exportación", 45, True),
    ("E6 Frontend / UX", 34, True),
    ("E2 Motor de Fichas", 28, True),
    ("E5 Seguridad y Auditoría", 29, True),
    ("E11 Calidad / CI-CD", 23, True),
    ("E7 SNOMED CT", 21, True),
    ("E9 Completado asincrónico", 21, True),
    ("E10 Evolución longitudinal", 13, True),
    ("E8 Mock Hospital", 13, True),
    ("E12 Integración Hospital", 60, False),
]


def gantt():
    color = {"previa": TEAL, "setup": AZUL, "build": VERDE, "integ": NARANJA}
    fig, ax = plt.subplots(figsize=(12, 6.2))
    for i, (nombre, ini, fin, fase) in enumerate(reversed(SPRINTS)):
        ax.barh(i, (fin - ini).days, left=ini, height=0.6,
                color=color[fase], edgecolor="white")
        ax.text(ini, i, "  " + nombre, va="center", ha="left",
                fontsize=8.5, color="#0f172a")
    ax.set_yticks([]); ax.set_ylim(-0.6, len(SPRINTS) - 0.4)
    pres = date(2026, 11, 1)
    ax.axvline(pres, color=ROJO, linestyle="--", linewidth=1.6)
    ax.text(pres, len(SPRINTS) - 0.7, " Presentación\n 1-nov", color=ROJO,
            fontsize=9, va="top", ha="left", fontweight="bold")
    ax.xaxis.set_major_locator(mdates.MonthLocator())
    ax.xaxis.set_major_formatter(mdates.DateFormatter("%b\n%Y"))
    ax.set_xlim(date(2026, 2, 22), date(2026, 11, 20))
    ax.set_title("Cronograma del proyecto ChequeAR — Scrum, sprints de 2 semanas",
                 fontsize=13, fontweight="bold", pad=12)
    leyenda = [Patch(color=TEAL, label="Anteproyecto"),
               Patch(color=AZUL, label="Setup / diseño"),
               Patch(color=VERDE, label="Desarrollo (entregado)"),
               Patch(color=NARANJA, label="Integración Hospital")]
    ax.legend(handles=leyenda, loc="upper center", bbox_to_anchor=(0.5, -0.11),
              ncol=4, frameon=False)
    ax.grid(axis="y", visible=False)
    fig.tight_layout(); fig.subplots_adjust(bottom=0.16)
    fig.savefig(os.path.join(OUT, "gantt_cronograma.png"), dpi=300)
    plt.close(fig)


def esfuerzo_epicas():
    datos = sorted(EPICAS, key=lambda x: x[1])
    nombres = [e[0] for e in datos]
    sp = [e[1] for e in datos]
    colores = [AZUL if e[2] else NARANJA for e in datos]
    fig, ax = plt.subplots(figsize=(10, 6))
    ax.barh(nombres, sp, color=colores, edgecolor="white")
    for i, v in enumerate(sp):
        ax.text(v + 0.6, i, str(v), va="center", fontsize=9)
    ax.set_xlabel("Puntos de historia (SP)")
    ax.set_title("Esfuerzo estimado por épica (Planning Poker)",
                 fontsize=13, fontweight="bold", pad=12)
    ax.legend(handles=[Patch(color=AZUL, label="Entregado"),
                       Patch(color=NARANJA, label="Pendiente (integración)")],
              loc="lower right", frameon=False)
    ax.grid(axis="y", visible=False)
    fig.tight_layout()
    fig.savefig(os.path.join(OUT, "esfuerzo_epicas.png"), dpi=300)
    plt.close(fig)


if __name__ == "__main__":
    gantt()
    esfuerzo_epicas()
    print("Gráficos generados en", OUT)
