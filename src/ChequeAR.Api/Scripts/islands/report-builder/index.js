/*
 * Isla Vue: report-builder (pantalla E2)
 * Query builder para cohortes: cuestionario base + filtros por pregunta + columnas.
 */
(function () {
    var ReportBuilder = {
        props: { reportId: { type: Number, default: null } },
        setup: function (props) {
            var questionnaires = Vue.ref([
                { id: 1, title: 'Cardio basal',  questions: [
                    { linkId: 'edad', text: 'Edad', type: 'integer' },
                    { linkId: 'sexo', text: 'Sexo', type: 'choice', options: ['F','M','O'] },
                    { linkId: 'tasis', text: 'TA sistólica', type: 'decimal' },
                    { linkId: 'hta', text: '¿HTA?', type: 'boolean' }
                ] },
                { id: 2, title: 'Neumo control', questions: [
                    { linkId: 'edad', text: 'Edad', type: 'integer' },
                    { linkId: 'fev1', text: 'FEV1', type: 'decimal' },
                    { linkId: 'asma', text: '¿Asma?', type: 'boolean' }
                ] }
            ]);
            var selectedQ = Vue.ref(questionnaires.value[0]);
            var dateFrom = Vue.ref('2026-01-01');
            var dateTo = Vue.ref('2026-06-10');
            var specialty = Vue.ref('cardio');
            var combineMode = Vue.ref('AND');
            var filters = Vue.ref([]);
            var columns = Vue.ref({ HC: true, Edad: true });
            var estimate = Vue.ref(142);

            function opsFor(type) {
                if (type === 'integer' || type === 'decimal') return ['=', '≠', '<', '≤', '>', '≥', 'entre', 'vacío', 'no_vacío'];
                if (type === 'string') return ['=', '≠', 'contiene', 'empieza_con', 'vacío'];
                if (type === 'boolean') return ['es'];
                if (type === 'date') return ['=', '≠', '<', '≤', '>', '≥', 'entre', 'últimos_N_días', 'vacío'];
                if (type === 'choice') return ['es', 'es_alguno_de', 'no_es', 'vacío'];
                return ['='];
            }
            function addFilter() {
                var first = selectedQ.value.questions[0];
                filters.value.push({ q: first.linkId, op: opsFor(first.type)[0], val: '' });
                recalc();
            }
            function removeFilter(i) { filters.value.splice(i, 1); recalc(); }
            function getQ(linkId) { return selectedQ.value.questions.find(function (x) { return x.linkId === linkId; }); }
            function recalc() {
                // Mock: cada filtro reduce ~15%
                estimate.value = Math.max(0, Math.round(142 * Math.pow(0.85, filters.value.length)));
            }
            Vue.watch(filters, recalc, { deep: true });

            function save() {
                window.cqToast && window.cqToast('Consulta guardada', 'success');
            }
            function preview() {
                location.href = '/Reports/Preview?id=1';
            }

            return {
                questionnaires: questionnaires, selectedQ: selectedQ,
                dateFrom: dateFrom, dateTo: dateTo, specialty: specialty,
                combineMode: combineMode, filters: filters, columns: columns,
                estimate: estimate, opsFor: opsFor, addFilter: addFilter,
                removeFilter: removeFilter, getQ: getQ, save: save, preview: preview
            };
        },
        template: '\
<div class="cq-card">\
    <div class="row g-3 mb-3">\
        <div class="col-md-5">\
            <label class="form-label">Cuestionario base</label>\
            <select v-model="selectedQ" class="form-select">\
                <option v-for="q in questionnaires" :key="q.id" :value="q">{{ q.title }}</option>\
            </select>\
        </div>\
        <div class="col-md-3">\
            <label class="form-label">Especialidad</label>\
            <select v-model="specialty" class="form-select">\
                <option value="cardio">Cardiología</option>\
                <option value="neumo">Neumonología</option>\
                <option value="onco">Oncología</option>\
                <option value="general">General</option>\
            </select>\
        </div>\
        <div class="col-md-2">\
            <label class="form-label">Desde</label>\
            <input v-model="dateFrom" type="date" class="form-control" />\
        </div>\
        <div class="col-md-2">\
            <label class="form-label">Hasta</label>\
            <input v-model="dateTo" type="date" class="form-control" />\
        </div>\
    </div>\
\
    <h3>Filtros sobre preguntas</h3>\
    <div v-if="filters.length === 0" class="cq-text-sm cq-text-muted mb-3">Sin filtros — la cohorte incluye todos los registros del período.</div>\
    <div v-for="(f, i) in filters" :key="i" class="row g-2 align-items-center mb-2">\
        <div class="col-md-4">\
            <select v-model="f.q" @change="f.op = opsFor(getQ(f.q).type)[0]" class="form-select">\
                <option v-for="qq in selectedQ.questions" :key="qq.linkId" :value="qq.linkId">{{ qq.text }}</option>\
            </select>\
        </div>\
        <div class="col-md-3">\
            <select v-model="f.op" class="form-select">\
                <option v-for="o in opsFor(getQ(f.q).type)" :key="o" :value="o">{{ o }}</option>\
            </select>\
        </div>\
        <div class="col-md-4">\
            <input v-if="getQ(f.q).type === \'boolean\'" v-model="f.val" placeholder="true / false" class="form-control" />\
            <select v-else-if="getQ(f.q).type === \'choice\'" v-model="f.val" class="form-select">\
                <option v-for="opt in getQ(f.q).options" :key="opt" :value="opt">{{ opt }}</option>\
            </select>\
            <input v-else v-model="f.val" class="form-control" :type="getQ(f.q).type === \'date\' ? \'date\' : (getQ(f.q).type === \'integer\' || getQ(f.q).type === \'decimal\' ? \'number\' : \'text\')" />\
        </div>\
        <div class="col-md-1 text-end">\
            <button class="btn btn-sm btn-link text-danger" @click="removeFilter(i)" aria-label="Eliminar filtro"><i class="bi bi-x-lg"></i></button>\
        </div>\
    </div>\
    <button class="btn btn-sm btn-outline-secondary me-2" @click="addFilter"><i class="bi bi-plus-lg me-1"></i> Agregar filtro</button>\
\
    <div v-if="filters.length > 1" class="d-inline-flex gap-3 ms-2 cq-text-sm">\
        <span class="cq-text-muted">Combinación:</span>\
        <label><input type="radio" v-model="combineMode" value="AND" /> Y todas</label>\
        <label><input type="radio" v-model="combineMode" value="OR" /> O cualquiera</label>\
    </div>\
\
    <hr />\
\
    <h3>Columnas en preview</h3>\
    <div class="d-flex flex-wrap gap-2 mb-3">\
        <label class="form-check"><input type="checkbox" class="form-check-input" v-model="columns.HC" /> Paciente (HC)</label>\
        <label v-for="q in selectedQ.questions" :key="q.linkId" class="form-check">\
            <input type="checkbox" class="form-check-input" :checked="columns[q.text]" @change="columns[q.text] = $event.target.checked" />\
            {{ q.text }}\
        </label>\
    </div>\
\
    <div class="d-flex justify-content-between align-items-center border-top pt-3">\
        <div>\
            Resultado estimado: <strong>{{ estimate }}</strong> registros\
        </div>\
        <div class="d-flex gap-2">\
            <button class="btn btn-outline-secondary" @click="save"><i class="bi bi-bookmark me-1"></i> Guardar</button>\
            <button class="btn btn-primary" @click="preview"><i class="bi bi-eye me-1"></i> Previsualizar</button>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('report-builder', ReportBuilder);
})();
