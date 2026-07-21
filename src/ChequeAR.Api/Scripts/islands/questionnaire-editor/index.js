/*
 * Isla Vue: questionnaire-editor (pantalla Q2)
 * Editor de cuestionarios FHIR Questionnaire con paleta de tipos, árbol de grupos
 * y panel de propiedades. Drag-and-drop pendiente de SortableJS.
 */
(function () {
    var QuestionnaireEditor = {
        props: { id: String, title: String, version: Number, isNew: Boolean },
        setup: function (props) {
            // Estado de persistencia. Para un cuestionario nuevo el id del controller es
            // sólo provisorio: el backend asigna uno propio al crear (POST). A partir de
            // ahí pasamos a modo edición (PUT) usando el id devuelto.
            var currentId = Vue.ref(props.id);
            var isNew = Vue.ref(!!props.isNew);
            var qTitle = Vue.ref(props.title || 'Nuevo cuestionario');
            var specialtyCode = Vue.ref('general');
            var saving = Vue.ref(false);
            var groups = Vue.ref([
                {
                    title: 'Datos del paciente',
                    questions: [
                        { linkId: 'edad', text: 'Edad', type: 'integer', required: true, unit: 'años', min: 0, max: 120 },
                        { linkId: 'sexo', text: 'Sexo', type: 'choice', required: true, options: ['Femenino', 'Masculino', 'Otro'] }
                    ]
                },
                {
                    title: 'Signos vitales',
                    questions: [
                        { linkId: 'tasis', text: 'TA sistólica', type: 'decimal', required: true, unit: 'mmHg' },
                        { linkId: 'tadia', text: 'TA diastólica', type: 'decimal', required: true, unit: 'mmHg' },
                        { linkId: 'fc',    text: 'FC',            type: 'integer', required: true, unit: 'lpm' }
                    ]
                },
                {
                    title: 'Antecedentes',
                    questions: [
                        { linkId: 'hta', text: '¿HTA?', type: 'boolean', required: true }
                    ]
                }
            ]);

            var types = [
                { code: 'string',  label: 'Texto',     icon: 'bi-fonts' },
                { code: 'integer', label: 'Número',    icon: 'bi-123' },
                { code: 'decimal', label: 'Decimal',   icon: 'bi-percent' },
                { code: 'boolean', label: 'Booleano',  icon: 'bi-toggle-on' },
                { code: 'choice',  label: 'Opción',    icon: 'bi-ui-radios' },
                { code: 'date',    label: 'Fecha',     icon: 'bi-calendar' },
                { code: 'attachment', label: 'Adjunto', icon: 'bi-paperclip' }
            ];

            var selected = Vue.ref(null);
            var dirty = Vue.ref(false);
            var lastSaved = Vue.ref(null);

            function selectQuestion(g, q) { selected.value = { group: g, question: q }; }
            function addGroup() {
                groups.value.push({ title: 'Nueva sección', questions: [] });
                dirty.value = true;
            }
            function addQuestion(g, type) {
                var q = {
                    linkId: 'q_' + Math.random().toString(36).slice(2, 7),
                    text: 'Nueva pregunta',
                    type: type,
                    required: false
                };
                if (type === 'choice') q.options = ['Opción 1', 'Opción 2'];
                g.questions.push(q);
                selected.value = { group: g, question: q };
                dirty.value = true;
            }
            function removeQuestion(g, q) {
                g.questions = g.questions.filter(function (x) { return x !== q; });
                if (selected.value && selected.value.question === q) selected.value = null;
                dirty.value = true;
            }
            function removeGroup(g) {
                groups.value = groups.value.filter(function (x) { return x !== g; });
                if (selected.value && selected.value.group === g) selected.value = null;
                dirty.value = true;
            }
            function toast(msg, kind) { if (window.cqToast) window.cqToast(msg, kind); }

            function getToken() {
                var meta = document.querySelector('meta[name="cq-token"]');
                return meta ? meta.getAttribute('content') : null;
            }

            // Aplana grupos -> array plano de Questions (entidad de dominio). El backend
            // bindea el enum Type por nombre (case-insensitive), así que mandamos el code tal cual.
            function buildPayload() {
                var order = 0;
                var questions = [];
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        questions.push({
                            linkId: q.linkId,
                            text: q.text,
                            type: q.type,
                            required: !!q.required,
                            orderIndex: order++
                        });
                    });
                });
                return {
                    title: qTitle.value,
                    specialtyCode: specialtyCode.value,
                    questions: questions
                };
            }

            function save() {
                if (saving.value) return;
                var token = getToken();
                if (!token) { toast('No hay sesión: no se puede guardar (token ausente).', 'error'); return; }

                saving.value = true;
                var creating = isNew.value;
                var url = creating
                    ? '/fhir/r5/Questionnaire'
                    : '/fhir/r5/Questionnaire/' + currentId.value;

                fetch(url, {
                    method: creating ? 'POST' : 'PUT',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': 'Bearer ' + token
                    },
                    body: JSON.stringify(buildPayload())
                }).then(function (res) {
                    if (!res.ok) { throw new Error('HTTP ' + res.status); }
                    return res.status === 204 ? null : res.json().catch(function () { return null; });
                }).then(function (data) {
                    if (creating && data && data.id) {
                        currentId.value = data.id;
                        isNew.value = false;
                        // Reflejar el id real en la URL sin recargar.
                        if (window.history && window.history.replaceState) {
                            window.history.replaceState(null, '', '/Questionnaires/Edit/' + data.id);
                        }
                    }
                    lastSaved.value = new Date().toLocaleTimeString();
                    dirty.value = false;
                    toast(creating ? 'Cuestionario creado' : 'Cambios guardados', 'success');
                }).catch(function (err) {
                    toast('Error al guardar: ' + err.message, 'error');
                }).then(function () {
                    saving.value = false;
                });
            }

            // Autosave (2s debounce) sólo para cuestionarios ya persistidos; así evitamos
            // crear duplicados mientras se arma uno nuevo. El primer guardado de un nuevo
            // cuestionario es siempre manual (botón Guardar).
            var debounce;
            Vue.watch(groups, function () {
                dirty.value = true;
                if (isNew.value) return;
                clearTimeout(debounce);
                debounce = setTimeout(save, 2000);
            }, { deep: true });

            // Engancha el botón "Guardar" del header (vive fuera de la isla).
            Vue.onMounted(function () {
                var btn = document.getElementById('saveBtn');
                if (btn) btn.addEventListener('click', save);
            });
            Vue.onUnmounted(function () {
                var btn = document.getElementById('saveBtn');
                if (btn) btn.removeEventListener('click', save);
            });

            return {
                groups: groups, types: types, selected: selected,
                dirty: dirty, lastSaved: lastSaved, saving: saving,
                qTitle: qTitle, specialtyCode: specialtyCode, isNew: isNew,
                selectQuestion: selectQuestion, addGroup: addGroup, addQuestion: addQuestion,
                removeQuestion: removeQuestion, removeGroup: removeGroup, save: save
            };
        },
        template: '\
<div class="row g-3">\
    <div class="col-lg-2">\
        <div class="cq-card" style="position:sticky; top:84px;">\
            <h3 class="cq-section-title" style="margin-top:0;">Tipos</h3>\
            <div class="d-flex flex-column gap-1">\
                <div v-for="t in types" :key="t.code" class="cq-text-sm d-flex align-items-center gap-2 p-1" style="border-radius:6px; cursor:grab;">\
                    <i :class="\'bi \' + t.icon"></i> {{ t.label }}\
                </div>\
            </div>\
            <p class="cq-text-xs cq-text-muted mt-3 mb-0">Hacé click en + dentro de una sección para agregar.</p>\
        </div>\
    </div>\
\
    <div class="col-lg-6">\
        <div class="cq-card mb-2">\
            <div class="row g-2">\
                <div class="col-8">\
                    <label class="form-label cq-text-sm">Título del cuestionario</label>\
                    <input v-model="qTitle" class="form-control form-control-sm" placeholder="Ej: Cardio basal" />\
                </div>\
                <div class="col-4">\
                    <label class="form-label cq-text-sm">Especialidad</label>\
                    <select v-model="specialtyCode" class="form-select form-select-sm">\
                        <option value="general">General</option>\
                        <option value="cardio">Cardiología</option>\
                        <option value="neumo">Neumonología</option>\
                        <option value="onco">Oncología</option>\
                    </select>\
                </div>\
            </div>\
        </div>\
        <div class="d-flex justify-content-between align-items-center mb-2">\
            <h3 class="m-0">Estructura</h3>\
            <span class="cq-text-xs cq-text-muted" v-if="lastSaved">Guardado {{ lastSaved }}</span>\
            <span class="cq-text-xs" style="color:var(--cq-warning);" v-else-if="dirty">Cambios sin guardar</span>\
        </div>\
        <div v-for="(g, gi) in groups" :key="gi" class="cq-card mb-2">\
            <div class="d-flex align-items-center gap-2 mb-2">\
                <i class="bi bi-folder text-primary"></i>\
                <input v-model="g.title" class="form-control form-control-sm fw-semibold" style="max-width:340px;" />\
                <span class="cq-text-xs cq-text-muted">{{ g.questions.length }} preguntas</span>\
                <button class="btn btn-sm btn-link text-danger ms-auto" @click="removeGroup(g)" :aria-label="\'Eliminar sección \' + g.title"><i class="bi bi-trash"></i></button>\
            </div>\
            <ul class="list-unstyled mb-2">\
                <li v-for="q in g.questions" :key="q.linkId"\
                    class="d-flex align-items-center gap-2 py-1 px-2"\
                    :style="{ borderRadius: \'6px\', cursor: \'pointer\', background: selected && selected.question === q ? \'var(--cq-primary-100)\' : \'transparent\' }"\
                    @click="selectQuestion(g, q)">\
                    <i class="bi bi-grip-vertical cq-text-muted" style="cursor:grab;"></i>\
                    <span class="cq-text-sm flex-fill">{{ q.text }} <span class="cq-text-xs cq-text-muted">({{ q.type }}{{ q.required ? \'*\' : \'\' }})</span></span>\
                    <button class="btn btn-sm btn-link text-danger" @click.stop="removeQuestion(g, q)" :aria-label="\'Eliminar \' + q.text"><i class="bi bi-x-lg"></i></button>\
                </li>\
            </ul>\
            <div class="dropdown">\
                <button class="btn btn-sm btn-outline-secondary dropdown-toggle" data-bs-toggle="dropdown">+ Agregar pregunta</button>\
                <ul class="dropdown-menu">\
                    <li v-for="t in types" :key="t.code"><a class="dropdown-item" href="javascript:" @click="addQuestion(g, t.code)"><i :class="\'bi \' + t.icon + \' me-1\'"></i> {{ t.label }}</a></li>\
                </ul>\
            </div>\
        </div>\
        <button class="btn btn-outline-secondary w-100" @click="addGroup"><i class="bi bi-plus-lg me-1"></i> Agregar sección</button>\
    </div>\
\
    <div class="col-lg-4">\
        <div v-if="selected" class="cq-card" style="position:sticky; top:84px;">\
            <h3 class="cq-section-title" style="margin-top:0;">Propiedades</h3>\
            <div class="mb-2">\
                <label class="form-label cq-text-sm">Texto</label>\
                <input v-model="selected.question.text" class="form-control form-control-sm" />\
            </div>\
            <div class="mb-2">\
                <label class="form-label cq-text-sm">Tipo</label>\
                <select v-model="selected.question.type" class="form-select form-select-sm">\
                    <option v-for="t in types" :key="t.code" :value="t.code">{{ t.label }}</option>\
                </select>\
            </div>\
            <div class="form-check mb-2">\
                <input v-model="selected.question.required" type="checkbox" class="form-check-input" id="reqCk" />\
                <label class="form-check-label cq-text-sm" for="reqCk">Obligatoria</label>\
            </div>\
            <div v-if="[\'integer\',\'decimal\'].indexOf(selected.question.type) >= 0">\
                <div class="row g-2 mb-2">\
                    <div class="col-6"><label class="form-label cq-text-sm">Min</label><input v-model.number="selected.question.min" type="number" class="form-control form-control-sm" /></div>\
                    <div class="col-6"><label class="form-label cq-text-sm">Max</label><input v-model.number="selected.question.max" type="number" class="form-control form-control-sm" /></div>\
                </div>\
                <div class="mb-2"><label class="form-label cq-text-sm">Unidad</label><input v-model="selected.question.unit" class="form-control form-control-sm" /></div>\
            </div>\
            <div v-if="selected.question.type === \'choice\'">\
                <label class="form-label cq-text-sm">Opciones</label>\
                <textarea v-model="selected.question.optionsText" @input="selected.question.options = $event.target.value.split(\'\\n\').filter(Boolean)" class="form-control form-control-sm" rows="3" :placeholder="(selected.question.options||[]).join(\'\\n\')">{{ (selected.question.options || []).join(\'\\n\') }}</textarea>\
                <p class="cq-text-xs cq-text-muted mt-1">Una opción por línea.</p>\
            </div>\
            <hr />\
            <button class="btn btn-sm btn-outline-danger" @click="removeQuestion(selected.group, selected.question)"><i class="bi bi-trash me-1"></i> Eliminar pregunta</button>\
        </div>\
        <div v-else class="cq-card cq-text-sm cq-text-muted">Seleccioná una pregunta para editar sus propiedades.</div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-editor', QuestionnaireEditor);
})();
