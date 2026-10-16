/*
 * Isla Vue: variable-form
 * Alta y edición de una variable clínica del catálogo (pantalla del Administrador).
 *
 * Es una isla y no Razor con unos inputs porque el formulario tiene tres cosas que en
 * markup estático se vuelven frágiles: el buscador SNOMED (que acá se consume desde
 * shared/snomed.js, no se reimplementa), la lista dinámica de opciones de respuesta —que
 * con un binder indexado obligaría a reindexar a mano al borrar una del medio— y los
 * campos que aparecen y desaparecen según el tipo.
 *
 * Persiste por el camino MVC: escribe el modelo completo en #variablePayload y deja que el
 * form de Razor lo postee con antiforgery. Así conserva el TempData["Toast"] + redirect del
 * resto de las pantallas de administración, en vez de inventar un tercer patrón de guardado.
 */
(function () {
    // Mismo vocabulario que QuestionTypeMap y que el renderer.
    var TYPES = [
        { code: 'decimal', label: 'Número decimal' },
        { code: 'integer', label: 'Número entero' },
        { code: 'string', label: 'Texto corto' },
        { code: 'text', label: 'Texto largo' },
        { code: 'boolean', label: 'Sí / No' },
        { code: 'choice', label: 'Opción única' },
        { code: 'openChoice', label: 'Opción múltiple' },
        { code: 'date', label: 'Fecha' },
        { code: 'dateTime', label: 'Fecha y hora' },
        { code: 'attachment', label: 'Adjunto' },
        { code: 'scale', label: 'Escala' }
    ];

    var NUMERIC = ['decimal', 'integer', 'scale'];
    var WITH_OPTIONS = ['choice', 'openChoice'];
    var WITH_MAXLENGTH = ['string', 'text'];

    var VariableForm = {
        props: {
            id: String,
            code: String,
            name: String,
            defaultText: String,
            description: String,
            type: String,
            repeatsAllowed: Boolean,
            unit: String,
            min: Number,
            max: Number,
            maxLength: Number,
            accept: String,
            snomedSystem: String,
            snomedCode: String,
            snomedDisplay: String,
            term: String,
            isActive: Boolean,
            sortOrder: Number,
            revision: Number,
            currentVersion: Number,
            options: { type: Array, default: function () { return []; } }
        },
        setup: function (props) {
            // En un alta el DTO trae Guid.Empty, que llega como string no vacío.
            var esNueva = !props.id || /^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(props.id);

            var model = Vue.reactive({
                code: props.code || '',
                name: props.name || '',
                defaultText: props.defaultText || '',
                description: props.description || '',
                type: props.type || 'decimal',
                repeatsAllowed: !!props.repeatsAllowed,
                unit: props.unit || '',
                min: props.min === undefined || props.min === null ? '' : props.min,
                max: props.max === undefined || props.max === null ? '' : props.max,
                maxLength: props.maxLength === undefined || props.maxLength === null ? '' : props.maxLength,
                accept: props.accept || '',
                sortOrder: props.sortOrder || 0,
                options: (props.options || []).map(function (o) {
                    return {
                        display: o.display || '',
                        value: o.value || '',
                        snomedCode: o.snomedCode || '',
                        snomedDisplay: o.snomedDisplay || ''
                    };
                })
            });

            // El buscador SNOMED escribe sobre term/code/codeDisplay: se expone un objeto con
            // esos tres nombres, igual que hacen la pregunta y el cuestionario en el editor.
            var coding = Vue.reactive({
                term: props.term || '',
                code: props.snomedCode || '',
                codeDisplay: props.snomedDisplay || ''
            });

            var snomed = window.ChequeARSnomed.createSearch(function () { return coding; });
            Vue.watch(function () { return coding.term; }, function (t) { snomed.schedule(t); });

            var codeVerifying = Vue.ref(false);
            var codeVerification = Vue.ref(null);

            // Verificación del código tipeado a mano. Debounce propio: acá no hay autosave que
            // lo absorba, y verificar en cada tecla golpearía el terminológico sin necesidad.
            var verifyDebounce;
            Vue.watch(function () { return coding.code; }, function () {
                codeVerification.value = null;
                clearTimeout(verifyDebounce);
                if (!(coding.code || '').trim()) return;
                verifyDebounce = setTimeout(verificar, 500);
            });

            function verificar() {
                codeVerifying.value = true;
                window.ChequeARSnomed.verifyCode(coding.code).then(function (r) {
                    if (r.ok) coding.codeDisplay = r.display;
                    codeVerification.value = r;
                    codeVerifying.value = false;
                });
            }

            // --- Visibilidad de campos según el tipo ---
            var esNumerico = Vue.computed(function () { return NUMERIC.indexOf(model.type) >= 0; });
            var tieneOpciones = Vue.computed(function () { return WITH_OPTIONS.indexOf(model.type) >= 0; });
            var tieneMaxLength = Vue.computed(function () { return WITH_MAXLENGTH.indexOf(model.type) >= 0; });
            var esAdjunto = Vue.computed(function () { return model.type === 'attachment'; });

            // El slug se propone desde el nombre mientras nadie lo haya tocado a mano. En una
            // variable ya creada NO se auto-genera: el código es la semilla del linkId de cada
            // pregunta, así que cambiarlo solo porque alguien corrigió una tilde del nombre
            // movería la clave sobre la que pivotean los reportes.
            var codeTocado = Vue.ref(!esNueva || !!props.code);
            Vue.watch(function () { return model.name; }, function (n) {
                if (codeTocado.value) return;
                model.code = slug(n);
            });

            function slug(s) {
                return (s || '')
                    .normalize('NFD').replace(/[̀-ͯ]/g, '')
                    .toLowerCase()
                    .replace(/[^a-z0-9]+/g, '-')
                    .replace(/^-+|-+$/g, '')
                    .slice(0, 64);
            }

            function addOption() {
                model.options.push({ display: '', value: '', snomedCode: '', snomedDisplay: '' });
            }
            function removeOption(i) { model.options.splice(i, 1); }

            function num(v) { return v === '' || v === null || v === undefined ? null : Number(v); }

            /* Serializa al contrato de ClinicalVariableDto. Los campos que no aplican al tipo
             * se mandan vacíos y el servidor los limpia igual (ClinicalVariableService.Normalize):
             * el cliente no es el guardián de esa regla, sólo evita mostrarlos. */
            function buildPayload() {
                return {
                    code: model.code.trim(),
                    name: model.name.trim(),
                    defaultText: model.defaultText.trim() || model.name.trim(),
                    description: model.description.trim(),
                    type: model.type,
                    repeatsAllowed: model.repeatsAllowed,
                    unit: esNumerico.value ? model.unit.trim() : '',
                    min: esNumerico.value ? num(model.min) : null,
                    max: esNumerico.value ? num(model.max) : null,
                    maxLength: tieneMaxLength.value ? num(model.maxLength) : null,
                    accept: esAdjunto.value ? model.accept.trim() : '',
                    snomedSystem: props.snomedSystem || '',
                    snomedCode: (coding.code || '').trim(),
                    snomedDisplay: (coding.codeDisplay || '').trim(),
                    term: (coding.term || '').trim(),
                    sortOrder: Number(model.sortOrder) || 0,
                    revision: props.revision || 0,
                    options: tieneOpciones.value ? model.options.map(function (o) {
                        return {
                            display: o.display.trim(),
                            value: (o.value || o.display).trim(),
                            snomedCode: (o.snomedCode || '').trim(),
                            snomedDisplay: (o.snomedDisplay || '').trim()
                        };
                    }) : []
                };
            }

            // El hidden se mantiene al día en vez de llenarse en el submit: si el navegador
            // dispara el submit por Enter en un input, el valor ya está puesto.
            function sync() {
                var hidden = document.getElementById('variablePayload');
                if (hidden) hidden.value = JSON.stringify(buildPayload());
            }
            Vue.watch([model, coding], sync, { deep: true, immediate: true });
            Vue.onMounted(sync);
            Vue.onBeforeUnmount(function () { snomed.cancel(); clearTimeout(verifyDebounce); });

            return {
                esNueva: esNueva, publicada: (props.currentVersion || 0) > 0,
                types: TYPES, model: model, coding: coding, snomed: snomed,
                codeVerifying: codeVerifying, codeVerification: codeVerification,
                esNumerico: esNumerico, tieneOpciones: tieneOpciones,
                tieneMaxLength: tieneMaxLength, esAdjunto: esAdjunto,
                codeTocado: codeTocado,
                addOption: addOption, removeOption: removeOption
            };
        },
        template: [
            '<div class="row g-3">',
            '  <div class="col-lg-7">',
            '    <div class="cq-card">',
            '      <h2 class="h3">Identidad</h2>',
            '      <div class="row g-2">',
            '        <div class="col-md-7">',
            '          <label class="form-label cq-text-sm">Nombre</label>',
            '          <input v-model="model.name" class="form-control" placeholder="Peso" />',
            '        </div>',
            '        <div class="col-md-5">',
            '          <label class="form-label cq-text-sm">Código</label>',
            '          <input v-model="model.code" @input="codeTocado = true" class="form-control" placeholder="peso" :disabled="publicada" :title="publicada ? \'Fijo en una variable publicada\' : null" />',
            '        </div>',
            '        <div class="col-12">',
            '          <label class="form-label cq-text-sm">Enunciado por defecto</label>',
            '          <input v-model="model.defaultText" class="form-control" placeholder="Peso corporal" />',
            '        </div>',
            '        <div class="col-12">',
            '          <label class="form-label cq-text-sm">Descripci&oacute;n</label>',
            '          <textarea v-model="model.description" rows="2" class="form-control" placeholder="Descripci&oacute;n..."></textarea>',
            '        </div>',
            '      </div>',
            '    </div>',

            '    <div class="cq-card mt-3">',
            '      <h2 class="h3">Respuesta</h2>',
            '      <div class="row g-2">',
            '        <div class="col-md-6">',
            '          <label class="form-label cq-text-sm">Tipo</label>',
            '          <select v-model="model.type" class="form-select" :disabled="publicada" :title="publicada ? \'Fijo en una variable publicada\' : null">',
            '            <option v-for="t in types" :key="t.code" :value="t.code">{{ t.label }}</option>',
            '          </select>',
            '        </div>',
            '        <div class="col-md-6 d-flex align-items-end">',
            '          <div class="form-check">',
            '            <input class="form-check-input" type="checkbox" id="chkRepeats" v-model="model.repeatsAllowed" :disabled="publicada" />',
            '            <label class="form-check-label cq-text-sm" for="chkRepeats">Admite varias respuestas</label>',
            '          </div>',
            '        </div>',

            '        <template v-if="esNumerico">',
            '          <div class="col-md-4">',
            '            <label class="form-label cq-text-sm">Unidad</label>',
            '            <input v-model="model.unit" class="form-control" placeholder="kg" />',
            '          </div>',
            '          <div class="col-md-4">',
            '            <label class="form-label cq-text-sm">M&iacute;nimo</label>',
            '            <input v-model="model.min" type="number" step="any" class="form-control" />',
            '          </div>',
            '          <div class="col-md-4">',
            '            <label class="form-label cq-text-sm">M&aacute;ximo</label>',
            '            <input v-model="model.max" type="number" step="any" class="form-control" />',
            '          </div>',
            '        </template>',

            '        <div class="col-md-6" v-if="tieneMaxLength">',
            '          <label class="form-label cq-text-sm">Largo m&aacute;ximo</label>',
            '          <input v-model="model.maxLength" type="number" min="1" max="2000" class="form-control" />',
            '        </div>',
            '        <div class="col-md-6" v-if="esAdjunto">',
            '          <label class="form-label cq-text-sm">Formatos aceptados</label>',
            '          <input v-model="model.accept" class="form-control" placeholder="application/pdf,image/*" />',
            '        </div>',
            '      </div>',

            '      <div v-if="tieneOpciones" class="mt-3">',
            '        <div class="d-flex justify-content-between align-items-center mb-2">',
            '          <h3 class="h3 mb-0">Opciones de respuesta</h3>',
            '          <button type="button" class="btn btn-sm btn-tertiary" @click="addOption">',
            '            <i class="bi bi-plus-lg me-1"></i>Agregar opci&oacute;n</button>',
            '        </div>',
            '        <p class="cq-text-xs cq-text-muted">En una pregunta de opci&oacute;n, el significado cl&iacute;nico est&aacute; en la respuesta: codificar cada una es lo que hace comparables los datos.</p>',
            '        <div v-for="(o, i) in model.options" :key="i" class="row g-2 align-items-end mb-2">',
            '          <div class="col-md-5">',
            '            <label class="form-label cq-text-xs" v-if="i === 0">Texto</label>',
            '            <input v-model="o.display" class="form-control form-control-sm" placeholder="Leve" />',
            '          </div>',
            '          <div class="col-md-3">',
            '            <label class="form-label cq-text-xs" v-if="i === 0">C&oacute;digo SNOMED</label>',
            '            <input v-model="o.snomedCode" class="form-control form-control-sm" placeholder="255604002" />',
            '          </div>',
            '          <div class="col-md-3">',
            '            <label class="form-label cq-text-xs" v-if="i === 0">T&eacute;rmino del concepto</label>',
            '            <input v-model="o.snomedDisplay" class="form-control form-control-sm" placeholder="Mild" />',
            '          </div>',
            '          <div class="col-md-1 text-end">',
            '            <button type="button" class="btn btn-sm btn-tertiary" @click="removeOption(i)" title="Quitar">',
            '              <i class="bi bi-x-lg"></i></button>',
            '          </div>',
            '        </div>',
            '        <div v-if="!model.options.length" class="cq-text-sm cq-text-muted">Todav&iacute;a no hay opciones.</div>',
            '      </div>',
            '    </div>',
            '  </div>',

            '  <div class="col-lg-5">',
            '    <div class="cq-card">',
            '      <h2 class="h3">Codificaci&oacute;n cl&iacute;nica</h2>',
            '      <label class="form-label cq-text-sm">T&eacute;rmino</label>',
            '      <input v-model="coding.term" class="form-control" placeholder="peso corporal" />',

            '      <div v-if="snomed.searching" class="cq-text-sm cq-text-muted mt-2">',
            '        <span class="cq-spinner"></span> Buscando en SNOMED CT&hellip;</div>',
            '      <div v-if="snomed.error" class="cq-text-sm text-danger mt-2">{{ snomed.error }}</div>',
            '      <ul v-if="snomed.open && snomed.results.length" class="list-group mt-2">',
            '        <li v-for="r in snomed.results" :key="r.code"',
            '            class="list-group-item list-group-item-action" role="button" @click="snomed.pick(r)">',
            '          <div>{{ r.display }}</div>',
            '          <code class="cq-text-xs cq-text-muted">{{ r.code }}</code>',
            '        </li>',
            '      </ul>',

            '      <hr />',
            '      <label class="form-label cq-text-sm">C&oacute;digo SNOMED CT</label>',
            '      <div class="d-flex gap-2 align-items-center">',
            '        <input v-model="coding.code" class="form-control" placeholder="27113001" />',
            '        <button type="button" class="btn btn-sm btn-tertiary" @click="snomed.clear()" v-if="coding.code" title="Quitar el c&oacute;digo">',
            '          <i class="bi bi-x-lg"></i></button>',
            '      </div>',
            '      <div v-if="codeVerifying" class="cq-text-sm cq-text-muted mt-1">Verificando&hellip;</div>',
            '      <div v-else-if="codeVerification" class="cq-text-sm mt-1"',
            '           :class="codeVerification.ok ? \'text-success\' : \'text-danger\'">',
            '        <template v-if="codeVerification.ok">&check; {{ codeVerification.display }}</template>',
            '        <template v-else>{{ codeVerification.msg }}</template>',
            '      </div>',
            '      <div v-else-if="coding.codeDisplay" class="cq-text-sm cq-text-muted mt-1">{{ coding.codeDisplay }}</div>',
            '    </div>',

            '  </div>',
            '</div>'
        ].join('\n')
    };

    window.ChequeARIslands.register('variable-form', VariableForm);
})();
