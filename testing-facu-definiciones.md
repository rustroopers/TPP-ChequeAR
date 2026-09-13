# Definiciones pendientes de producto/UX

Mensaje para llevar a Carolo, Gonza y equipo. Consolida las 11 decisiones que no
puedo tomar solo desde el testing manual — están detalladas en
[`testing-facu-feedback.md`](testing-facu-feedback.md) (columna "🔵").

> Rama: `testing-hecho-por-facu`
> Última actualización: 2026-09-06

---

Chicos, necesito su ayuda para desbloquear el testing de ChequeAR 🙏

En la última pasada de testing quedaron 11 preguntas de producto/UX que no puedo
decidir yo solo. Están todas acá abajo — respondan inline (o si prefieren
armamos 30' de call). Puse las opciones que veo en cada una y **en negrita la
que me parece razonable**, para que sea más rápido de vetar/aprobar.

---

## 🔷 Editor de cuestionarios (Carolo)

### D1 — Ícono de "Pacientes" en la sidebar

Cambié provisoriamente `person-vcard` (tarjeta de identidad) por `person-heart`
(más acorde a historia clínica pediátrica). ¿Lo dejamos, volvemos al anterior,
o probamos otro?

Opciones: **`person-heart`** ✅ (actual) · `person-vcard` · `clipboard-heart` ·
otra sugerencia.

### D2 — SNOMED sin match exacto

El médico busca un término y no aparece en Snowstorm.

- (a) Bloquear entrada manual y forzar autocomplete.
- (b) Permitir manual + validación async (ya está el botón "Verificar" que
  compara contra el término oficial).
- **(c)** Permitir manual pero marcar la pregunta como "no codificada" para no
  contaminar reportes.

¿Cuál preferís?

### D3 — Cita textual del paciente

Casos tipo Munchausen requieren capturar la frase literal (*"me duele acá"*)
además del término clínico.

- (a) Campo libre paralelo: "Texto del paciente" + "Interpretación clínica".
- **(b)** Anotación tipo highlight sobre un párrafo de relato.
- (c) Lo dejamos para más adelante.

### D4 — Comentarios en cuestionarios/preguntas

¿Necesitan poder comentar entre médicos (revisión pre-publicación, notas)?

Opciones: **Sí, a nivel pregunta y cuestionario** · Sí, sólo cuestionario · No.

### D5 — Concepto de "sección"

Hoy el ícono de carpeta agrupa preguntas pero no las colapsa visualmente. ¿Qué
queremos?

- **(a)** Agrupador real: colapsar preguntas dentro de la sección.
- (b) Sacar el concepto: preguntas planas, ordenables por drag.

---

## 🔷 Lista de cuestionarios (Gonza)

### D6 — Ordenamiento por defecto

¿Fecha de creación desc, último uso, alfabético?

Sugerencia: **último uso desc** (lo más relevante arriba).

### D7 — Archivar vs. borrar

Reglas propuestas:

- Si nunca se usó → permitir borrado duro (con confirmación).
- Si ya se usó en una ficha → sólo archivar (integridad referencial).

¿OK así o preferís que archivar sea la única opción siempre?

### D8 — Estado vs. archivado

Si archivar cambia el estado, ¿sigue teniendo sentido la columna "Estado"
separada? Propongo **fusionar en una sola columna "Estado"** con valores:
Borrador / Publicado / Archivado.

### D9 — Título de la lista

Hoy dice "TRABAJO" y no dice nada.

- (a) Mis cuestionarios (los que creé yo).
- **(b)** Cuestionarios del servicio.
- (c) Cuestionarios activos (mezcla borradores y publicados).

Depende del scope real — ¿son personales, del servicio o del hospital?

---

## 🔷 Pantalla de paciente

### D10 — Pacientes es un buscador, no un listado

¿Fue decisión por privacidad, por volumen (200k pacientes no paginables), o UX
intencional? ¿Sumamos un modo "listado + filtros" para casos como "ver todos
los pacientes de mi último turno", o mantenemos sólo búsqueda?

### D11 — Propósito del detalle de ficha

¿Qué se puede cargar ahí? ¿Es sólo lectura de la ficha original, o admite
anotaciones/adjuntos posteriores? La card interna "Sin movimientos registrados"
está pensada para eso — necesito saber qué "movimientos" caben.

---

**Contexto**: todo lo demás del testing manual ya está implementado y pusheado
en la rama `testing-hecho-por-facu` (5 commits, del `4b628e1` al `702f6b1`).
Ver [`testing-facu-avance.md`](testing-facu-avance.md) y
[`testing-facu-feedback.md`](testing-facu-feedback.md) en el repo si quieren
el detalle.

Cualquier respuesta parcial ayuda — no hace falta que contesten todas juntas.

Gracias 🚀
