/*
 * Isla Vue: report-builder (pantalla E2)
 * Construye una cohorte sobre un cuestionario base, previsualiza contra la API real
 * (POST /api/reports/query) y exporta a Excel/CSV (POST /api/reports/export).
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

    var ReportBuilder = {
        props: { reportId: { type: String, default: null } },
        setup: function (props) {
            var questionnaires = Vue.ref([]);
            var selectedId = Vue.ref(null);
            var dateFrom = Vue.ref('');
            var dateTo = Vue.ref('');
            var rows = Vue.ref([]);
            var columns = Vue.ref([]);
            var total = Vue.ref(null);
            var loading = Vue.ref(false);
            var exporting = Vue.ref(false);
            var error = Vue.ref(null);

            function toast(m, k) { if (window.cqToast) window.cqToast(m, k); }

            Vue.onMounted(function () {
                fetch('/fhir/r5/Questionnaire?_count=100', { headers: authHeaders() })
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        var list = (data && data.entry) ? data.entry : [];
                        // sólo publicados (status 1 = Active) tienen respuestas para reportar
                        questionnaires.value = list.map(function (q) {
                            return { id: q.Id || q.id, title: q.Title || q.title, status: q.Status };
                        });
                        if (questionnaires.value.length) selectedId.value = questionnaires.value[0].id;
                    })
                    .catch(function () { error.value = 'No se pudieron cargar los cuestionarios.'; });
            });

            function buildQuery(format) {
                var q = { questionnaireId: selectedId.value };
                if (dateFrom.value || dateTo.value) {
                    q.dateRange = {
                        from: dateFrom.value ? dateFrom.value + 'T00:00:00' : null,
                        to: dateTo.value ? dateTo.value + 'T23:59:59' : null
                    };
                }
                if (format) q.format = format;
                return q;
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
                dateFrom: dateFrom, dateTo: dateTo,
                rows: rows, columns: columns, total: total,
                loading: loading, exporting: exporting, error: error,
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
    <div class="d-flex justify-content-between align-items-center border-top pt-3">\
        <div class="cq-text-sm cq-text-muted">\
            <span v-if="total !== null">Resultado: <strong>{{ total }}</strong> filas</span>\
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
