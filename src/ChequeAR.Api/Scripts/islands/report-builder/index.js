/*
 * Isla Vue: report-builder (pantalla E2)
 * Cohorte sobre un cuestionario base + filtros por pregunta + rango de fechas.
 * Previsualiza contra POST /api/reports/query y exporta a Excel/CSV (POST /api/reports/export).
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
            var loading = Vue.ref(false);
            var exporting = Vue.ref(false);
            var error = Vue.ref(null);

            function toast(m, k) { if (window.cqToast) window.cqToast(m, k); }

            function opsFor(type) {
                if (type === 'integer' || type === 'decimal')
                    return [{ v: '=', l: '=' }, { v: '!=', l: '≠' }, { v: '>', l: '>' }, { v: '>=', l: '≥' }, { v: '<', l: '<' }, { v: '<=', l: '≤' }];
                if (type === 'choice')
                    return [{ v: '=', l: 'es' }, { v: '!=', l: 'no es' }];
                if (type === 'boolean')
                    return [{ v: '=', l: 'es' }];
                if (type === 'date')
                    return [{ v: '=', l: '=' }, { v: '>=', l: 'desde' }, { v: '<=', l: 'hasta' }];
                return [{ v: '=', l: 'es' }, { v: '!=', l: 'no es' }, { v: 'contains', l: 'contiene' }];
            }
            function questionByLink(linkId) {
                return questions.value.find(function (q) { return q.linkId === linkId; });
            }

            function loadQuestions(id) {
                questions.value = []; filters.value = [];
                if (!id) return;
                fetch('/fhir/r5/Questionnaire/' + id, { headers: authHeaders() })
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

            Vue.onMounted(function () {
                fetch('/fhir/r5/Questionnaire?_count=100', { headers: authHeaders() })
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        var list = (data && data.entry) ? data.entry : [];
                        questionnaires.value = list.map(function (q) { return { id: q.Id || q.id, title: q.Title || q.title }; });
                        if (questionnaires.value.length) { selectedId.value = questionnaires.value[0].id; }
                    })
                    .catch(function () { error.value = 'No se pudieron cargar los cuestionarios.'; });
            });

            Vue.watch(selectedId, function (id) { loadQuestions(id); });

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

            function preview() {
                if (!selectedId.value) { toast('Elegí un cuestionario', 'error'); return; }
                loading.value = true; error.value = null;
                fetch('/api/reports/query', { method: 'POST', headers: authHeaders(), body: JSON.stringify(buildQuery()) })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (data) {
                        total.value = data.total;
                        rows.value = (data.rows || []).slice(0, 100);
                        columns.value = rows.value.length ? Object.keys(rows.value[0]) : [];
                    })
                    .catch(function (e) { error.value = 'Error al previsualizar: ' + e.message; })
                    .then(function () { loading.value = false; });
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
                loading: loading, exporting: exporting, error: error,
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
    <h3 class="cq-section-title">Filtros por pregunta</h3>\
    <div v-if="filters.length === 0" class="cq-text-sm cq-text-muted mb-2">Sin filtros — la cohorte incluye todas las respuestas del período.</div>\
    <div v-for="(f, i) in filters" :key="i" class="row g-2 align-items-center mb-2">\
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
        <div class="col-md-4">\
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
        <button class="btn btn-sm btn-outline-secondary" @click="addFilter"><i class="bi bi-plus-lg me-1"></i> Agregar filtro</button>\
        <div v-if="filters.length > 1" class="d-inline-flex gap-3 cq-text-sm">\
            <span class="cq-text-muted">Combinación:</span>\
            <label><input type="radio" v-model="combineMode" value="AND" /> Y todas</label>\
            <label><input type="radio" v-model="combineMode" value="OR" /> O cualquiera</label>\
        </div>\
    </div>\
\
    <div class="d-flex justify-content-between align-items-center border-top pt-3">\
        <div class="cq-text-sm cq-text-muted">\
            <span v-if="total !== null">Resultado: <strong>{{ total }}</strong> respuestas</span>\
            <span v-else>Previsualizá para ver los resultados.</span>\
        </div>\
        <div class="d-flex gap-2">\
            <button class="btn btn-outline-secondary" @click="preview" :disabled="loading"><i class="bi bi-eye me-1"></i> {{ loading ? \'Cargando…\' : \'Previsualizar\' }}</button>\
            <button class="btn btn-outline-success" @click="exportCsv" :disabled="exporting"><i class="bi bi-filetype-csv me-1"></i> CSV</button>\
            <button class="btn btn-primary" @click="exportXlsx" :disabled="exporting"><i class="bi bi-file-earmark-excel me-1"></i> {{ exporting ? \'Exportando…\' : \'Exportar a Excel\' }}</button>\
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
        <p class="cq-text-xs cq-text-muted">Mostrando hasta 100 filas. La exportación incluye todas.</p>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('report-builder', ReportBuilder);
})();
