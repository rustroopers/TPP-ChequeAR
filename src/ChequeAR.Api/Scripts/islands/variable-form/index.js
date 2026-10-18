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
            isPublished: Boolean,
            replacesId: String,
            replacesName: String,
            deactivateReplacedOnPublish: Boolean,
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
                deactivateReplaced: !!props.deactivateReplacedOnPublish,
                options: (props.options || []).map(function (o) {
                    return {
                        display: o.display || '',
                        value: o.value || '',
                        snomedCode: o.snomedCode || '',
                        snomedDisplay: o.snomedDisplay || '',
                        // Sólo de pantalla: si se codifica. Una opción ya codificada arranca activa.
                        snomed: !!o.snomedCode
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

            var snomed = window.ChequeARSnomed.createSearch(function () { return coding; }, null, 'variable');
            // El término que deja un concepto elegido no es una búsqueda nueva.
            var terminoElegido = null;
            Vue.watch(function () { return coding.term; }, function (t) {
                if (terminoElegido !== null && t === terminoElegido) { terminoElegido = null; return; }
                terminoElegido = null;
                snomed.schedule(t);
            });

            // Al elegir un concepto, su término reemplaza al escrito: ya no hace falta repetirlo abajo.
            var codigoElegido = null;
            function elegir(r) {
                codigoElegido = r.code;
                if (coding.term !== r.display) terminoElegido = r.display;
                snomed.pick(r);
                coding.term = r.display;
            }

            var codeVerifying = Vue.ref(false);
            var codeVerification = Vue.ref(null);

            // Verificación del código tipeado a mano. Debounce propio: acá no hay autosave que
            // lo absorba, y verificar en cada tecla golpearía el terminológico sin necesidad.
            var verifyDebounce;
            Vue.watch(function () { return coding.code; }, function () {
                codeVerification.value = null;
                clearTimeout(verifyDebounce);
                if (!(coding.code || '').trim()) return;
                // El código de un concepto elegido de la lista ya viene del terminológico.
                if (coding.code === codigoElegido) return;
                codigoElegido = null;
                verifyDebounce = setTimeout(verificar, 500);
            });

            // --- SNOMED CT de las opciones de respuesta ---
            // Busca con el texto de la opción y deja ese texto como está: es lo que ve quien
            // completa. Un solo buscador, apuntado a la opción que se está editando.
            var opcionActiva = Vue.ref(-1);
            var opcionSnomed = window.ChequeARSnomed.createSearch(function () {
                var o = model.options[opcionActiva.value];
                if (!o) return null;
                return {
                    get term() { return o.display; },
                    get code() { return o.snomedCode; },
                    set code(v) { o.snomedCode = v || ''; },
                    get codeDisplay() { return o.snomedDisplay; },
                    set codeDisplay(v) { o.snomedDisplay = v || ''; }
                };
            }, null, 'variable-option');

            function enfocarOpcion(i) {
                if (opcionActiva.value === i) return;
                opcionSnomed.reset();
                opcionActiva.value = i;
            }
            function buscarOpcion(i) {
                var o = model.options[i];
                if (!o || !o.snomed) return;
                enfocarOpcion(i);
                opcionSnomed.schedule(o.display);
            }
            function alternarSnomed(i) {
                if (model.options[i].snomed) buscarOpcion(i);
                else if (opcionActiva.value === i) opcionSnomed.reset();
            }

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
                model.options.push({ display: '', value: '', snomedCode: '', snomedDisplay: '', snomed: false });
            }
            function removeOption(i) {
                // Los índices se corren: la búsqueda en curso quedaría apuntando a otra opción.
                opcionSnomed.reset();
                opcionActiva.value = -1;
                model.options.splice(i, 1);
            }

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
                    replacesId: props.replacesId || null,
                    deactivateReplacedOnPublish: !!props.replacesId && model.deactivateReplaced,
                    options: tieneOpciones.value ? model.options.map(function (o) {
                        return {
                            display: o.display.trim(),
                            value: (o.value || o.display).trim(),
                            snomedCode: o.snomed ? (o.snomedCode || '').trim() : '',
                            snomedDisplay: o.snomed ? (o.snomedDisplay || '').trim() : ''
                        };
                    }) : []
                };
            }

            // El hidden se mantiene al día en vez de llenarse en el submit: si el navegador
            // dispara el submit por Enter en un input, el valor ya está puesto.
            // Publicar, como en el editor de cuestionarios: sólo con todo guardado.
            var guardado = null;
            function sync() {
                var json = JSON.stringify(buildPayload());
                var hidden = document.getElementById('variablePayload');
                if (hidden) hidden.value = json;
                if (guardado === null) guardado = json;
                var pub = document.getElementById('publishBtn');
                var save = document.getElementById('saveBtn');
                var sinGuardar = json !== guardado || (pub != null && pub.hasAttribute('data-unsaved'));
                // Como en el editor de cuestionarios: Guardar sólo con algo para guardar.
                if (save && !esNueva) save.disabled = !sinGuardar;
                if (pub) {
                    pub.disabled = sinGuardar || !pub.hasAttribute('data-pending');
                    pub.title = sinGuardar ? 'Guarde los cambios antes de publicar'
                        : (pub.disabled ? 'No hay cambios para publicar' : '');
                }
            }
            Vue.watch([model, coding], sync, { deep: true, immediate: true });
            Vue.onMounted(sync);
            Vue.onBeforeUnmount(function () { snomed.cancel(); opcionSnomed.cancel(); clearTimeout(verifyDebounce); });

            return {
                esNueva: esNueva, publicada: !!props.isPublished,
                replacesId: props.replacesId, replacesName: props.replacesName,
                types: TYPES, model: model, coding: coding, snomed: snomed,
                codeVerifying: codeVerifying, codeVerification: codeVerification,
                esNumerico: esNumerico, tieneOpciones: tieneOpciones,
                tieneMaxLength: tieneMaxLength, esAdjunto: esAdjunto,
                codeTocado: codeTocado, elegir: elegir,
                opcionActiva: opcionActiva, opcionSnomed: opcionSnomed,
                enfocarOpcion: enfocarOpcion, buscarOpcion: buscarOpcion, alternarSnomed: alternarSnomed,
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
            '          <input v-model="model.name" class="form-control" :placeholder="replacesName || \'Peso\'" :disabled="publicada" :title="publicada ? \'Fijo en una variable publicada\' : null" />',
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
            '        <div class="col-12" v-if="replacesId && !publicada">',
            '          <div class="form-check">',
            '            <input class="form-check-input" type="checkbox" id="chkDeactivateReplaced" v-model="model.deactivateReplaced" />',
            '            <label class="form-check-label cq-text-sm" for="chkDeactivateReplaced">Dar de baja &laquo;{{ replacesName }}&raquo; al publicar</label>',
            '          </div>',
            '        </div>',
            '      </div>',
            '    </div>',

            '    <div class="cq-card mt-3">',
            '      <h2 class="h3">Respuesta</h2>',
            '      <fieldset :disabled="publicada" class="border-0 p-0 m-0">',
            '      <div class="row g-2">',
            '        <div class="col-md-6">',
            '          <label class="form-label cq-text-sm">Tipo</label>',
            '          <select v-model="model.type" class="form-select">',
            '            <option v-for="t in types" :key="t.code" :value="t.code">{{ t.label }}</option>',
            '          </select>',
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
            '          <button type="button" class="btn btn-sm btn-primary" @click="addOption">',
            '            <i class="bi bi-plus-lg me-1"></i>Agregar opci&oacute;n</button>',
            '        </div>',
            '        <div v-for="(o, i) in model.options" :key="i" class="row g-2 align-items-end mb-3">',
            '          <div class="col-12 col-md position-relative" data-snomed-area="variable-option">',
            '            <label class="form-label cq-text-xs" v-if="i === 0 || o.snomed">Texto</label>',
            '            <input v-model="o.display" class="form-control form-control-sm" placeholder="Leve"',
            '                   @focus="enfocarOpcion(i)" @input="buscarOpcion(i)" />',
            '            <div v-if="o.snomed && opcionActiva === i && (opcionSnomed.searching || opcionSnomed.error || opcionSnomed.open)"',
            '                 class="position-absolute bg-white border rounded shadow-sm mt-1"',
            '                 style="z-index:1000; top:100%; left:calc(var(--bs-gutter-x) * .5); right:calc(var(--bs-gutter-x) * .5); min-width:20rem; max-height:240px; overflow-y:auto;">',
            '              <div v-if="opcionSnomed.searching" class="cq-text-xs cq-text-muted px-2 py-1"><span class="cq-spinner cq-spinner--sm"></span> Buscando en SNOMED CT&hellip;</div>',
            '              <div v-else-if="opcionSnomed.error" class="cq-text-xs text-danger px-2 py-1">{{ opcionSnomed.error }}</div>',
            '              <ul v-else-if="opcionSnomed.results.length" class="list-group list-group-flush">',
            '                <li v-for="r in opcionSnomed.results" :key="r.code" class="list-group-item list-group-item-action px-2 py-1 cq-text-sm"',
            '                    role="button" @click="opcionSnomed.pick(r)">',
            '                  {{ r.display }} <code class="cq-text-xs cq-text-muted">{{ r.code }}</code>',
            '                </li>',
            '              </ul>',
            '              <div v-else class="cq-text-xs cq-text-muted px-2 py-1">Sin resultados.</div>',
            '            </div>',
            '          </div>',
            '          <template v-if="o.snomed">',
            '            <div class="col-6 col-md-2">',
            '              <label class="form-label cq-text-xs">C&oacute;digo</label>',
            '              <input v-model="o.snomedCode" class="form-control form-control-sm" placeholder="255604002" />',
            '            </div>',
            '            <div class="col-6 col-md-3">',
            '              <label class="form-label cq-text-xs">T&eacute;rmino</label>',
            '              <input v-model="o.snomedDisplay" class="form-control form-control-sm" placeholder="Mild" />',
            '            </div>',
            '          </template>',
            '          <div class="col-auto">',
            // Interruptor arriba y rótulo abajo, en la altura de un input -sm para no agrandar la fila.
            '            <div class="form-check form-switch p-0 m-0 d-flex flex-column align-items-center justify-content-center"',
            '                 style="height:calc(1.5em + .5rem + 2px);">',
            '              <input class="form-check-input float-none m-0" type="checkbox" role="switch" :id="\'optSnomed\' + i" v-model="o.snomed" @change="alternarSnomed(i)" />',
            '              <label class="form-check-label cq-text-xs lh-1 mt-1" :for="\'optSnomed\' + i">SNOMED-CT</label>',
            '            </div>',
            '          </div>',
            '          <div class="col-auto ms-auto text-end">',
            '            <button type="button" class="cq-icon-btn cq-icon-btn--danger" @click="removeOption(i)" title="Quitar" aria-label="Quitar opci&oacute;n">',
            '              <i class="bi bi-trash cq-icon-btn__idle"></i><i class="bi bi-trash-fill cq-icon-btn__hover"></i></button>',
            '          </div>',
            '        </div>',
            '        <div v-if="!model.options.length" class="cq-text-sm cq-text-muted">Todav&iacute;a no hay opciones.</div>',
            '      </div>',
            '      </fieldset>',
            '    </div>',
            '  </div>',

            '  <div class="col-lg-5">',
            '    <div class="cq-card" data-snomed-area="variable">',
            '      <h2 class="h3">Codificaci&oacute;n cl&iacute;nica</h2>',
            '      <fieldset :disabled="publicada" class="border-0 p-0 m-0">',
            '      <label class="form-label cq-text-sm">T&eacute;rmino</label>',
            '      <input v-model="coding.term" class="form-control" placeholder="peso corporal" />',

            '      <div v-if="snomed.searching" class="cq-text-sm cq-text-muted mt-2">',
            '        <span class="cq-spinner"></span> Buscando en SNOMED CT&hellip;</div>',
            '      <div v-if="snomed.error" class="cq-text-sm text-danger mt-2">{{ snomed.error }}</div>',
            '      <ul v-if="snomed.open && snomed.results.length" class="list-group mt-2">',
            '        <li v-for="r in snomed.results" :key="r.code"',
            '            class="list-group-item list-group-item-action" role="button" @click="elegir(r)">',
            '          <div>{{ r.display }}</div>',
            '          <code class="cq-text-xs cq-text-muted">{{ r.code }}</code>',
            '        </li>',
            '      </ul>',

            '      <hr />',
            '      <label class="form-label cq-text-sm">C&oacute;digo SNOMED CT</label>',
            '      <div class="d-flex gap-2 align-items-center">',
            '        <input v-model="coding.code" class="form-control" placeholder="27113001" />',
            '        <button type="button" class="cq-icon-btn" @click="snomed.clear()" v-if="coding.code && !publicada" title="Quitar el c&oacute;digo" aria-label="Quitar el c&oacute;digo">',
            '          <i class="bi bi-x-lg"></i></button>',
            '      </div>',
            '      <div v-if="codeVerifying" class="cq-text-sm cq-text-muted mt-1">Verificando&hellip;</div>',
            '      <div v-else-if="codeVerification" class="cq-text-sm mt-1"',
            '           :class="codeVerification.ok ? \'text-success\' : \'text-danger\'">',
            '        <template v-if="codeVerification.ok">&check; {{ codeVerification.display }}</template>',
            '        <template v-else>{{ codeVerification.msg }}</template>',
            '      </div>',
            '      </fieldset>',
            '    </div>',

            '  </div>',
            '</div>'
        ].join('\n')
    };

    window.ChequeARIslands.register('variable-form', VariableForm);
})();
