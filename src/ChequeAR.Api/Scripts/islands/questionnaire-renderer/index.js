/*
 * Isla Vue: questionnaire-renderer (pantalla R3)
 * Renderiza un FHIR Questionnaire dinámicamente, evalúa enableWhen,
 * valida en línea y persiste como QuestionnaireResponse con autosave.
 */
(function () {
    var QuestionnaireRenderer = {
        props: { patientId: Number, questionnaireId: Number },
        setup: function (props) {
            // Mock del cuestionario — en producción se hace fetch a /api/fhir/r5/Questionnaire/{id}
            var questionnaire = Vue.ref({
                title: 'Cardio basal',
                version: 3,
                groups: [
                    {
                        linkId: 'gpaciente', title: 'Datos del paciente',
                        questions: [
                            { linkId: 'edad', text: 'Edad', type: 'integer', required: true, unit: 'años', min: 0, max: 120 },
                            { linkId: 'sexo', text: 'Sexo', type: 'choice', required: true, options: ['Femenino', 'Masculino', 'Otro'] }
                        ]
                    },
                    {
                        linkId: 'gvitales', title: 'Signos vitales',
                        questions: [
                            { linkId: 'tasis', text: 'TA sistólica', type: 'decimal', required: true, unit: 'mmHg', min: 40, max: 250 },
                            { linkId: 'tadia', text: 'TA diastólica', type: 'decimal', required: true, unit: 'mmHg', min: 20, max: 150 },
                            { linkId: 'fc',    text: 'FC',            type: 'integer', required: true, unit: 'lpm', min: 20, max: 240 }
                        ]
                    },
                    {
                        linkId: 'gantec', title: 'Antecedentes',
                        questions: [
                            { linkId: 'hta', text: '¿HTA?', type: 'boolean', required: true },
                            { linkId: 'htatto', text: 'Tratamiento actual de HTA', type: 'string', enableWhen: { question: 'hta', op: '=', value: true } },
                            { linkId: 'dm',  text: '¿Diabetes?', type: 'boolean', required: false }
                        ]
                    }
                ]
            });

            var answers = Vue.reactive({});
            var errors = Vue.reactive({});
            var savingState = Vue.ref('saved'); // idle | saving | saved | error

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

            var debounce;
            function triggerAutosave() {
                savingState.value = 'idle';
                clearTimeout(debounce);
                debounce = setTimeout(function () {
                    savingState.value = 'saving';
                    setTimeout(function () { savingState.value = 'saved'; }, 350); // simulado
                }, 2000);
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

            function submit() {
                var allErrors = [];
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        validate(q, answers[q.linkId]);
                        if (errors[q.linkId]) allErrors.push(q.text + ': ' + errors[q.linkId]);
                    });
                });
                if (allErrors.length) {
                    window.cqToast && window.cqToast('Hay ' + allErrors.length + ' campos con error', 'error');
                    return;
                }
                window.cqToast && window.cqToast('Ficha finalizada', 'success');
            }

            return {
                questionnaire: questionnaire, answers: answers, errors: errors,
                savingState: savingState,
                isVisible: isVisible, onChange: onChange,
                totalQuestions: totalQuestions, answeredCount: answeredCount,
                submit: submit
            };
        },
        template: '\
<div class="row g-3">\
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
                <span v-else-if="savingState === \'saved\'" style="color:var(--cq-success);"><i class="bi bi-cloud-check"></i> Borrador guardado</span>\
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
                <button class="btn btn-primary" @click="submit"><i class="bi bi-check2-circle me-1"></i> Finalizar y enviar</button>\
            </div>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-renderer', QuestionnaireRenderer);
})();
