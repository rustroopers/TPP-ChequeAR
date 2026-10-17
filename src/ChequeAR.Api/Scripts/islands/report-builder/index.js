/*
 * Isla Vue: report-builder (pantalla E2)
 * Reporte sobre un cuestionario base + condiciones por pregunta + rango de fechas.
 * El grupo de fichas resultante se previsualiza contra POST /api/reports/query y se
 * exporta a Excel/CSV (POST /api/reports/export).
 */
(function () {
    function token() { return window.ChequeARIslands.auth.token(); }
    function authHeaders() { return window.ChequeARIslands.auth.headers(true); }
    // Questionnaire.item.type de FHIR R5 -> categoría de filtro.
    //
    // Antes llegaba el enum del backend serializado como número; ahora llega el recurso FHIR,
    // así que el tipo es un string del estándar. 'coding' cubre opción cerrada y abierta:
    // para filtrar da lo mismo, en los dos casos se compara contra un código.
    // Opciones de pantalla para el operador 'exists' de la API (que viaja con true/false).
    var ANSWERED = 'answered', UNANSWERED = 'unanswered';

    function typeCategory(t) {
        switch (t) {
            case 'decimal': return 'decimal';
            case 'integer': return 'integer';
            case 'date':
            case 'dateTime': return 'date';
            case 'boolean': return 'boolean';
            case 'coding': return 'choice';
            // Vocabulario R4 de las versiones publicadas antes de la feature 009.
            case 'choice':
            case 'open-choice': return 'choice';
            default: return 'string';
        }
    }
    // Questionnaire.status de FHIR -> etiqueta y orden del picker. Publicado primero (es lo
    // que se usa para cargar datos nuevos); Archivado al final (solo sirve para reportar
    // sobre lo ya cargado).
    var STATUS_LABEL = { draft: 'Borrador', active: 'Publicado', retired: 'Archivado' };
    var STATUS_ORDER = ['active', 'draft', 'retired'];

    // Etiquetas en palabras para que cada condición se lea como una frase:
    // "PCR sea mayor a 5", "Tratamiento actual sea Corticoides".
    function opsFor(q) {
        var type = q && q.type;
        var ops;
        if (type === 'integer' || type === 'decimal')
            ops = [{ v: '=', l: 'sea igual a' }, { v: '!=', l: 'sea distinto de' }, { v: '>', l: 'sea mayor a' }, { v: '>=', l: 'sea mayor o igual a' }, { v: '<', l: 'sea menor a' }, { v: '<=', l: 'sea menor o igual a' }, { v: 'between', l: 'esté entre' }];
        else if (type === 'choice')
            ops = [{ v: '=', l: 'sea' }, { v: '!=', l: 'no sea' }, { v: 'in', l: 'sea alguna de' }];
        else if (type === 'boolean')
            ops = [{ v: '=', l: 'sea' }];
        else if (type === 'date')
            ops = [{ v: '=', l: 'sea del' }, { v: '>=', l: 'sea desde' }, { v: '<=', l: 'sea hasta' }, { v: 'between', l: 'esté entre' }];
        else
            // Texto libre: lo que se escribe a mano casi nunca coincide letra por
            // letra, así que el default es "contenga". Las tres ignoran mayúsculas
            // y tildes (lo resuelve el servidor).
            ops = [{ v: 'contains', l: 'contenga' }, { v: 'notcontains', l: 'no contenga' }, { v: '=', l: 'sea exactamente' }];

        // Una pregunta obligatoria siempre está respondida en una ficha finalizada:
        // ofrecerlo ahí sería un filtro que no filtra nada.
        if (q && !q.required)
            ops = ops.concat([{ v: ANSWERED, l: 'esté respondida' }, { v: UNANSWERED, l: 'no esté respondida' }]);
        return ops;
    }
    // "Esté respondida" no compara contra un valor, así que no lleva campo de valor.
    function needsValue(op) { return op !== ANSWERED && op !== UNANSWERED; }

    function newCondition(q) {
        return { linkId: q.linkId, op: opsFor(q)[0].v, value: q.type === 'boolean' ? 'true' : '', value2: '', values: [] };
    }

    // Una condición: pregunta, operador y valor (dos para "entre", varias opciones para
    // "sea alguna de"). Se usa suelta y dentro de un grupo.
    var FilterRow = {
        props: { f: { type: Object, required: true }, questions: { type: Array, required: true } },
        emits: ['remove'],
        setup: function (props) {
            function question() {
                return props.questions.find(function (q) { return q.linkId === props.f.linkId; }) || {};
            }
            function onQuestionChange() {
                var c = newCondition(question());
                props.f.op = c.op; props.f.value = c.value; props.f.value2 = ''; props.f.values = [];
            }
            function inputType() {
                var t = question().type;
                return t === 'date' ? 'date' : (t === 'integer' || t === 'decimal' ? 'number' : 'text');
            }
            return { question: question, onQuestionChange: onQuestionChange, inputType: inputType, opsFor: opsFor, needsValue: needsValue };
        },
        template: '\
<div class="row g-2 align-items-center mb-2">\
    <div class="col-auto cq-text-muted pe-0" style="width:1.2rem;">•</div>\
    <div class="col-md-4">\
        <select v-model="f.linkId" @change="onQuestionChange" class="form-select form-select-sm" aria-label="Pregunta">\
            <option v-for="q in questions" :key="q.linkId" :value="q.linkId">{{ q.text }}</option>\
        </select>\
    </div>\
    <div class="col-md-3">\
        <select v-model="f.op" class="form-select form-select-sm" aria-label="Condición">\
            <option v-for="o in opsFor(question())" :key="o.v" :value="o.v">{{ o.l }}</option>\
        </select>\
    </div>\
    <div class="col-md-4">\
        <span v-if="!needsValue(f.op)"></span>\
        <select v-else-if="question().type === \'boolean\'" v-model="f.value" class="form-select form-select-sm" aria-label="Valor">\
            <option value="true">Sí</option><option value="false">No</option>\
        </select>\
        <select v-else-if="question().type === \'choice\' && f.op === \'in\'" v-model="f.values" multiple class="form-select form-select-sm" aria-label="Opciones" size="3">\
            <option v-for="o in question().options" :key="o.value" :value="o.value">{{ o.display }}</option>\
        </select>\
        <select v-else-if="question().type === \'choice\'" v-model="f.value" class="form-select form-select-sm" aria-label="Valor">\
            <option value="">—</option>\
            <option v-for="o in question().options" :key="o.value" :value="o.value">{{ o.display }}</option>\
        </select>\
        <div v-else-if="f.op === \'between\'" class="d-flex align-items-center gap-1">\
            <input v-model="f.value" class="form-control form-control-sm" :type="inputType()" aria-label="Desde" />\
            <span class="cq-text-sm cq-text-muted">y</span>\
            <input v-model="f.value2" class="form-control form-control-sm" :type="inputType()" aria-label="Hasta" />\
        </div>\
        <input v-else v-model="f.value" class="form-control form-control-sm" :type="inputType()" aria-label="Valor" />\
    </div>\
    <div class="col-md-auto text-end">\
        <button type="button" class="cq-icon-btn cq-icon-btn--danger" @click="$emit(\'remove\')" aria-label="Eliminar condición" title="Eliminar condición"><i class="bi bi-trash cq-icon-btn__idle"></i><i class="bi bi-trash-fill cq-icon-btn__hover"></i></button>\
    </div>\
</div>'
    };

    var ReportBuilder = {
        components: { 'cq-filter-row': FilterRow },
        props: { reportId: { type: String, default: null } },
        setup: function (props) {
            var questionnaires = Vue.ref([]);
            var selectedId = Vue.ref(null);
            var questions = Vue.ref([]);      // preguntas del cuestionario elegido
            var filters = Vue.ref([]);        // condiciones y grupos { group, logic, items }
            var combineMode = Vue.ref('AND');
            var dateFrom = Vue.ref('');
            var dateTo = Vue.ref('');
            var rows = Vue.ref([]);
            var columns = Vue.ref([]);
            var total = Vue.ref(null);
            var page = Vue.ref(1);
            var PAGE_SIZE = 25;
            var totalPages = Vue.computed(function () {
                return Math.max(1, Math.ceil((total.value || 0) / PAGE_SIZE));
            });
            var loading = Vue.ref(false);
            var exporting = Vue.ref(false);
            var error = Vue.ref(null);

            // Consulta guardada. Si la pantalla se abrió con un id (editar), currentReportId
            // arranca con ese valor y Guardar hace PUT; si no, el primer Guardar hace POST y
            // recién ahí queda el id.
            var currentReportId = Vue.ref(props.reportId || null);
            var reportName = Vue.ref('');
            var saving = Vue.ref(false);
            var showSave = Vue.ref(false);
            // Mientras se rehidrata una consulta guardada, el cambio programático de
            // cuestionario NO debe limpiar los filtros que se están prefilleando.
            var rehydrating = false;

            function toast(m, k) { if (window.cqToast) window.cqToast(m, k); }

            function questionByLink(linkId) {
                return questions.value.find(function (q) { return q.linkId === linkId; });
            }

            // Devuelve una promesa: rehidratar una consulta guardada necesita esperar a que
            // las preguntas estén cargadas antes de reconstruir los filtros (que las
            // referencian por linkId). No limpia `filters`: de eso se encarga el watch.
            // Aplana el árbol de item[] del recurso FHIR. Las secciones anidan, así que hay
            // que recorrer en profundidad: con la lista plana de antes, las preguntas dentro
            // de una sección quedaban invisibles para el constructor de filtros.
            function flattenItems(items, out) {
                (items || []).forEach(function (it) {
                    if (it.type === 'group') { flattenItems(it.item, out); return; }
                    out.push({
                        linkId: it.linkId,
                        text: it.text,
                        type: typeCategory(it.type),
                        required: !!it.required,
                        options: (it.answerOption || []).map(function (o) {
                            var c = o.valueCoding || {};
                            return { value: c.code, display: c.display || c.code };
                        })
                    });
                });
                return out;
            }

            var questionsSeq = window.ChequeARIslands.http.latest();
            function loadQuestions(id) {
                var vigente = questionsSeq();
                questions.value = [];
                if (!id) return Promise.resolve();
                // Las preguntas de las versiones publicadas, que son contra las que se cargaron las fichas.
                return fetch('/api/reports/questionnaires/' + id + '/structure', { headers: authHeaders() })
                    .then(window.ChequeARIslands.http.ensureOk)
                    .then(function (r) { return r.json(); })
                    .then(function (q) { if (vigente()) questions.value = flattenItems(q.item, []); })
                    .catch(function () { /* sin preguntas: se puede filtrar igual por fecha */ });
            }

            // Rehidrata el editor desde una consulta guardada (flujo "Editar consulta").
            function loadSavedReport(id) {
                fetch('/api/reports/saved/' + id, { headers: authHeaders() })
                    .then(window.ChequeARIslands.http.ensureOk)
                    .then(function (r) { return r.json(); })
                    .then(function (rep) {
                        reportName.value = rep.Name || rep.name || '';
                        var query = {};
                        try { query = JSON.parse(rep.QueryJson || rep.queryJson || '{}'); } catch (e) { query = {}; }

                        rehydrating = true;
                        selectedId.value = rep.QuestionnaireId || rep.questionnaireId || query.questionnaireId;
                        loadQuestions(selectedId.value).then(function () {
                            if (query.dateRange) {
                                dateFrom.value = (query.dateRange.from || '').slice(0, 10);
                                dateTo.value = (query.dateRange.to || '').slice(0, 10);
                            }
                            if (query.filters) {
                                combineMode.value = query.filters.logic || 'AND';
                                filters.value = (query.filters.predicates || []).map(fromPredicate);
                            }
                            rehydrating = false;
                            // Cargar desde el server es el estado "sin cambios pendientes"
                            // más literal posible: los watchers que corren durante la
                            // rehidratación ya fueron ignorados por markDirty().
                            dirty.value = false;
                        });
                    })
                    .catch(function () { error.value = 'No se pudo cargar el reporte guardado.'; rehydrating = false; });
            }

            // Agrupado por estado (Publicado / Borrador / Archivado): sin esto el <select>
            // mezclaba todo con el mismo aspecto y no había forma de saber, por ejemplo, que
            // el cuestionario que tocaba para el reporte estaba archivado (ni de distinguir
            // entre dos con el mismo título "Nuevo cuestionario" en estados distintos).
            var groupedQuestionnaires = Vue.computed(function () {
                var byStatus = {};
                questionnaires.value.forEach(function (q) {
                    (byStatus[q.status] = byStatus[q.status] || []).push(q);
                });
                return STATUS_ORDER
                    .filter(function (s) { return byStatus[s] && byStatus[s].length; })
                    .map(function (s) { return { label: STATUS_LABEL[s] || 'Otro', items: byStatus[s] }; });
            });

            // Combobox buscable del cuestionario base: se puede tipear y filtra en vivo,
            // conservando el agrupado por estado. selectedId sigue siendo la fuente de verdad.
            var qSearch = Vue.ref('');
            var qOpen = Vue.ref(false);
            var qBox = Vue.ref(null);
            var selectedTitle = Vue.computed(function () {
                var f = questionnaires.value.find(function (q) { return q.id === selectedId.value; });
                return f ? f.title : '';
            });
            var filteredGroups = Vue.computed(function () {
                var s = (qSearch.value || '').trim().toLowerCase();
                if (!s) return groupedQuestionnaires.value;
                return groupedQuestionnaires.value
                    .map(function (g) {
                        return { label: g.label, items: g.items.filter(function (q) { return (q.title || '').toLowerCase().indexOf(s) >= 0; }) };
                    })
                    .filter(function (g) { return g.items.length; });
            });
            // Todas las páginas del Bundle, siguiendo el link "next": con una sola página, lo
            // que pasaba de ahí no aparecía en el selector.
            function fetchAllEntries(url, acc) {
                acc = acc || [];
                return fetch(url, { headers: authHeaders() })
                    .then(window.ChequeARIslands.http.ensureOk)
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        acc = acc.concat((data && data.entry) || []);
                        var next = ((data && data.link) || []).filter(function (l) { return l.relation === 'next'; })[0];
                        return next ? fetchAllEntries(next.url, acc) : acc;
                    });
            }

            function pickQuestionnaire(q) { selectedId.value = q.id; qSearch.value = ''; qOpen.value = false; }

            Vue.onMounted(function () {
                // Cerrar el combobox al hacer clic fuera.
                document.addEventListener('click', function (e) {
                    if (qOpen.value && qBox.value && !qBox.value.contains(e.target)) qOpen.value = false;
                });
                document.addEventListener('keydown', function (e) {
                    if (e.key === 'Escape') qOpen.value = false;
                });
                fetchAllEntries('/fhir/r5/Questionnaire?_count=200')
                    .then(function (list) {
                        questionnaires.value = list
                            .map(function (e) { return e.resource; })
                            .filter(Boolean)
                            .map(function (q) {
                                return { id: q.id, title: q.title, status: q.status };
                            });
                        // Editar una consulta guardada manda su cuestionario; en ese caso no
                        // se pisa con el primero de la lista. Si no, arranca con el primero
                        // del primer grupo (Publicado antes que Borrador o Archivado).
                        if (currentReportId.value) loadSavedReport(currentReportId.value);
                        else if (groupedQuestionnaires.value.length) selectedId.value = groupedQuestionnaires.value[0].items[0].id;
                    })
                    .catch(function () { error.value = 'No se pudieron cargar los cuestionarios.'; });
            });

            // Cambiar de cuestionario invalida los filtros (referencian preguntas de otro).
            // Salvo durante la rehidratación, que los está reconstruyendo a propósito.
            Vue.watch(selectedId, function (id) {
                if (rehydrating) return;
                filters.value = [];
                loadQuestions(id);
            });

            // Auto-actualización (estilo debounce): apenas cambian cuestionario, fechas o
            // condiciones, se re-previsualiza y se recalculan las estadísticas — sin botones.
            var autoTimer = null;
            function scheduleAuto() {
                if (autoTimer) clearTimeout(autoTimer);
                autoTimer = setTimeout(function () {
                    if (!selectedId.value) return;
                    preview(1);
                    stats();
                }, 500);
            }
            Vue.watch([selectedId, dateFrom, dateTo, combineMode], scheduleAuto);
            Vue.watch(filters, scheduleAuto, { deep: true });

            // "Guardar cambios" tiene que apagarse cuando no hay nada nuevo que
            // guardar. Feedback: apenas se guardaba, el botón quedaba clickeable
            // permitiendo re-POST/PUT vacíos y confundía si el guardado había
            // funcionado. Marca cambios en el query y en el nombre; se limpia al
            // rehidratar desde el server o después de un save exitoso.
            var dirty = Vue.ref(false);
            function markDirty() { if (!rehydrating) dirty.value = true; }
            Vue.watch([selectedId, dateFrom, dateTo, combineMode, reportName], markDirty);
            Vue.watch(filters, markDirty, { deep: true });

            function addFilter() {
                if (!questions.value.length) { toast('Este cuestionario no tiene preguntas para filtrar.', 'info'); return; }
                filters.value.push(newCondition(questions.value[0]));
            }
            // Un grupo es un paréntesis: sus condiciones se combinan entre sí con su propio
            // "todas / al menos una", y el grupo entero cuenta como una condición más.
            function addGroup() {
                if (!questions.value.length) { toast('Este cuestionario no tiene preguntas para filtrar.', 'info'); return; }
                filters.value.push({ group: true, logic: 'OR', items: [newCondition(questions.value[0]), newCondition(questions.value[0])] });
            }
            function addToGroup(g) { g.items.push(newCondition(questions.value[0])); }
            function removeFilter(i) { filters.value.splice(i, 1); }

            function fromPredicate(p) {
                if (p.group) return { group: true, logic: p.group.logic || 'AND', items: (p.group.predicates || []).map(fromPredicate) };
                var q = questionByLink(p.linkId);
                // 'exists' viaja con true/false; en pantalla son dos opciones.
                if (p.op === 'exists')
                    return { linkId: p.linkId, op: (p.value === false || p.value === 'false') ? UNANSWERED : ANSWERED, value: '', value2: '', values: [] };
                if (p.op === 'between')
                    return { linkId: p.linkId, op: p.op, value: (p.value || [])[0], value2: (p.value || [])[1], values: [] };
                if (p.op === 'in')
                    return { linkId: p.linkId, op: p.op, value: '', value2: '', values: (p.value || []).slice() };
                // El select de un booleano espera el string 'true'/'false'; guardado viene como bool.
                var val = (q && q.type === 'boolean') ? String(p.value) : p.value;
                return { linkId: p.linkId, op: p.op, value: val, value2: '', values: [] };
            }

            function toPredicate(f) {
                if (f.group) {
                    var internos = f.items.map(toPredicate).filter(Boolean);
                    return internos.length ? { group: { logic: f.logic, predicates: internos } } : null;
                }
                var q = questionByLink(f.linkId);
                if (!q || !f.op) return null;
                if (!needsValue(f.op)) return { linkId: f.linkId, op: 'exists', value: f.op === ANSWERED };
                if (f.op === 'between') {
                    if (f.value === '' || f.value == null || f.value2 === '' || f.value2 == null) return null;
                    return { linkId: f.linkId, op: 'between', value: [coerce(q, f.value), coerce(q, f.value2)] };
                }
                if (f.op === 'in') return f.values && f.values.length ? { linkId: f.linkId, op: 'in', value: f.values.slice() } : null;
                if (q.type !== 'boolean' && (f.value === '' || f.value == null)) return null;
                return { linkId: f.linkId, op: f.op, value: coerce(q, f.value) };
            }

            function coerce(q, raw) {
                if (!q) return raw;
                if (q.type === 'integer') return parseInt(raw, 10);
                if (q.type === 'decimal') return parseFloat(raw);
                if (q.type === 'boolean') return (raw === true || raw === 'true');
                return raw;
            }

            // Sincroniza el rango desde el componente <cq-date-range>.
            function onRangeChange(e) {
                dateFrom.value = e.detail.from || '';
                dateTo.value = e.detail.to || '';
            }

            function buildQuery(format) {
                var query = { questionnaireId: selectedId.value };
                if (dateFrom.value || dateTo.value) {
                    query.dateRange = {
                        from: dateFrom.value ? dateFrom.value + 'T00:00:00' : null,
                        to: dateTo.value ? dateTo.value + 'T23:59:59' : null
                    };
                }
                var preds = filters.value.map(toPredicate).filter(Boolean);
                if (preds.length) query.filters = { logic: combineMode.value, predicates: preds };
                if (format) query.format = format;
                return query;
            }

            // El boton "Previsualizar" pasa el Event como argumento (no un numero): en ese
            // caso arranca en la pagina 1. Anterior/Siguiente pasan el numero de pagina.
            function preview(goToPage) {
                if (!selectedId.value) { toast('Seleccione un cuestionario.', 'error'); return; }
                var p = (typeof goToPage === 'number' && goToPage > 0) ? goToPage : 1;
                loading.value = true; error.value = null;
                var q = buildQuery();
                q.page = p;
                q.pageSize = PAGE_SIZE;
                fetch('/api/reports/query', { method: 'POST', headers: authHeaders(), body: JSON.stringify(q) })
                    .then(window.ChequeARIslands.http.ensureOk)
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        // total es el de la COHORTE, no el de la pagina: lo usa totalPages.
                        total.value = data.total;
                        page.value = data.page || p;
                        rows.value = data.rows || [];
                        columns.value = rows.value.length ? Object.keys(rows.value[0]) : [];
                    })
                    .catch(function (e) { error.value = 'Error al previsualizar: ' + e.message; })
                    .then(function () { loading.value = false; });
            }

            // Estadisticas de la cohorte ACTUAL. Se le pasa la misma query que a
            // /query: pedirlas por id de cuestionario daria numeros que no corresponden a
            // los filtros que la persona tiene puestos en pantalla.
            function stats() {
                if (!selectedId.value) { toast('Seleccione un cuestionario.', 'error'); return; }
                window.dispatchEvent(new CustomEvent('cq:report-query', { detail: buildQuery() }));
            }

            function openSave() {
                if (!selectedId.value) { toast('Seleccione un cuestionario.', 'error'); return; }
                showSave.value = true;
            }

            // Guarda la consulta (definicion, no resultados): el cuestionario base y los
            // filtros/fechas actuales, para volver a correrla despues. Es POST la primera
            // vez y PUT sobre la misma si ya existia. El backend solo deja actualizar al
            // dueño (403 si no).
            function saveReport() {
                if (!selectedId.value) { toast('Seleccione un cuestionario.', 'error'); return; }
                var name = (reportName.value || '').trim();
                if (!name) { toast('Ingrese un nombre para el reporte.', 'error'); return; }
                if (saving.value) return;

                saving.value = true;
                var editing = !!currentReportId.value;
                var body = {
                    // scope 'private' fijo por ahora: compartir por especialidad depende del
                    // login contra el hospital, que todavía no está cableado.
                    name: name,
                    scope: 'private',
                    questionnaireId: selectedId.value,
                    queryJson: JSON.stringify(buildQuery())
                };
                var url = editing ? '/api/reports/saved/' + currentReportId.value : '/api/reports/saved';
                fetch(url, { method: editing ? 'PUT' : 'POST', headers: authHeaders(), body: JSON.stringify(body) })
                    .then(window.ChequeARIslands.http.ensureOk)
                    .then(function (r) { return r.json().catch(function () { return null; }); })
                    .then(function (saved) {
                        if (saved && !editing) {
                            var id = saved.Id || saved.id;
                            if (id) {
                                currentReportId.value = id;
                                // Reflejar que ahora es una consulta existente (siguiente guardado = PUT).
                                if (window.history && window.history.replaceState)
                                    window.history.replaceState(null, '', '/Reports/Build/' + id);
                            }
                        }
                        showSave.value = false;
                        dirty.value = false;
                        toast(editing ? 'Cambios guardados.' : 'Reporte guardado.', 'success');
                    })
                    .catch(function (e) { toast('No se pudo guardar: ' + e.message, 'error'); })
                    .then(function () { saving.value = false; });
            }

            // Nombre + " - reporte - fecha.ext", sin caracteres invalidos para nombre de archivo.
            function exportFilename(ext) {
                var base = (selectedTitle.value || 'reporte').trim();
                var safe = base.replace(/[\\/:*?"<>|]/g, '').trim() || 'reporte';
                var today = new Date();
                var pad = function (n) { return String(n).padStart(2, '0'); };
                var dateStr = today.getFullYear() + '-' + pad(today.getMonth() + 1) + '-' + pad(today.getDate());
                return safe + ' - reporte - ' + dateStr + '.' + ext;
            }

            function exportFile(format, filename) {
                if (!selectedId.value) { toast('Seleccione un cuestionario.', 'error'); return; }
                exporting.value = true;
                fetch('/api/reports/export', { method: 'POST', headers: authHeaders(), body: JSON.stringify(buildQuery(format)) })
                    .then(window.ChequeARIslands.http.ensureOk)
                    .then(function (r) { return r.blob(); })
                    .then(function (blob) {
                        var url = URL.createObjectURL(blob);
                        var a = document.createElement('a');
                        a.href = url; a.download = filename; document.body.appendChild(a); a.click();
                        document.body.removeChild(a); URL.revokeObjectURL(url);
                        toast('Archivo descargado.', 'success');
                    })
                    .catch(function (e) { toast('No se pudo exportar: ' + e.message, 'error'); })
                    .then(function () { exporting.value = false; });
            }

            // Sólo para mostrar: fechas y decimales como en el resto de la app (dd/mm/aaaa, coma).
            // El export conserva el formato del servidor.
            function formatCell(v) {
                if (typeof v === 'number') v = String(v);
                if (typeof v !== 'string') return v;
                var d = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v);
                if (d) return d[3] + '/' + d[2] + '/' + d[1];
                if (/^-?\d+\.\d+$/.test(v)) return v.replace('.', ',');
                return v;
            }

            return {
                formatCell: formatCell,
                questionnaires: questionnaires, groupedQuestionnaires: groupedQuestionnaires, selectedId: selectedId,
                qSearch: qSearch, qOpen: qOpen, qBox: qBox, selectedTitle: selectedTitle,
                filteredGroups: filteredGroups, pickQuestionnaire: pickQuestionnaire,
                questions: questions, filters: filters, combineMode: combineMode,
                dateFrom: dateFrom, dateTo: dateTo, onRangeChange: onRangeChange,
                rows: rows, columns: columns, total: total,
                page: page, totalPages: totalPages,
                loading: loading, exporting: exporting, error: error,
                stats: stats,
                currentReportId: currentReportId, reportName: reportName,
                saving: saving, showSave: showSave, openSave: openSave, saveReport: saveReport,
                dirty: dirty,
                addFilter: addFilter, addGroup: addGroup, addToGroup: addToGroup, removeFilter: removeFilter,
                preview: preview,
                exportXlsx: function () { exportFile('xlsx', exportFilename('xlsx')); },
                exportCsv: function () { exportFile('csv', exportFilename('csv')); }
            };
        },
        template: '\
<div class="cq-card">\
    <div class="row g-3 mb-3">\
        <div class="col-md-6">\
            <label class="form-label">Cuestionario base</label>\
            <div class="cq-combo" ref="qBox">\
                <input type="text" class="form-control cq-combo__input" v-model="qSearch"\
                       :placeholder="selectedTitle || \'Buscar cuestionario…\'"\
                       @focus="qOpen = true" @keydown.esc="qOpen = false" autocomplete="off" />\
                <i class="bi bi-chevron-down cq-combo__caret" aria-hidden="true"></i>\
                <div v-if="qOpen" class="cq-combo__pop">\
                    <template v-for="g in filteredGroups" :key="g.label">\
                        <div class="cq-combo__group">{{ g.label }}</div>\
                        <button v-for="q in g.items" :key="q.id" type="button"\
                                class="cq-combo__opt" :class="{ \'is-active\': q.id === selectedId }"\
                                @click="pickQuestionnaire(q)">{{ q.title }}</button>\
                    </template>\
                    <div v-if="!filteredGroups.length" class="cq-combo__empty">Sin resultados</div>\
                </div>\
            </div>\
        </div>\
        <div class="col-md-6">\
            <label class="form-label">Período</label>\
            <cq-date-range :value-from="dateFrom" :value-to="dateTo" @range-change="onRangeChange"></cq-date-range>\
        </div>\
    </div>\
\
    <h3 class="cq-section-title">Mostrar solo las fichas que:</h3>\
    <template v-for="(f, i) in filters" :key="i">\
        <div v-if="f.group" class="border rounded p-2 mb-2">\
            <div class="d-flex align-items-center gap-3 cq-text-sm mb-2">\
                <span class="cq-text-muted">Grupo: las fichas deben</span>\
                <label><input type="radio" v-model="f.logic" value="AND" /> cumplir todas</label>\
                <label><input type="radio" v-model="f.logic" value="OR" /> cumplir al menos una</label>\
                <button type="button" class="cq-icon-btn cq-icon-btn--danger ms-auto" @click="removeFilter(i)" aria-label="Eliminar grupo" title="Eliminar grupo"><i class="bi bi-trash cq-icon-btn__idle"></i><i class="bi bi-trash-fill cq-icon-btn__hover"></i></button>\
            </div>\
            <cq-filter-row v-for="(g, j) in f.items" :key="j" :f="g" :questions="questions" @remove="f.items.splice(j, 1)"></cq-filter-row>\
            <button class="btn btn-sm btn-secondary" @click="addToGroup(f)"><i class="bi bi-plus-lg me-1"></i> Agregar condición al grupo</button>\
        </div>\
        <cq-filter-row v-else :f="f" :questions="questions" @remove="removeFilter(i)"></cq-filter-row>\
    </template>\
    <div class="d-flex align-items-center gap-3 mb-3">\
        <button class="btn btn-sm btn-primary" @click="addFilter"><i class="bi bi-plus-lg me-1"></i> Agregar condición</button>\
        <button class="btn btn-sm btn-secondary" @click="addGroup"><i class="bi bi-collection me-1"></i> Agregar grupo</button>\
        <div v-if="filters.length > 1" class="d-inline-flex gap-3 cq-text-sm">\
            <span class="cq-text-muted">Las fichas deben:</span>\
            <label><input type="radio" v-model="combineMode" value="AND" /> cumplir todas</label>\
            <label><input type="radio" v-model="combineMode" value="OR" /> cumplir al menos una</label>\
        </div>\
    </div>\
\
    <div class="cq-text-sm cq-text-muted border-top pt-3">\
        <span v-if="loading"><span class="cq-spinner cq-spinner--sm me-1"></span> Actualizando…</span>\
        <span v-else-if="total === null">Seleccione un cuestionario para ver los resultados.</span>\
    </div>\
\
    <div v-if="showSave" class="modal-backdrop fade show" style="z-index:1050;"></div>\
    <div v-if="showSave" class="modal fade show d-block" tabindex="-1" role="dialog" style="z-index:1055;" @click.self="showSave = false">\
        <div class="modal-dialog modal-dialog-centered" role="document" @click.stop>\
            <div class="modal-content">\
                <div class="modal-header">\
                    <h3 class="modal-title h5 m-0">{{ currentReportId ? \'Guardar cambios\' : \'Guardar reporte\' }}</h3>\
                    <button type="button" class="btn-close" aria-label="Cerrar" @click="showSave = false"></button>\
                </div>\
                <div class="modal-body">\
                    <label class="form-label cq-text-sm">Nombre del reporte</label>\
                    <input v-model="reportName" class="form-control" placeholder="Ej: AIJ oligoarticular 2026" @keyup.enter="saveReport" autofocus />\
                </div>\
                <div class="modal-footer">\
                    <button type="button" class="btn btn-outline-danger" @click="showSave = false">Cancelar</button>\
                    <button type="button" class="btn btn-primary" @click="saveReport" :disabled="saving">{{ saving ? \'Guardando…\' : \'Guardar\' }}</button>\
                </div>\
            </div>\
        </div>\
    </div>\
\
    <div v-if="error" class="alert alert-danger mt-3 mb-0">{{ error }}</div>\
\
    <div v-if="rows.length" class="table-responsive mt-3" style="max-height:420px; overflow:auto;">\
        <table class="cq-table">\
            <thead><tr><th v-for="c in columns" :key="c">{{ c }}</th></tr></thead>\
            <tbody>\
                <tr v-for="(row, i) in rows" :key="i">\
                    <td v-for="c in columns" :key="c">{{ formatCell(row[c]) }}</td>\
                </tr>\
            </tbody>\
        </table>\
        <div class="d-flex justify-content-between align-items-center flex-wrap gap-2 mt-2">\
            <span class="cq-text-xs cq-text-muted">Mostrando {{ rows.length }} de {{ total }} fichas.</span>\
            <div v-if="totalPages > 1" class="btn-group btn-group-sm">\
                <button class="btn btn-outline-secondary" :disabled="page <= 1 || loading" @click="preview(page - 1)"><i class="bi bi-chevron-left"></i> Anterior</button>\
                <button class="btn btn-outline-secondary" :disabled="page >= totalPages || loading" @click="preview(page + 1)">Siguiente <i class="bi bi-chevron-right"></i></button>\
            </div>\
        </div>\
    </div>\
    <div v-else-if="!loading && total !== null" class="cq-card mt-3">\
        <div class="cq-empty">\
            <div class="cq-empty__icon"><i class="bi bi-clipboard-x"></i></div>\
            <div class="cq-empty__title">No hay fichas</div>\
        </div>\
    </div>\
\
    <div class="d-flex justify-content-end gap-2 mt-3">\
        <button class="btn btn-secondary" @click="openSave"\
                :disabled="!!currentReportId && !dirty"\
                :title="(!!currentReportId && !dirty) ? \'No hay cambios para guardar\' : \'\'">\
            <i class="bi" :class="(!!currentReportId && !dirty) ? \'bi-check2\' : \'bi-bookmark\'"></i>\
            {{ currentReportId ? (dirty ? \'Guardar cambios\' : \'Guardado\') : \'Guardar reporte\' }}\
        </button>\
        <div class="dropdown">\
            <button class="btn btn-primary dropdown-toggle" type="button" data-bs-toggle="dropdown" aria-expanded="false" :disabled="exporting || !selectedId">\
                <i class="bi bi-download me-1"></i> {{ exporting ? \'Exportando…\' : \'Exportar\' }}\
            </button>\
            <ul class="dropdown-menu dropdown-menu-end">\
                <li><button class="dropdown-item" type="button" @click="exportXlsx"><i class="bi bi-file-earmark-excel me-1"></i> Excel (.xlsx)</button></li>\
                <li><button class="dropdown-item" type="button" @click="exportCsv"><i class="bi bi-filetype-csv me-1"></i> CSV (.csv)</button></li>\
            </ul>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('report-builder', ReportBuilder);
})();
