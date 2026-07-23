/*
 * Isla Vue: questionnaire-renderer (pantalla R3)
 * Renderiza un FHIR Questionnaire dinámicamente, evalúa enableWhen,
 * valida en línea y persiste como QuestionnaireResponse con autosave.
 */
(function () {
    var QuestionnaireRenderer = {
        props: {
            patientId: String,
            questionnaireId: String,
            questionnaireVersionId: String,
            version: Number,
            title: String,
            groups: { type: Array, default: function () { return []; } }
        },
        setup: function (props) {
            // La estructura llega ya resuelta desde el server, leída de la VERSIÓN
            // CONGELADA. Antes se pedía la entidad viva a la API, así que una publicación
            // en el medio le cambiaba las preguntas al médico mientras completaba.
            var questionnaire = Vue.ref({
                title: props.title || '',
                version: props.version || 0,
                groups: (props.groups || []).map(function (g) {
                    return {
                        linkId: g.linkId,
                        title: g.title,
                        questions: (g.questions || []).map(function (q) {
                            return {
                                linkId: q.linkId,
                                text: q.text,
                                type: q.type || 'string',
                                required: !!q.required,
                                unit: q.unit,
                                min: q.min,
                                max: q.max,
                                accept: q.accept || 'image/jpeg,image/png,application/pdf',
                                options: q.options || [],
                                enableWhen: q.enableWhen || null
                            };
                        })
                    };
                }).filter(function (g) { return g.questions.length > 0; })
            });
            var loading = Vue.ref(false);
            var loadError = Vue.ref(
                (props.groups && props.groups.length) ? null : 'La versión publicada no tiene preguntas.');

            // Al finalizar, se marca ESTA entrada del historial (replaceState) antes de
            // redirigir a la ficha. Si la persona vuelve con "atrás", cae de nuevo acá y
            // la marca sigue puesta: mostramos el aviso en vez de un formulario en blanco.
            // La marca vive en la entrada del historial, así que sobrevive a la recarga
            // que fuerza el Cache-Control: no-store del server (no depende del bfcache ni
            // del tipo de navegación, que con no-store el navegador reporta como "navigate").
            // Entrando de nuevo desde Pacientes se crea una entrada NUEVA sin marca, así
            // que un "Seguimiento" real se puede cargar las veces que haga falta.
            function finalizedMark() {
                try {
                    var s = window.history.state;
                    return (s && s.cqFinalized) ? s : null;
                } catch (e) { return null; }
            }
            var blockedByBack = Vue.ref(false);
            var completedResponseId = Vue.ref(null);
            function applyFinalizedMark() {
                var m = finalizedMark();
                if (m) { blockedByBack.value = true; completedResponseId.value = m.responseId; }
            }
            // Caso recarga real: si el navegador respeta no-store, "atrás" re-ejecuta
            // setup() con el formulario vacío y la marca del historial todavía puesta.
            applyFinalizedMark();

            // Caso bfcache: los navegadores actuales (Chromium/Brave) conservan la página
            // VIVA en memoria aunque el server mande Cache-Control: no-store, así que al
            // volver con "atrás" setup() NO se re-ejecuta y el chequeo de arriba no corre.
            // pageshow sí dispara en esa restauración (los listeners sobreviven al
            // congelado del bfcache): ahí re-evaluamos la marca y bloqueamos el formulario.
            function onPageShow() { applyFinalizedMark(); }
            window.addEventListener('pageshow', onPageShow);
            Vue.onUnmounted(function () { window.removeEventListener('pageshow', onPageShow); });

            var answers = Vue.reactive({});
            var errors = Vue.reactive({});
            var savingState = Vue.ref('idle'); // idle | dirty | saving | error
            var submitting = Vue.ref(false);

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


            // --- Evaluación de condiciones (enableWhen) ---------------------------------
            // La condición referencia otra pregunta y un valor esperado. Los dos lados
            // pueden no tener la misma forma que un === estricto necesita, y cada
            // desajuste dejaba la pregunta dependiente invisible para siempre:
            //  - la respuesta de una opción múltiple es un array, no un valor suelto;
            //  - los inputs de texto devuelven strings ("5") contra un valor esperado que
            //    el recurso FHIR guarda tipado (answerInteger -> 5).
            // Por eso la comparación se normaliza acá en vez de en cada operador.

            /// Un valor del formulario contra el valor esperado de la condición.
            function sameAnswer(actual, expected) {
                if (actual === expected) return true;
                if (typeof expected === 'number') {
                    var n = parseNumeric(actual);
                    return !isNaN(n) && n === expected;
                }
                return false;
            }

            function compareAnswer(op, actual, expected) {
                switch (op) {
                    case '=':  return sameAnswer(actual, expected);
                    case '!=': return !sameAnswer(actual, expected);
                    // Comparaciones numéricas: si todavía no hay respuesta, la condición no
                    // se cumple (y la pregunta queda oculta) en vez de comparar contra NaN.
                    case '>':  return actual !== undefined && actual !== '' && parseNumeric(actual) > Number(expected);
                    case '<':  return actual !== undefined && actual !== '' && parseNumeric(actual) < Number(expected);
                    default:   return true;
                }
            }

            function isVisible(q) {
                if (!q.enableWhen) return true;
                var actual = answers[q.enableWhen.question];
                var expected = q.enableWhen.value;

                // FHIR R5: cuando la pregunta origen admite varias respuestas, la condición
                // se cumple si ALGUNA de ellas la satisface. Un array sin nada marcado no
                // satisface ninguna condición: equivale a no haber respondido.
                if (Array.isArray(actual)) {
                    return actual.some(function (one) {
                        return compareAnswer(q.enableWhen.op, one, expected);
                    });
                }
                return compareAnswer(q.enableWhen.op, actual, expected);
            }

            // Adjuntos que fallaron al subir, por linkId. Van aparte de `errors` porque
            // validate() reescribe ese mapa entero: como la pregunta queda sin respuesta,
            // el submit borraba el error y finalizaba la ficha sin el archivo.
            var uploadErrors = Vue.reactive({});

            // En es-AR el separador decimal es la coma. El input de decimales es de texto
            // justamente para que la coma llegue hasta acá (un input[type=number] la
            // descarta y entrega '', sin forma de saber qué se había tipeado).
            function parseNumeric(value) {
                if (value === undefined || value === null) return NaN;
                var raw = String(value).trim().replace(',', '.');
                if (raw === '') return NaN;
                return Number(raw);
            }

            function validate(q, value) {
                if (!isVisible(q)) { delete errors[q.linkId]; return; }
                // Una subida fallida bloquea antes que cualquier otra regla: la pregunta
                // figura vacía, pero el problema no es que falte responder.
                if (uploadErrors[q.linkId]) { errors[q.linkId] = uploadErrors[q.linkId]; return; }
                // Una opción múltiple sin nada tildado llega como [], que no es '' ni null:
                // sin este caso, una pregunta obligatoria vacía pasaba como respondida.
                var vacio = value === undefined || value === '' || value === null
                         || (Array.isArray(value) && value.length === 0);
                if (q.required && vacio) {
                    errors[q.linkId] = 'Requerido';
                    return;
                }
                if ((q.type === 'integer' || q.type === 'decimal') && value !== undefined && value !== '') {
                    var n = parseNumeric(value);
                    if (isNaN(n)) { errors[q.linkId] = 'Debe ser un número'; return; }
                    // parseInt() truncaba 1,8 a 1 sin avisar. Mejor pedir el valor entero.
                    if (q.type === 'integer' && n !== Math.trunc(n)) { errors[q.linkId] = 'Debe ser un número entero'; return; }
                    if (q.min !== undefined && n < q.min) { errors[q.linkId] = 'Mín ' + q.min; return; }
                    if (q.max !== undefined && n > q.max) { errors[q.linkId] = 'Máx ' + q.max; return; }
                }
                delete errors[q.linkId];
            }

            function onChange(q, value) {
                answers[q.linkId] = value;
                validate(q, value);
                // re-validar campos dependientes
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (qq) {
                        if (qq.enableWhen && qq.enableWhen.question === q.linkId) validate(qq, answers[qq.linkId]);
                    });
                });
                triggerAutosave();
            }

            // Adjuntos: se sube primero contra /api/attachments (que valida tipo y tamaño)
            // y la respuesta guarda el id devuelto, no el archivo.
            var uploading = Vue.reactive({});
            function onFile(q, file) {
                if (!file) return;
                if (!token()) { window.cqToast && window.cqToast('No hay sesión: no se puede subir.', 'error'); return; }
                // Elegir otro archivo es el reintento: se limpia el bloqueo anterior.
                delete uploadErrors[q.linkId];
                delete errors[q.linkId];
                uploading[q.linkId] = true;
                var form = new FormData();
                form.append('file', file, file.name);
                fetch('/api/attachments', {
                    method: 'POST',
                    headers: { 'Authorization': 'Bearer ' + token() },   // sin Content-Type: lo pone FormData con su boundary
                    body: form
                }).then(function (r) {
                    if (r.status === 415) throw new Error('formato no permitido');
                    if (r.status === 413) throw new Error('el archivo supera el máximo permitido');
                    if (!r.ok) throw new Error('HTTP ' + r.status);
                    return r.json();
                }).then(function (att) {
                    onChange(q, { attachmentId: att.id, fileName: file.name, size: att.size });
                }).catch(function (err) {
                    uploadErrors[q.linkId] = 'No se pudo subir: ' + err.message + '. Elegí el archivo de nuevo.';
                    errors[q.linkId] = uploadErrors[q.linkId];
                }).then(function () {
                    uploading[q.linkId] = false;
                });
            }

            function clearFile(q) {
                delete uploadErrors[q.linkId];
                onChange(q, '');
                delete errors[q.linkId];
            }

            // Opción múltiple: la respuesta es un array de opciones marcadas.
            function onToggleMulti(q, option, checked) {
                var current = (answers[q.linkId] || []).slice();
                var i = current.indexOf(option);
                if (checked && i < 0) current.push(option);
                if (!checked && i >= 0) current.splice(i, 1);
                onChange(q, current);
            }

            // El backend todavia no expone un endpoint de update para
            // QuestionnaireResponse (solo POST y $finalize), asi que no hay
            // autosave posible. Se marca el estado como 'dirty' para que la UI
            // diga la verdad: hay cambios sin guardar hasta que se finalice.
            function triggerAutosave() {
                savingState.value = 'dirty';
            }

            function totalQuestions() {
                var n = 0;
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) { if (isVisible(q)) n++; });
                });
                return n;
            }
            function answeredCount() {
                var n = 0;
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (isVisible(q) && answers[q.linkId] !== undefined && answers[q.linkId] !== '' && answers[q.linkId] !== null) n++;
                    });
                });
                return n;
            }

            // Un valor del formulario -> la columna tipada que espera
            // QuestionnaireResponseItem en el backend.
            // Devuelve una LISTA porque una opción múltiple genera un item por opción
            // marcada (linkId repetido), que es como FHIR modela las respuestas repetidas.
            function toItems(q) {
                var v = answers[q.linkId];
                var base = function () { return { LinkId: q.linkId, QuestionType: q.type }; };
                if (v === undefined || v === '' || v === null) return [base()];

                if (q.type === 'openChoice') {
                    var selectedOpts = Array.isArray(v) ? v : [v];
                    if (!selectedOpts.length) return [base()];
                    return selectedOpts.map(function (opt) {
                        var it = base();
                        it.ValueCode = String(opt);
                        return it;
                    });
                }

                var item = base();
                switch (q.type) {
                    case 'boolean':  item.ValueBoolean = !!v; break;
                    case 'integer':  item.ValueInteger = parseNumeric(v); break;
                    case 'decimal':
                    case 'scale':    item.ValueDecimal = parseNumeric(v); break;
                    case 'date':
                    case 'dateTime': item.ValueDate = v; break;
                    case 'choice':   item.ValueCode = String(v); break;
                    // El adjunto se guarda por referencia: el archivo ya está subido.
                    case 'attachment':
                        item.AttachmentId = v.attachmentId;
                        item.ValueString = v.fileName;
                        break;
                    default:         item.ValueString = String(v); break;
                }
                return [item];
            }

            function submit() {
                if (submitting.value) return;

                var allErrors = [];
                var items = [];
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (!isVisible(q)) return;
                        validate(q, answers[q.linkId]);
                        if (errors[q.linkId]) allErrors.push(q.text + ': ' + errors[q.linkId]);
                        toItems(q).forEach(function (it) { items.push(it); });
                    });
                });
                if (allErrors.length) {
                    window.cqToast && window.cqToast('Hay ' + allErrors.length + ' campos con error', 'error');
                    return;
                }
                if (!token()) {
                    window.cqToast && window.cqToast('No hay sesión: no se puede guardar la ficha.', 'error');
                    return;
                }

                submitting.value = true;
                savingState.value = 'saving';

                // Se crea la respuesta y recien despues se finaliza: son dos
                // endpoints distintos y $finalize necesita el id.
                fetch('/fhir/r5/QuestionnaireResponse', {
                    method: 'POST',
                    headers: authHeaders(),
                    body: JSON.stringify({
                        QuestionnaireId: props.questionnaireId,
                        // Ancla la ficha a la version que se rendrizo, no a la que este
                        // publicada al momento de guardar.
                        QuestionnaireVersionId: props.questionnaireVersionId,
                        PatientHospitalId: props.patientId,
                        Items: items
                    })
                }).then(function (r) {
                    if (!r.ok) throw new Error('HTTP ' + r.status);
                    return r.json();
                }).then(function (created) {
                    var id = created.Id || created.id;
                    return fetch('/fhir/r5/QuestionnaireResponse/' + id + '/$finalize', {
                        method: 'POST',
                        headers: authHeaders()
                    }).then(function (r) {
                        if (!r.ok) throw new Error('HTTP ' + r.status + ' al finalizar');
                        return id;
                    });
                }).then(function (id) {
                    savingState.value = 'idle';
                    window.cqToast && window.cqToast('Ficha finalizada', 'success');
                    // Marca esta entrada del historial ANTES de redirigir: si la persona
                    // vuelve con "atrás", el renderer la lee y muestra el aviso en vez de
                    // dejar re-finalizar. Se guarda el id para poder linkear a la ficha.
                    try {
                        window.history.replaceState(
                            { cqFinalized: true, responseId: id }, '', window.location.href);
                    } catch (e) { /* history bloqueado: el aviso no aparecerá, pero la ficha ya se guardó */ }
                    window.location.href = '/Encounters/View/' + id;
                }).catch(function (err) {
                    savingState.value = 'error';
                    window.cqToast && window.cqToast('No se pudo guardar la ficha: ' + err.message, 'error');
                }).then(function () {
                    submitting.value = false;
                });
            }

            return {
                questionnaire: questionnaire, answers: answers, errors: errors,
                savingState: savingState, submitting: submitting,
                loading: loading, loadError: loadError, blockedByBack: blockedByBack,
                completedResponseId: completedResponseId,
                isVisible: isVisible, onChange: onChange, onToggleMulti: onToggleMulti,
                onFile: onFile, clearFile: clearFile, uploading: uploading,
                totalQuestions: totalQuestions, answeredCount: answeredCount,
                submit: submit
            };
        },
        template: '\
<div v-if="blockedByBack" class="cq-card">\
    <div class="cq-empty">\
        <div class="cq-empty__icon"><i class="bi bi-check2-circle"></i></div>\
        <div class="cq-empty__title">Ya cargaste esta ficha</div>\
        <div class="cq-empty__desc">La finalizaste recién. Si necesitás cargar otra, entrá de nuevo desde Pacientes.</div>\
        <a v-if="completedResponseId" :href="\'/Encounters/View/\' + completedResponseId" class="btn btn-outline-secondary">Ver ficha cargada</a>\
    </div>\
</div>\
<div v-else-if="loading" class="cq-card">\
    <div class="cq-loading"><div class="cq-spinner cq-spinner--lg"></div>\
        <div class="cq-loading__label">Cargando cuestionario…</div>\
    </div>\
</div>\
<div v-else-if="loadError" class="alert alert-danger" role="alert">\
    <strong>No se pudo cargar el cuestionario.</strong>\
    <div class="small">{{ loadError }}</div>\
</div>\
<div v-else class="row g-3">\
    <aside class="col-lg-3">\
        <div class="cq-card" style="position:sticky; top:84px;">\
            <h3 class="cq-section-title" style="margin-top:0;">Índice</h3>\
            <ul class="list-unstyled mb-3">\
                <li v-for="g in questionnaire.groups" :key="g.linkId" class="py-1">\
                    <a :href="\'#\' + g.linkId" class="cq-text-sm text-decoration-none">{{ g.title }}</a>\
                </li>\
            </ul>\
            <div class="cq-text-xs cq-text-muted">{{ answeredCount() }} / {{ totalQuestions() }} respondidas</div>\
            <div class="progress mt-2" style="height:6px;">\
                <div class="progress-bar" :style="{ width: (totalQuestions() ? (answeredCount() * 100 / totalQuestions()) : 0) + \'%\', background: \'var(--cq-primary)\' }"></div>\
            </div>\
            <p class="cq-text-xs mt-3 mb-0">\
                <span v-if="savingState === \'saving\'" class="cq-text-muted"><i class="bi bi-cloud-arrow-up"></i> Guardando…</span>\
                <span v-else-if="savingState === \'dirty\'" class="cq-text-muted"><i class="bi bi-pencil"></i> Cambios sin guardar</span>\
                <span v-else-if="savingState === \'error\'" class="text-danger"><i class="bi bi-exclamation-triangle"></i> No se pudo guardar</span>\
            </p>\
        </div>\
    </aside>\
\
    <div class="col-lg-9">\
        <div class="cq-card">\
            <section v-for="g in questionnaire.groups" :key="g.linkId" :id="g.linkId" class="mb-4">\
                <h2 class="cq-section-title" style="margin-top:0;">{{ g.title }}</h2>\
                <template v-for="q in g.questions" :key="q.linkId">\
                    <div v-if="isVisible(q)" class="mb-3">\
                        <label class="form-label">{{ q.text }}<span v-if="q.required" class="text-danger" aria-label="obligatorio"> *</span></label>\
\
                        <input v-if="q.type === \'string\'" type="text" class="form-control" :class="{ \'is-invalid\': errors[q.linkId] }"\
                               :value="answers[q.linkId] || \'\'" @input="onChange(q, $event.target.value)" />\
\
                        <div v-else-if="q.type === \'integer\' || q.type === \'decimal\'" class="input-group" style="max-width:260px;">\
                            <input :type="q.type === \'integer\' ? \'number\' : \'text\'"\
                                   :inputmode="q.type === \'integer\' ? \'numeric\' : \'decimal\'"\
                                   :step="q.type === \'integer\' ? \'1\' : null"\
                                   class="form-control" :class="{ \'is-invalid\': errors[q.linkId] }"\
                                   :value="answers[q.linkId] || \'\'" @input="onChange(q, $event.target.value)" />\
                            <span v-if="q.unit" class="input-group-text">{{ q.unit }}</span>\
                        </div>\
\
                        <div v-else-if="q.type === \'boolean\'" class="btn-group" role="group">\
                            <input :id="q.linkId + \'_y\'" type="radio" class="btn-check" :name="q.linkId" :checked="answers[q.linkId] === true" @change="onChange(q, true)" />\
                            <label class="btn btn-outline-secondary" :for="q.linkId + \'_y\'">Sí</label>\
                            <input :id="q.linkId + \'_n\'" type="radio" class="btn-check" :name="q.linkId" :checked="answers[q.linkId] === false" @change="onChange(q, false)" />\
                            <label class="btn btn-outline-secondary" :for="q.linkId + \'_n\'">No</label>\
                        </div>\
\
                        <select v-else-if="q.type === \'choice\'" class="form-select" style="max-width:340px;" :class="{ \'is-invalid\': errors[q.linkId] }"\
                                :value="answers[q.linkId] || \'\'" @change="onChange(q, $event.target.value)">\
                            <option value="">Seleccioná…</option>\
                            <option v-for="opt in q.options" :key="opt" :value="opt">{{ opt }}</option>\
                        </select>\
\
                        <textarea v-else-if="q.type === \'text\'" rows="4" class="form-control" :class="{ \'is-invalid\': errors[q.linkId] }"\
                                  :value="answers[q.linkId] || \'\'" @input="onChange(q, $event.target.value)"></textarea>\
\
                        <div v-else-if="q.type === \'scale\'" class="d-flex align-items-center gap-3" style="max-width:360px;">\
                            <input type="range" class="form-range" :min="q.min !== undefined ? q.min : 0" :max="q.max !== undefined ? q.max : 10"\
                                   :value="answers[q.linkId] !== undefined && answers[q.linkId] !== \'\' ? answers[q.linkId] : (q.min !== undefined ? q.min : 0)"\
                                   @input="onChange(q, Number($event.target.value))" />\
                            <span class="cq-text-sm fw-semibold" style="min-width:2.5rem; text-align:right;">{{ answers[q.linkId] !== undefined && answers[q.linkId] !== \'\' ? answers[q.linkId] : \'—\' }}</span>\
                        </div>\
\
                        <div v-else-if="q.type === \'openChoice\'" :class="{ \'is-invalid\': errors[q.linkId] }">\
                            <div v-for="opt in q.options" :key="opt" class="form-check">\
                                <input class="form-check-input" type="checkbox" :id="q.linkId + \'_\' + opt"\
                                       :checked="(answers[q.linkId] || []).indexOf(opt) >= 0" @change="onToggleMulti(q, opt, $event.target.checked)" />\
                                <label class="form-check-label cq-text-sm" :for="q.linkId + \'_\' + opt">{{ opt }}</label>\
                            </div>\
                        </div>\
\
                        <input v-else-if="q.type === \'date\'" type="date" class="form-control" style="max-width:240px;"\
                               :value="answers[q.linkId] || \'\'" @input="onChange(q, $event.target.value)" />\
\
                        <input v-else-if="q.type === \'dateTime\'" type="datetime-local" class="form-control" style="max-width:260px;"\
                               :value="answers[q.linkId] || \'\'" @input="onChange(q, $event.target.value)" />\
\
                        <div v-else-if="q.type === \'attachment\'">\
                            <div v-if="answers[q.linkId] && answers[q.linkId].attachmentId" class="d-flex align-items-center gap-2">\
                                <i class="bi bi-paperclip"></i>\
                                <span class="cq-text-sm">{{ answers[q.linkId].fileName }}</span>\
                                <button class="btn btn-sm btn-link text-danger" @click="clearFile(q)">Quitar</button>\
                            </div>\
                            <div v-else class="d-flex align-items-center gap-2">\
                                <input type="file" class="form-control" style="max-width:340px;" :accept="q.accept"\
                                       :disabled="uploading[q.linkId]" @change="onFile(q, $event.target.files[0])" />\
                                <span v-if="uploading[q.linkId]" class="cq-spinner"></span>\
                            </div>\
                        </div>\
\
                        <input v-else type="text" class="form-control" disabled placeholder="Tipo no soportado" />\
\
                        <div v-if="errors[q.linkId]" class="invalid-feedback d-block">{{ errors[q.linkId] }}</div>\
                    </div>\
                </template>\
            </section>\
\
            <div class="d-flex justify-content-end border-top pt-3 mt-3">\
                <button class="btn btn-primary" :disabled="submitting" @click="submit"><i class="bi bi-check2-circle me-1"></i> {{ submitting ? \'Guardando…\' : \'Finalizar y enviar\' }}</button>\
            </div>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-renderer', QuestionnaireRenderer);
})();
