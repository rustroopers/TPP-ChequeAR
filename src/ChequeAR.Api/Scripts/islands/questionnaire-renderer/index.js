/*
 * Isla Vue: questionnaire-renderer (pantalla R3)
 * Renderiza un FHIR Questionnaire dinámicamente, evalúa enableWhen,
 * valida en línea y persiste como QuestionnaireResponse con autosave.
 */
(function () {
    var QuestionnaireRenderer = {
        props: { patientId: String, questionnaireId: String },
        setup: function (props) {
            var questionnaire = Vue.ref({ title: '', version: 0, groups: [] });
            var loading = Vue.ref(true);
            var loadError = Vue.ref(null);

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

            // QuestionType del backend (enum serializado como numero) -> tipo del renderer.
            var TYPES = ['decimal', 'integer', 'string', 'text', 'date', 'dateTime',
                         'boolean', 'choice', 'openChoice', 'attachment', 'scale'];
            function typeName(t) {
                if (typeof t === 'number') return TYPES[t] || 'string';
                return String(t || 'string').charAt(0).toLowerCase() + String(t || 'string').slice(1);
            }

            function parseJson(raw) {
                if (!raw) return null;
                try { return JSON.parse(raw); } catch (e) { return null; }
            }

            // El endpoint devuelve la entidad Questionnaire: Groups (opcional) y
            // Questions plana, cada una con GroupId. Se arma la jerarquia aca.
            function toViewModel(q) {
                var rawQuestions = q.Questions || q.questions || [];
                var rawGroups = q.Groups || q.groups || [];

                var questions = rawQuestions
                    .slice()
                    .sort(function (a, b) { return (a.OrderIndex || 0) - (b.OrderIndex || 0); })
                    .map(function (x) {
                        var validation = parseJson(x.ValidationJson) || {};
                        var enableWhen = parseJson(x.EnableWhenJson);
                        var options = (x.Options || x.options || []).map(function (o) {
                            return o.Display || o.Value || o.Code;
                        });
                        return {
                            linkId: x.LinkId,
                            groupId: x.GroupId || null,
                            text: x.Text,
                            type: typeName(x.Type),
                            required: !!x.Required,
                            unit: validation.unit,
                            min: validation.min,
                            max: validation.max,
                            options: options,
                            enableWhen: enableWhen
                        };
                    });

                var groups = rawGroups
                    .slice()
                    .sort(function (a, b) { return (a.OrderIndex || 0) - (b.OrderIndex || 0); })
                    .map(function (g) {
                        return {
                            linkId: g.LinkId || ('g-' + g.Id),
                            id: g.Id,
                            title: g.Title,
                            questions: questions.filter(function (x) { return x.groupId === g.Id; })
                        };
                    })
                    .filter(function (g) { return g.questions.length > 0; });

                // Preguntas sin grupo: van a un bloque unico al final.
                var ungrouped = questions.filter(function (x) {
                    return !x.groupId || !groups.some(function (g) { return g.id === x.groupId; });
                });
                if (ungrouped.length) {
                    groups.push({ linkId: 'sin-grupo', id: null, title: q.Title || q.title || 'Preguntas', questions: ungrouped });
                }

                return {
                    title: q.Title || q.title,
                    version: (q.Versions || q.versions || []).length,
                    groups: groups
                };
            }

            function load() {
                if (!props.questionnaireId) {
                    loading.value = false;
                    loadError.value = 'No se indicó qué cuestionario cargar.';
                    return;
                }
                fetch('/fhir/r5/Questionnaire/' + props.questionnaireId, { headers: authHeaders() })
                    .then(function (r) {
                        if (!r.ok) throw new Error('HTTP ' + r.status);
                        return r.json();
                    })
                    .then(function (q) {
                        questionnaire.value = toViewModel(q);
                    })
                    .catch(function (err) {
                        loadError.value = err.message;
                    })
                    .then(function () {
                        loading.value = false;
                    });
            }
            load();

            function isVisible(q) {
                if (!q.enableWhen) return true;
                var v = answers[q.enableWhen.question];
                if (q.enableWhen.op === '=') return v === q.enableWhen.value;
                return true;
            }

            function validate(q, value) {
                if (!isVisible(q)) { delete errors[q.linkId]; return; }
                if (q.required && (value === undefined || value === '' || value === null)) {
                    errors[q.linkId] = 'Requerido';
                    return;
                }
                if ((q.type === 'integer' || q.type === 'decimal') && value !== undefined && value !== '') {
                    var n = Number(value);
                    if (isNaN(n)) { errors[q.linkId] = 'Debe ser un número'; return; }
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
            function toItem(q) {
                var v = answers[q.linkId];
                var item = { LinkId: q.linkId, QuestionType: q.type };
                if (v === undefined || v === '' || v === null) return item;
                switch (q.type) {
                    case 'boolean':  item.ValueBoolean = !!v; break;
                    case 'integer':  item.ValueInteger = parseInt(v, 10); break;
                    case 'decimal':
                    case 'scale':    item.ValueDecimal = Number(v); break;
                    case 'date':
                    case 'dateTime': item.ValueDate = v; break;
                    case 'choice':
                    case 'openChoice': item.ValueCode = String(v); break;
                    default:         item.ValueString = String(v); break;
                }
                return item;
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
                        items.push(toItem(q));
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
                loading: loading, loadError: loadError,
                isVisible: isVisible, onChange: onChange,
                totalQuestions: totalQuestions, answeredCount: answeredCount,
                submit: submit
            };
        },
        template: '\
<div v-if="loading" class="cq-card">\
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
                            <input :type="q.type === \'integer\' ? \'number\' : \'number\'" :step="q.type === \'decimal\' ? \'0.1\' : \'1\'"\
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
                        <input v-else-if="q.type === \'date\'" type="date" class="form-control" style="max-width:240px;"\
                               :value="answers[q.linkId] || \'\'" @input="onChange(q, $event.target.value)" />\
\
                        <input v-else type="text" class="form-control" disabled placeholder="Tipo no soportado" />\
\
                        <div v-if="errors[q.linkId]" class="invalid-feedback d-block">{{ errors[q.linkId] }}</div>\
                    </div>\
                </template>\
            </section>\
\
            <div class="d-flex justify-content-between border-top pt-3 mt-3">\
                <button class="btn btn-outline-secondary"><i class="bi bi-save me-1"></i> Guardar borrador</button>\
                <button class="btn btn-primary" :disabled="submitting" @click="submit"><i class="bi bi-check2-circle me-1"></i> {{ submitting ? \'Guardando…\' : \'Finalizar y enviar\' }}</button>\
            </div>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-renderer', QuestionnaireRenderer);
})();
