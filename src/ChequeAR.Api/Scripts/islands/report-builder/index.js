/*
 * Isla Vue: report-builder (pantalla E2)
 * Reporte sobre un cuestionario base + condiciones por pregunta + rango de fechas.
 * El grupo de fichas resultante se previsualiza contra POST /api/reports/query y se
 * exporta a Excel/CSV (POST /api/reports/export).
 */
(function () {
    function token() {
        var m = document.querySelector('meta[name="cq-token"]');
        return m ? m.getAttribute('content') : null;
    }
    function authHeaders() {
        var h = { 'Content-Type': 'application/json' };
        var t = token();
        if (t) h['Authorization'] = 'Bearer ' + t;
        return h;
    }
    // QuestionType (enum del backend, serializado como número) -> categoría de filtro.
    function typeCategory(n) {
        switch (n) {
            case 0: return 'decimal';
            case 1: return 'integer';
            case 4: case 5: return 'date';
            case 6: return 'boolean';
            case 7: case 8: return 'choice';
            default: return 'string';
        }
    }

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
            function loadQuestions(id) {
                questions.value = [];
                if (!id) return Promise.resolve();
                return fetch('/fhir/r5/Questionnaire/' + id, { headers: authHeaders() })
                    .then(function (r) { return r.json(); })
                    .then(function (q) {
                        var raw = q.Questions || q.questions || [];
                        questions.value = raw.map(function (x) {
                            var opts = (x.Options || x.options || []).map(function (o) {
                                return { value: (o.Value != null ? o.Value : o.value), display: (o.Display != null ? o.Display : o.display) };
                            });
                            return {
                                linkId: x.LinkId || x.linkId,
                                text: x.Text || x.text,
                                type: typeCategory(x.Type != null ? x.Type : x.type),
                                options: opts
                            };
                        });
                    })
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
                        });
                    })
                    .catch(function () { error.value = 'No se pudo cargar el reporte guardado.'; rehydrating = false; });
            }

            Vue.onMounted(function () {
                fetch('/fhir/r5/Questionnaire?_count=100', { headers: authHeaders() })
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        var list = (data && data.entry) ? data.entry : [];
                        questionnaires.value = list.map(function (q) { return { id: q.Id || q.id, title: q.Title || q.title }; });
                        // Editar una consulta guardada manda su cuestionario; en ese caso no
                        // se pisa con el primero de la lista.
                        if (currentReportId.value) loadSavedReport(currentReportId.value);
                        else if (questionnaires.value.length) selectedId.value = questionnaires.value[0].id;
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
                if (!selectedId.value) { toast('Elegí un cuestionario', 'error'); return; }
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
                if (!selectedId.value) { toast('Elegí un cuestionario', 'error'); return; }
                window.dispatchEvent(new CustomEvent('cq:report-query', { detail: buildQuery() }));
            }

            function openSave() {
                if (!selectedId.value) { toast('Elegí un cuestionario', 'error'); return; }
                showSave.value = true;
            }

            // Guarda la consulta (definicion, no resultados): el cuestionario base y los
            // filtros/fechas actuales, para volver a correrla despues. Es POST la primera
            // vez y PUT sobre la misma si ya existia. El backend solo deja actualizar al
            // dueño (403 si no).
            function saveReport() {
                if (!selectedId.value) { toast('Elegí un cuestionario', 'error'); return; }
                var name = (reportName.value || '').trim();
                if (!name) { toast('Poné un nombre para el reporte', 'error'); return; }
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
                        toast(editing ? 'Reporte actualizado' : 'Reporte guardado', 'success');
                    })
                    .catch(function (e) { toast('No se pudo guardar: ' + e.message, 'error'); })
                    .then(function () { saving.value = false; });
            }

            function exportFile(format, filename) {
                if (!selectedId.value) { toast('Elegí un cuestionario', 'error'); return; }
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
                questionnaires: questionnaires, selectedId: selectedId,
                questions: questions, filters: filters, combineMode: combineMode,
                dateFrom: dateFrom, dateTo: dateTo,
                rows: rows, columns: columns, total: total,
                page: page, totalPages: totalPages,
                loading: loading, exporting: exporting, error: error,
                stats: stats,
                currentReportId: currentReportId, reportName: reportName,
                saving: saving, showSave: showSave, openSave: openSave, saveReport: saveReport,
                opsFor: opsFor, questionByLink: questionByLink,
                addFilter: addFilter, removeFilter: removeFilter, onQuestionChange: onQuestionChange,
                preview: preview,
                exportXlsx: function () { exportFile('xlsx', 'reporte.xlsx'); },
                exportCsv: function () { exportFile('csv', 'reporte.csv'); }
            };
        },
        template: '\
<div class="cq-card">\
    <div class="row g-3 mb-3">\
        <div class="col-md-6">\
            <label class="form-label">Cuestionario base</label>\
            <select v-model="selectedId" class="form-select">\
                <option v-for="q in questionnaires" :key="q.id" :value="q.id">{{ q.title }}</option>\
            </select>\
        </div>\
        <div class="col-md-3">\
            <label class="form-label">Desde</label>\
            <input v-model="dateFrom" type="date" class="form-control" />\
        </div>\
        <div class="col-md-3">\
            <label class="form-label">Hasta</label>\
            <input v-model="dateTo" type="date" class="form-control" />\
        </div>\
    </div>\
\
    <h3 class="cq-section-title">Mostrar solo las fichas que:</h3>\
    <div v-if="filters.length === 0" class="cq-text-sm cq-text-muted mb-2">Sin condiciones — el reporte incluye todas las fichas del período.</div>\
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
        <button class="btn btn-sm btn-outline-secondary" @click="addFilter"><i class="bi bi-plus-lg me-1"></i> Agregar condición</button>\
        <div v-if="filters.length > 1" class="d-inline-flex gap-3 cq-text-sm">\
            <span class="cq-text-muted">Las fichas deben:</span>\
            <label><input type="radio" v-model="combineMode" value="AND" /> cumplir todas</label>\
            <label><input type="radio" v-model="combineMode" value="OR" /> cumplir al menos una</label>\
        </div>\
    </div>\
\
    <div class="d-flex justify-content-between align-items-center border-top pt-3">\
        <div class="cq-text-sm cq-text-muted">\
            <span v-if="total !== null">Grupo de fichas: <strong>{{ total }}</strong> fichas</span>\
            <span v-else>Previsualizá para ver los resultados.</span>\
        </div>\
        <div class="d-flex gap-2">\
            <button class="btn btn-outline-secondary" @click="openSave"><i class="bi bi-bookmark me-1"></i> {{ currentReportId ? \'Guardar cambios\' : \'Guardar reporte\' }}</button>\
            <button class="btn btn-outline-secondary" @click="preview" :disabled="loading"><i class="bi bi-eye me-1"></i> {{ loading ? \'Cargando…\' : \'Previsualizar\' }}</button>\
            <button class="btn btn-outline-secondary" @click="stats"><i class="bi bi-graph-up me-1"></i> Estadísticas</button>\
            <button class="btn btn-outline-success" @click="exportCsv" :disabled="exporting"><i class="bi bi-filetype-csv me-1"></i> CSV</button>\
            <button class="btn btn-primary" @click="exportXlsx" :disabled="exporting"><i class="bi bi-file-earmark-excel me-1"></i> {{ exporting ? \'Exportando…\' : \'Exportar a Excel\' }}</button>\
        </div>\
    </div>\
\
    <div v-if="showSave" class="cq-card mt-3 mb-0" style="border-left:3px solid var(--cq-primary);">\
        <label class="form-label cq-text-sm">Nombre del reporte</label>\
        <div class="d-flex gap-2">\
            <input v-model="reportName" class="form-control form-control-sm" style="max-width:360px;" placeholder="Ej: AIJ oligoarticular 2026" @keyup.enter="saveReport" />\
            <button class="btn btn-sm btn-primary" @click="saveReport" :disabled="saving">{{ saving ? \'Guardando…\' : \'Guardar\' }}</button>\
            <button class="btn btn-sm btn-link" @click="showSave = false">Cancelar</button>\
        </div>\
        <p class="cq-text-xs cq-text-muted mt-1 mb-0">El reporte guarda la definición (cuestionario y condiciones), no los resultados. Lo podés volver a correr desde el listado de reportes.</p>\
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
            <span class="cq-text-xs cq-text-muted">Mostrando {{ rows.length }} de {{ total }} fichas · página {{ page }} de {{ totalPages }}. La exportación incluye todas.</span>\
            <div v-if="totalPages > 1" class="btn-group btn-group-sm">\
                <button class="btn btn-outline-secondary" :disabled="page <= 1 || loading" @click="preview(page - 1)"><i class="bi bi-chevron-left"></i> Anterior</button>\
                <button class="btn btn-outline-secondary" :disabled="page >= totalPages || loading" @click="preview(page + 1)">Siguiente <i class="bi bi-chevron-right"></i></button>\
            </div>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('report-builder', ReportBuilder);
})();
