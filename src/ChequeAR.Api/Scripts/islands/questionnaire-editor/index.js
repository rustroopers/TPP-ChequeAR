/*
 * Isla Vue: questionnaire-editor (pantalla Q2)
 * Editor de cuestionarios FHIR Questionnaire con paleta de tipos, árbol de grupos
 * y panel de propiedades. Drag-and-drop pendiente de SortableJS.
 */
(function () {
    var QuestionnaireEditor = {
        props: {
            id: String, title: String, version: Number, isNew: Boolean,
            specialtyCode: String,
            status: String,
            concurrencyVersion: Number,
            groups: { type: Array, default: function () { return []; } }
        },
        setup: function (props) {
            // Estado de persistencia. Para un cuestionario nuevo el id del controller es
            // sólo provisorio: el backend asigna uno propio al crear (POST). A partir de
            // ahí pasamos a modo edición (PUT) usando el id devuelto.
            var currentId = Vue.ref(props.id);
            var isNew = Vue.ref(!!props.isNew);
            var qTitle = Vue.ref(props.title || 'Nuevo cuestionario');
            var specialtyCode = Vue.ref(props.specialtyCode || 'general');
            var saving = Vue.ref(false);
            var publishing = Vue.ref(false);
            // Un publicado no se autoguarda: sus cambios quedan pendientes hasta que
            // alguien decide publicar la versión siguiente. Guardar es una decisión.
            var isPublished = Vue.ref(props.status === 'published');
            // Versión del registro que este editor cargó. Viaja en If-Match y se refresca
            // con cada guardado propio. Si otra persona guarda, la suya avanza y la nuestra
            // queda vieja -> el server responde 409 en vez de dejarnos pisar su trabajo.
            var recordVersion = Vue.ref(props.concurrencyVersion || 0);
            var conflict = Vue.ref(false);

            // Estructura real del cuestionario, inyectada por el server en data-props.
            // Uno nuevo arranca con una sección vacía (no con datos de ejemplo: se
            // guardaban como si fueran del usuario).
            var initial = (props.groups || []).map(function (g) {
                return {
                    title: g.title,
                    questions: (g.questions || []).map(function (q) {
                        return {
                            linkId: q.linkId,
                            text: q.text,
                            type: q.type,
                            required: !!q.required,
                            unit: q.unit,
                            min: typeof q.min === 'number' ? q.min : undefined,
                            max: typeof q.max === 'number' ? q.max : undefined,
                            accept: q.accept,
                            enableWhen: q.enableWhen ? {
                                question: q.enableWhen.question,
                                op: q.enableWhen.op || '=',
                                value: q.enableWhen.value
                            } : undefined,
                            code: q.code,
                            codeDisplay: q.codeDisplay,
                            options: q.options && q.options.length ? q.options.slice() : undefined,
                            optionsText: q.options && q.options.length ? q.options.join('\n') : ''
                        };
                    })
                };
            });
            if (!initial.length) initial = [{ title: 'Nueva sección', questions: [] }];
            var groups = Vue.ref(initial);

            // Los códigos coinciden con QuestionType del backend (bindeo por nombre,
            // case-insensitive) y con los que dibuja questionnaire-renderer. Antes de
            // sumar uno acá hay que poder renderizarlo allá, o queda una pregunta que se
            // puede crear pero no responder.
            var types = [
                { code: 'string',     label: 'Texto corto',     icon: 'bi-fonts' },
                { code: 'text',       label: 'Texto largo',     icon: 'bi-textarea-t' },
                { code: 'integer',    label: 'Número',          icon: 'bi-123' },
                { code: 'decimal',    label: 'Decimal',         icon: 'bi-percent' },
                { code: 'scale',      label: 'Escala',          icon: 'bi-sliders' },
                { code: 'boolean',    label: 'Booleano',        icon: 'bi-toggle-on' },
                { code: 'choice',     label: 'Opción única',    icon: 'bi-ui-radios' },
                { code: 'openChoice', label: 'Opción múltiple', icon: 'bi-check2-square' },
                { code: 'date',       label: 'Fecha',           icon: 'bi-calendar' },
                { code: 'dateTime',   label: 'Fecha y hora',    icon: 'bi-clock' },
                { code: 'attachment', label: 'Adjunto',         icon: 'bi-paperclip' }
            ];

            var selected = Vue.ref(null);
            var dirty = Vue.ref(false);
            var lastSaved = Vue.ref(null);

            function selectQuestion(g, q) { selected.value = { group: g, question: q }; }

            // --- Lógica condicional (enableWhen) -------------------------------------
            // Solo se puede condicionar contra preguntas ANTERIORES: al responder, el
            // valor de una pregunta posterior todavía no existe, y además evita ciclos.
            var conditionSources = Vue.computed(function () {
                if (!selected.value) return [];
                var list = [];
                var reached = false;
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (q === selected.value.question) { reached = true; return; }
                        if (!reached && q.linkId) list.push(q);
                    });
                });
                return list;
            });

            // La pregunta de la que depende la condición actual: define qué input mostrar
            // para el valor esperado.
            var conditionSource = Vue.computed(function () {
                var ew = selected.value && selected.value.question.enableWhen;
                if (!ew || !ew.question) return null;
                var found = null;
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) { if (q.linkId === ew.question) found = q; });
                });
                return found;
            });

            function onConditionQuestion(linkId) {
                var q = selected.value.question;
                if (!linkId) q.enableWhen = undefined;
                else q.enableWhen = { question: linkId, op: '=', value: '' };
                dirty.value = true;
            }

            function onConditionValue(raw) {
                var q = selected.value.question;
                if (!q.enableWhen) return;
                var src = conditionSource.value;
                // El renderer compara con === , así que el tipo tiene que coincidir con el
                // de la respuesta: booleano contra booleano, número contra número.
                if (src && src.type === 'boolean') q.enableWhen.value = (raw === 'true');
                else if (src && ['integer', 'decimal', 'scale'].indexOf(src.type) >= 0) q.enableWhen.value = raw === '' ? '' : Number(raw);
                else q.enableWhen.value = raw;
                dirty.value = true;
            }
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
                if (type === 'choice' || type === 'openChoice') q.options = ['Opción 1', 'Opción 2'];
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

            // min/max/unidad viven en Question.ValidationJson (un CLOB con JSON), que es de
            // donde los lee el renderer para validar la carga. Se manda serializado porque
            // la API bindea directo contra la entidad y la propiedad es string.
            function buildValidation(q) {
                if (q.type === 'attachment') {
                    return q.accept ? JSON.stringify({ accept: q.accept }) : null;
                }
                if (['integer', 'decimal', 'scale'].indexOf(q.type) < 0) return null;
                var v = {};
                if (typeof q.min === 'number' && !isNaN(q.min)) v.min = q.min;
                if (typeof q.max === 'number' && !isNaN(q.max)) v.max = q.max;
                if (q.unit) v.unit = q.unit;
                return Object.keys(v).length ? JSON.stringify(v) : null;
            }

            // Metadata clínica (SNOMED CT, único sistema soportado) para que la variable
            // sea interoperable fuera de ChequeAR. Se serializa en Question.CodingJson.
            var SNOMED_SYSTEM = 'http://snomed.info/sct';
            function buildCoding(q) {
                if (!q.code) return null;
                return JSON.stringify({ system: SNOMED_SYSTEM, code: q.code, display: q.codeDisplay || q.text });
            }

            // Manda el árbol completo (secciones -> preguntas -> opciones). El backend
            // bindea el enum Type por nombre (case-insensitive), así que mandamos el code
            // tal cual, y reemplaza la estructura guardada por ésta.
            function buildPayload() {
                var order = 0;
                var payloadGroups = groups.value.map(function (g, gi) {
                    return {
                        title: g.title,
                        orderIndex: gi,
                        questions: g.questions.map(function (q) {
                            return {
                                linkId: q.linkId,
                                text: q.text,
                                type: q.type,
                                required: !!q.required,
                                orderIndex: order++,
                                validationJson: buildValidation(q),
                                enableWhenJson: (q.enableWhen && q.enableWhen.question)
                                    ? JSON.stringify(q.enableWhen) : null,
                                codingJson: buildCoding(q),
                                options: (q.options || []).map(function (opt, oi) {
                                    return { value: opt, display: opt, code: opt, orderIndex: oi };
                                })
                            };
                        })
                    };
                });
                return {
                    title: qTitle.value,
                    specialtyCode: specialtyCode.value,
                    groups: payloadGroups
                };
            }

            // explicit = lo pidió el usuario (botón Guardar) -> confirmamos con un toast.
            // El autosave guarda en silencio: para eso está el indicador "Guardado hh:mm"
            // en el header. Los errores avisan siempre, vengan de donde vengan.
            function save(options) {
                var explicit = !!(options && options.explicit);
                if (saving.value) return Promise.resolve(false);
                // Con un conflicto sin resolver no se vuelve a intentar: reintentar sólo
                // serviría para pisar a la otra persona en cuanto refresquemos la versión.
                if (conflict.value) return Promise.resolve(false);
                var token = getToken();
                if (!token) {
                    toast('No hay sesión: no se puede guardar (token ausente).', 'error');
                    return Promise.resolve(false);
                }

                saving.value = true;
                var creating = isNew.value;
                var url = creating
                    ? '/fhir/r5/Questionnaire'
                    : '/fhir/r5/Questionnaire/' + currentId.value;

                var headers = {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer ' + token
                };
                // If-Match sólo al actualizar: al crear todavía no hay nada con qué chocar.
                if (!creating) headers['If-Match'] = 'W/"' + recordVersion.value + '"';

                // Devuelve una promesa que resuelve en true/false: publicar necesita saber
                // si el guardado previo salió bien antes de congelar una versión.
                return fetch(url, {
                    method: creating ? 'POST' : 'PUT',
                    headers: headers,
                    body: JSON.stringify(buildPayload())
                }).then(function (res) {
                    if (res.status === 409) {
                        return res.json().catch(function () { return null; }).then(function (body) {
                            var err = new Error((body && body.error) || 'Conflicto de edición');
                            err.isConflict = true;
                            throw err;
                        });
                    }
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
                    // El server devuelve la entidad con su ConcurrencyVersion nueva; sin
                    // esto el segundo guardado seguido chocaría contra nuestro propio cambio.
                    if (data) {
                        var v = data.concurrencyVersion;
                        if (v === undefined) v = data.ConcurrencyVersion;
                        if (typeof v === 'number') recordVersion.value = v;
                    }
                    lastSaved.value = new Date().toLocaleTimeString();
                    dirty.value = false;
                    if (creating) toast('Cuestionario creado', 'success');
                    else if (explicit) {
                        toast(isPublished.value
                            ? 'Cambios guardados. Publicá de nuevo para que lleguen a los clínicos.'
                            : 'Cambios guardados', 'success');
                    }
                    return true;
                }).catch(function (err) {
                    if (err.isConflict) {
                        // No se pisa nada: se corta acá y decide la persona.
                        conflict.value = true;
                        toast(err.message, 'error');
                    } else {
                        toast('Error al guardar: ' + err.message, 'error');
                    }
                    return false;
                }).then(function (ok) {
                    saving.value = false;
                    return ok;
                });
            }

            // Publicar desde el editor. Guarda primero: publicar congela lo que está en la
            // base, no lo que está en pantalla, así que sin esto publicarías la versión
            // anterior sin enterarte.
            // Descarta lo local y vuelve a cargar lo que hay en el server. Es la salida
            // honesta del conflicto: no hay merge automático de estructuras.
            function reloadFromServer() {
                dirty.value = false;   // que beforeunload no pregunte: la decisión ya se tomó
                window.location.reload();
            }

            function publish() {
                if (saving.value || publishing.value) return;
                if (conflict.value) {
                    toast('Resolvé el conflicto de edición antes de publicar.', 'error');
                    return;
                }
                var token = getToken();
                if (!token) { toast('No hay sesión: no se puede publicar (token ausente).', 'error'); return; }

                publishing.value = true;
                var pending = (dirty.value || isNew.value) ? save({ explicit: false }) : Promise.resolve(true);

                pending.then(function (ok) {
                    if (!ok) throw new Error('no se pudieron guardar los cambios');
                    return fetch('/fhir/r5/Questionnaire/' + currentId.value + '/$publish', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token },
                        body: '{}'
                    });
                }).then(function (res) {
                    if (res.ok) return null;
                    // El API devuelve 422 con {error} cuando la publicación no es válida.
                    return res.json().catch(function () { return null; }).then(function (body) {
                        throw new Error((body && body.error) || ('HTTP ' + res.status));
                    });
                }).then(function () {
                    dirty.value = false;
                    // Recargar: versión, estado y "cambios sin publicar" los calcula el server.
                    window.location.reload();
                }).catch(function (err) {
                    toast('No se pudo publicar: ' + err.message, 'error');
                    publishing.value = false;
                });
            }

            function saveExplicit() { save({ explicit: true }); }

            // Autosave (2s debounce) sólo para borradores ya persistidos:
            //  - uno nuevo se guarda a mano la primera vez, para no crear duplicados;
            //  - uno publicado no se autoguarda nunca (ver isPublished).
            var debounce;
            Vue.watch(groups, function () {
                dirty.value = true;
                if (isNew.value || isPublished.value || conflict.value) return;
                clearTimeout(debounce);
                debounce = setTimeout(save, 2000);
            }, { deep: true });

            // Sin autosave en publicados, salir de la página con cambios pendientes los
            // perdía en silencio. El navegador pide confirmación.
            function beforeUnload(e) {
                if (!dirty.value) return;
                e.preventDefault();
                e.returnValue = '';
                return '';
            }

            // Engancha los botones del header (viven fuera de la isla). Ojo: el listener
            // recibe el Event, así que va el wrapper y no save directamente.
            Vue.onMounted(function () {
                var btn = document.getElementById('saveBtn');
                if (btn) btn.addEventListener('click', saveExplicit);
                var pub = document.getElementById('publishBtn');
                if (pub) pub.addEventListener('click', publish);
                window.addEventListener('beforeunload', beforeUnload);
            });
            Vue.onUnmounted(function () {
                clearTimeout(debounce);
                var btn = document.getElementById('saveBtn');
                if (btn) btn.removeEventListener('click', saveExplicit);
                var pub = document.getElementById('publishBtn');
                if (pub) pub.removeEventListener('click', publish);
                window.removeEventListener('beforeunload', beforeUnload);
            });

            return {
                groups: groups, types: types, selected: selected,
                dirty: dirty, lastSaved: lastSaved, saving: saving, publishing: publishing,
                qTitle: qTitle, specialtyCode: specialtyCode, isNew: isNew,
                isPublished: isPublished, publish: publish,
                conflict: conflict, reloadFromServer: reloadFromServer,
                conditionSources: conditionSources, conditionSource: conditionSource,
                onConditionQuestion: onConditionQuestion, onConditionValue: onConditionValue,
                selectQuestion: selectQuestion, addGroup: addGroup, addQuestion: addQuestion,
                removeQuestion: removeQuestion, removeGroup: removeGroup, save: saveExplicit
            };
        },
        template: '\
<div>\
<div v-if="conflict" class="cq-card mb-2" style="border-left:3px solid var(--cq-danger);">\
    <div class="d-flex align-items-start gap-2">\
        <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-danger);"></i>\
        <div class="flex-fill">\
            <div class="cq-text-sm"><strong>Otra persona guardó cambios mientras editabas.</strong></div>\
            <div class="cq-text-sm cq-text-muted">Tus cambios NO se guardaron, y los de la otra persona siguen intactos. No hay forma automática de combinar las dos versiones, así que tenés que elegir: anotá lo tuyo y volvé a cargar, o abrí el cuestionario en otra pestaña para comparar.</div>\
            <button class="btn btn-sm btn-danger mt-2" @click="reloadFromServer"><i class="bi bi-arrow-clockwise me-1"></i> Descartar lo mío y recargar</button>\
        </div>\
    </div>\
</div>\
<div v-if="isPublished && !conflict" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-info);">\
    <i class="bi bi-info-circle" style="color:var(--cq-info);"></i>\
    <span class="cq-text-sm">Este cuestionario está <strong>publicado</strong>. Los cambios se guardan como pendientes: los clínicos siguen viendo la versión publicada hasta que publiques una nueva.</span>\
</div>\
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
            <span class="cq-text-xs" style="color:var(--cq-warning);" v-if="dirty">Cambios sin guardar</span>\
            <span class="cq-text-xs cq-text-muted" v-else-if="lastSaved">Guardado {{ lastSaved }}</span>\
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
            <div v-if="[\'integer\',\'decimal\',\'scale\'].indexOf(selected.question.type) >= 0">\
                <div class="row g-2 mb-2">\
                    <div class="col-6"><label class="form-label cq-text-sm">Min</label><input v-model.number="selected.question.min" type="number" class="form-control form-control-sm" /></div>\
                    <div class="col-6"><label class="form-label cq-text-sm">Max</label><input v-model.number="selected.question.max" type="number" class="form-control form-control-sm" /></div>\
                </div>\
                <div class="mb-2"><label class="form-label cq-text-sm">Unidad</label><input v-model="selected.question.unit" class="form-control form-control-sm" /></div>\
            </div>\
            <div v-if="[\'choice\',\'openChoice\'].indexOf(selected.question.type) >= 0">\
                <label class="form-label cq-text-sm">Opciones</label>\
                <textarea v-model="selected.question.optionsText" @input="selected.question.options = $event.target.value.split(\'\\n\').filter(Boolean)" class="form-control form-control-sm" rows="3" :placeholder="(selected.question.options||[]).join(\'\\n\')">{{ (selected.question.options || []).join(\'\\n\') }}</textarea>\
                <p class="cq-text-xs cq-text-muted mt-1">Una opción por línea.</p>\
            </div>\
            <div v-if="selected.question.type === \'attachment\'">\
                <label class="form-label cq-text-sm">Formatos permitidos</label>\
                <select v-model="selected.question.accept" class="form-select form-select-sm">\
                    <option :value="undefined">Imágenes y PDF</option>\
                    <option value="image/jpeg,image/png">Sólo imágenes</option>\
                    <option value="application/pdf">Sólo PDF</option>\
                </select>\
                <p class="cq-text-xs cq-text-muted mt-1">El servidor acepta JPG, PNG y PDF hasta 10 MB.</p>\
            </div>\
\
            <template v-if="conditionSources.length">\
            <hr />\
            <h4 class="cq-text-sm fw-semibold mb-2">Mostrar sólo si…</h4>\
            <select class="form-select form-select-sm mb-2"\
                    :value="selected.question.enableWhen ? selected.question.enableWhen.question : \'\'"\
                    @change="onConditionQuestion($event.target.value)">\
                <option value="">Siempre visible</option>\
                <option v-for="s in conditionSources" :key="s.linkId" :value="s.linkId">{{ s.text }}</option>\
            </select>\
            <div v-if="selected.question.enableWhen" class="row g-2">\
                <div class="col-4">\
                    <select v-model="selected.question.enableWhen.op" class="form-select form-select-sm">\
                        <option value="=">es igual a</option>\
                        <option value="!=">es distinto de</option>\
                        <option value="&gt;">es mayor que</option>\
                        <option value="&lt;">es menor que</option>\
                    </select>\
                </div>\
                <div class="col-8">\
                    <select v-if="conditionSource && conditionSource.type === \'boolean\'" class="form-select form-select-sm"\
                            :value="String(selected.question.enableWhen.value)" @change="onConditionValue($event.target.value)">\
                        <option value="true">Sí</option>\
                        <option value="false">No</option>\
                    </select>\
                    <select v-else-if="conditionSource && conditionSource.options && conditionSource.options.length" class="form-select form-select-sm"\
                            :value="selected.question.enableWhen.value" @change="onConditionValue($event.target.value)">\
                        <option v-for="opt in conditionSource.options" :key="opt" :value="opt">{{ opt }}</option>\
                    </select>\
                    <input v-else class="form-control form-control-sm" :value="selected.question.enableWhen.value"\
                           @input="onConditionValue($event.target.value)" placeholder="valor esperado" />\
                </div>\
            </div>\
            </template>\
\
            <hr />\
            <h4 class="cq-text-sm fw-semibold mb-2">Codificación clínica <span class="cq-text-xs cq-text-muted fw-normal">(opcional)</span></h4>\
            <div class="mb-2">\
                <label class="form-label cq-text-sm">Código SNOMED CT</label>\
                <input v-model="selected.question.code" class="form-control form-control-sm" placeholder="ej: 50373000" />\
            </div>\
\
            <hr />\
            <button class="btn btn-sm btn-outline-danger" @click="removeQuestion(selected.group, selected.question)"><i class="bi bi-trash me-1"></i> Eliminar pregunta</button>\
        </div>\
        <div v-else class="cq-card cq-text-sm cq-text-muted">Seleccioná una pregunta para editar sus propiedades.</div>\
    </div>\
</div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-editor', QuestionnaireEditor);
})();
