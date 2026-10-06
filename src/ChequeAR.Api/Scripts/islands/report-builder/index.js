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

    var ReportBuilder = {
        props: { reportId: { type: String, default: null } },
        setup: function (props) {
            var questionnaires = Vue.ref([]);
            var selectedId = Vue.ref(null);
            var questions = Vue.ref([]);      // preguntas del cuestionario elegido
            var filters = Vue.ref([]);        // [{ linkId, op, value }]
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

            // Etiquetas en palabras para que cada condición se lea como una frase:
            // "PCR sea mayor a 5", "Tratamiento actual sea Corticoides".
            function opsFor(type) {
                if (type === 'integer' || type === 'decimal')
                    return [{ v: '=', l: 'sea igual a' }, { v: '!=', l: 'sea distinto de' }, { v: '>', l: 'sea mayor a' }, { v: '>=', l: 'sea mayor o igual a' }, { v: '<', l: 'sea menor a' }, { v: '<=', l: 'sea menor o igual a' }];
                if (type === 'choice')
                    return [{ v: '=', l: 'sea' }, { v: '!=', l: 'no sea' }];
                if (type === 'boolean')
                    return [{ v: '=', l: 'sea' }];
                if (type === 'date')
                    return [{ v: '=', l: 'sea del' }, { v: '>=', l: 'sea desde' }, { v: '<=', l: 'sea hasta' }];
                return [{ v: '=', l: 'sea' }, { v: '!=', l: 'no sea' }, { v: 'contains', l: 'contenga' }];
            }
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
                        options: (it.answerOption || []).map(function (o) {
                            var c = o.valueCoding || {};
                            return { value: c.code, display: c.display || c.code };
                        })
                    });
                });
                return out;
            }

            function loadQuestions(id) {
                questions.value = [];
                if (!id) return Promise.resolve();
                return fetch('/fhir/r5/Questionnaire/' + id, { headers: authHeaders() })
                    .then(function (r) { return r.json(); })
                    .then(function (q) { questions.value = flattenItems(q.item, []); })
                    .catch(function () { /* sin preguntas: se puede filtrar igual por fecha */ });
            }

            // Rehidrata el editor desde una consulta guardada (flujo "Editar consulta").
            function loadSavedReport(id) {
                fetch('/api/reports/saved/' + id, { headers: authHeaders() })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
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
                                filters.value = (query.filters.predicates || []).map(function (p) {
                                    var q = questionByLink(p.linkId);
                                    // El select de un booleano espera el string 'true'/'false';
                                    // guardado viene como bool.
                                    var val = (q && q.type === 'boolean') ? String(p.value) : p.value;
                                    return { linkId: p.linkId, op: p.op, value: val };
                                });
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
            function pickQuestionnaire(q) { selectedId.value = q.id; qSearch.value = ''; qOpen.value = false; }

            Vue.onMounted(function () {
                // Cerrar el combobox al hacer clic fuera.
                document.addEventListener('click', function (e) {
                    if (qOpen.value && qBox.value && !qBox.value.contains(e.target)) qOpen.value = false;
                });
                fetch('/fhir/r5/Questionnaire?_count=100', { headers: authHeaders() })
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        // Bundle conforme: cada resultado viene envuelto en entry[].resource.
                        var list = (data && data.entry) ? data.entry : [];
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
                if (!questions.value.length) { toast('Este cuestionario no tiene preguntas para filtrar', 'info'); return; }
                var q = questions.value[0];
                filters.value.push({ linkId: q.linkId, op: opsFor(q.type)[0].v, value: q.type === 'boolean' ? 'true' : '' });
            }
            function removeFilter(i) { filters.value.splice(i, 1); }
            function onQuestionChange(f) {
                var q = questionByLink(f.linkId);
                f.op = opsFor(q.type)[0].v;
                f.value = q.type === 'boolean' ? 'true' : '';
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
                var preds = filters.value
                    .filter(function (f) { var q = questionByLink(f.linkId); return q && f.op && (q.type === 'boolean' || (f.value !== '' && f.value != null)); })
                    .map(function (f) { return { linkId: f.linkId, op: f.op, value: coerce(questionByLink(f.linkId), f.value) }; });
                if (preds.length) query.filters = { logic: combineMode.value, predicates: preds };
                if (format) query.format = format;
                return query;
            }

            // El boton "Previsualizar" pasa el Event como argumento (no un numero): en ese
            // caso arranca en la pagina 1. Anterior/Siguiente pasan el numero de pagina.
            function preview(goToPage) {
                if (!selectedId.value) { toast('Seleccione un cuestionario', 'error'); return; }
                var p = (typeof goToPage === 'number' && goToPage > 0) ? goToPage : 1;
                loading.value = true; error.value = null;
                var q = buildQuery();
                q.page = p;
                q.pageSize = PAGE_SIZE;
                fetch('/api/reports/query', { method: 'POST', headers: authHeaders(), body: JSON.stringify(q) })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
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
                if (!selectedId.value) { toast('Seleccione un cuestionario', 'error'); return; }
                window.dispatchEvent(new CustomEvent('cq:report-query', { detail: buildQuery() }));
            }

            function openSave() {
                if (!selectedId.value) { toast('Seleccione un cuestionario', 'error'); return; }
                showSave.value = true;
            }

            // Guarda la consulta (definicion, no resultados): el cuestionario base y los
            // filtros/fechas actuales, para volver a correrla despues. Es POST la primera
            // vez y PUT sobre la misma si ya existia. El backend solo deja actualizar al
            // dueño (403 si no).
            function saveReport() {
                if (!selectedId.value) { toast('Seleccione un cuestionario', 'error'); return; }
                var name = (reportName.value || '').trim();
                if (!name) { toast('Ingrese un nombre para el reporte', 'error'); return; }
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
                    .then(function (r) {
                        if (!r.ok) throw new Error('HTTP ' + r.status);
                        return r.json().catch(function () { return null; });
                    })
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
                        toast(editing ? 'Reporte actualizado' : 'Reporte guardado', 'success');
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
                if (!selectedId.value) { toast('Seleccione un cuestionario', 'error'); return; }
                exporting.value = true;
                fetch('/api/reports/export', { method: 'POST', headers: authHeaders(), body: JSON.stringify(buildQuery(format)) })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.blob(); })
                    .then(function (blob) {
                        var url = URL.createObjectURL(blob);
                        var a = document.createElement('a');
                        a.href = url; a.download = filename; document.body.appendChild(a); a.click();
                        document.body.removeChild(a); URL.revokeObjectURL(url);
                        toast('Exportación lista', 'success');
                    })
                    .catch(function (e) { toast('Error al exportar: ' + e.message, 'error'); })
                    .then(function () { exporting.value = false; });
            }

            return {
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
                opsFor: opsFor, questionByLink: questionByLink,
                addFilter: addFilter, removeFilter: removeFilter, onQuestionChange: onQuestionChange,
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
    <div v-for="(f, i) in filters" :key="i" class="row g-2 align-items-center mb-2">\
        <div class="col-auto cq-text-muted pe-0" style="width:1.2rem;">•</div>\
        <div class="col-md-4">\
            <select v-model="f.linkId" @change="onQuestionChange(f)" class="form-select form-select-sm">\
                <option v-for="q in questions" :key="q.linkId" :value="q.linkId">{{ q.text }}</option>\
            </select>\
        </div>\
        <div class="col-md-3">\
            <select v-model="f.op" class="form-select form-select-sm">\
                <option v-for="o in opsFor(questionByLink(f.linkId).type)" :key="o.v" :value="o.v">{{ o.l }}</option>\
            </select>\
        </div>\
        <div class="col-md-3">\
            <select v-if="questionByLink(f.linkId).type === \'boolean\'" v-model="f.value" class="form-select form-select-sm">\
                <option value="true">Sí</option><option value="false">No</option>\
            </select>\
            <select v-else-if="questionByLink(f.linkId).type === \'choice\'" v-model="f.value" class="form-select form-select-sm">\
                <option value="">—</option>\
                <option v-for="o in questionByLink(f.linkId).options" :key="o.value" :value="o.value">{{ o.display }}</option>\
            </select>\
            <input v-else v-model="f.value" class="form-control form-control-sm"\
                   :type="questionByLink(f.linkId).type === \'date\' ? \'date\' : (questionByLink(f.linkId).type === \'integer\' || questionByLink(f.linkId).type === \'decimal\' ? \'number\' : \'text\')" />\
        </div>\
        <div class="col-md-1 text-end">\
            <button class="btn btn-sm btn-link text-danger" @click="removeFilter(i)" aria-label="Eliminar filtro"><i class="bi bi-x-lg"></i></button>\
        </div>\
    </div>\
    <div class="d-flex align-items-center gap-3 mb-3">\
        <button class="btn btn-sm btn-primary" @click="addFilter"><i class="bi bi-plus-lg me-1"></i> Agregar condición</button>\
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
        <table class="table table-sm table-striped">\
            <thead><tr><th v-for="c in columns" :key="c">{{ c }}</th></tr></thead>\
            <tbody>\
                <tr v-for="(row, i) in rows" :key="i">\
                    <td v-for="c in columns" :key="c">{{ row[c] }}</td>\
                </tr>\
            </tbody>\
        </table>\
        <div class="d-flex justify-content-between align-items-center flex-wrap gap-2">\
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
            <div class="cq-empty__desc">Pruebe ampliando el rango de fechas o sacando condiciones.</div>\
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
