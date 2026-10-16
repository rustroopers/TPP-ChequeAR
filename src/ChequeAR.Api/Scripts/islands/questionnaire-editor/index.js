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
            // Cambios guardados en una sesión anterior y todavía sin publicar. Lo calcula
            // el server; sin declararlo acá Vue lo deja en $attrs y llega undefined, que es
            // como se perdía el permiso para publicar al reabrir el editor.
            hasPendingChanges: Boolean,
            concurrencyVersion: Number,
            // Codificación clínica del cuestionario completo (opcional): término de autoría +
            // concepto SNOMED. Es el análogo a nivel cuestionario de la que ya existía por
            // pregunta; los códigos de cada pregunta siguen siendo suyos y no se tocan.
            code: String,
            codeDisplay: String,
            term: String,
            groups: { type: Array, default: function () { return []; } },
            // Declarado (como hasPendingChanges): sin esto Vue lo deja en $attrs y
            // props.canEdit llega siempre undefined. Default true porque una prop Boolean
            // ausente vale false, y el server siempre la manda.
            canEdit: { type: Boolean, default: true },
            // linkIds de versiones publicadas: no se proponen para preguntas nuevas.
            publishedLinkIds: { type: Array, default: function () { return []; } }
        },
        setup: function (props) {
            // Estado de persistencia. Para un cuestionario nuevo el id del controller es
            // sólo provisorio: el backend asigna uno propio al crear (POST). A partir de
            // ahí pasamos a modo edición (PUT) usando el id devuelto.
            var currentId = Vue.ref(props.id);
            var isNew = Vue.ref(!!props.isNew);
            // En "nuevo" arranca vacío para forzar al usuario a titular explícitamente:
            // isStepDone(0) exige título no-vacío, así la timeline bloquea Siguiente.
            var qTitle = Vue.ref(props.title || '');
            // Marca si la persona ya pasó por el campo (blur): antes de eso no tuvo
            // oportunidad de escribir nada, así que mostrar el error de "obligatorio" ni bien
            // se entra a la pantalla es ruido, no ayuda. Un título existente nunca dispara el
            // error igual, entre por acá o no.
            var qTitleTouched = Vue.ref(false);
            var specialtyCode = Vue.ref(props.specialtyCode || '');
            var specialties = Vue.ref([]);
            var specialtiesLoading = Vue.ref(false);
            var specialtiesError = Vue.ref(null);
            // Codificación del cuestionario completo. Objeto reactivo (no tres refs sueltos)
            // para que el mismo buscador SNOMED que usan las preguntas pueda escribir aquí
            // code/codeDisplay tratándolo igual que a una pregunta.
            var questionnaireCoding = Vue.reactive({
                term: props.term || '',
                code: props.code || undefined,
                codeDisplay: props.codeDisplay || undefined
            });
            var saving = Vue.ref(false);
            var publishing = Vue.ref(false);
            // Un publicado no se autoguarda: sus cambios quedan pendientes hasta que
            // alguien decide publicar la versión siguiente. Guardar es una decisión.
            var isPublished = Vue.ref(props.status === 'published');
            // Si ya hay cambios guardados sin publicar de una sesión anterior (el server
            // lo calcula comparando con la versión publicada). "dirty" sólo sabe de esta
            // sesión, así que publish() necesita este flag además para no bloquear una
            // publicación legítima cuando recién se abre la página.
            var hasPendingChanges = Vue.ref(!!props.hasPendingChanges);
            // Un archivado no se puede editar: el backend lo rechaza (Retired questionnaires
            // cannot be edited). Sin esto el editor lo trataba como un borrador cualquiera
            // -autosave incluido- y cada guardado terminaba en un 500 silencioso.
            var isArchived = Vue.ref(props.status === 'archived');
            // El rol Medico ve cuestionarios (para saber qué hay disponible) pero no los
            // edita (ver RolePolicy: sólo Administrador/Jefe tienen questionnaire.write). El
            // server ya no renderiza los botones Guardar/Publicar en ese caso (ver
            // Views/Questionnaires/Edit.cshtml); esto es la segunda barrera, para las
            // acciones que no dependen de esos botones (autosave, importar preguntas).
            var canEdit = props.canEdit;
            var readOnly = Vue.computed(function () { return isArchived.value || !canEdit; });
            // Versión del registro que este editor cargó. Viaja en If-Match y se refresca
            // con cada guardado propio. Si otra persona guarda, la suya avanza y la nuestra
            // queda vieja -> el server responde 409 en vez de dejarnos pisar su trabajo.
            var recordVersion = Vue.ref(props.concurrencyVersion || 0);
            var conflict = Vue.ref(false);

            // Identidad estable de una sección. Antes el linkId salía de la posición al guardar
            // ("seccion-N"): reordenar lo cambiaba, y el merge de conflictos no tenía cómo saber
            // qué sección era cuál entre la versión propia y la de la otra persona. Una sección
            // existente conserva el que le dio el server; una nueva recibe uno al crearse.
            // `reservados` cubre los ya repartidos que todavía no están en el árbol.
            function newSectionLinkId(reservados) {
                var usados = {};
                Object.keys(reservados || {}).forEach(function (l) { usados[l] = true; });
                ((groups && groups.value) || []).forEach(function (g) {
                    usados[g.linkId] = true;
                    g.questions.forEach(function (q) { usados[q.linkId] = true; });
                });
                var id;
                do { id = 'seccion-' + Math.random().toString(36).slice(2, 8); } while (usados[id]);
                if (reservados) reservados[id] = true;
                return id;
            }

            // Estructura real del cuestionario, inyectada por el server en data-props.
            // Uno nuevo arranca con una sección vacía (no con datos de ejemplo: se
            // guardaban como si fueran del usuario). El linkId se completa ANTES de copiar
            // la base del merge, para que las dos fotos hablen de las mismas secciones.
            var seedGroups = (props.groups || []).map(function (g) {
                return Object.assign({}, g, { linkId: g.linkId || newSectionLinkId() });
            });
            var initial = seedGroups.map(function (g) {
                return {
                    linkId: g.linkId,
                    // Coerción a string: si el server manda una sección sin title,
                    // el template hace `!g.title.trim()` y sin default explota con
                    // TypeError; en Vue prod eso deja la isla en blanco sin errores.
                    title: g.title || '',
                    questions: (g.questions || []).map(function (q) {
                        return {
                            linkId: q.linkId,
                            text: q.text,
                            type: q.type,
                            required: !!q.required,
                            unit: q.unit,
                            min: typeof q.min === 'number' ? q.min : undefined,
                            max: typeof q.max === 'number' ? q.max : undefined,
                            maxLength: typeof q.maxLength === 'number' ? q.maxLength : undefined,
                            accept: q.accept,
                            enableWhen: q.enableWhen ? {
                                question: q.enableWhen.question,
                                op: q.enableWhen.op || '=',
                                value: q.enableWhen.value
                            } : undefined,
                            code: q.code,
                            codeDisplay: q.codeDisplay,
                            term: q.term,
                            options: q.options && q.options.length ? q.options.slice() : undefined,
                            // Origen de la pregunta. variableRevision es la revision con la que
                            // se armo; compararla contra variableCurrentRevision es lo que
                            // permite avisar que el Administrador edito la variable despues.
                            variableId: q.variableId,
                            variableName: q.variableName,
                            variableRevision: q.variableRevision,
                            variableCurrentRevision: q.variableCurrentRevision
                        };
                    })
                };
            });
            if (!initial.length) initial = [{ linkId: newSectionLinkId(), title: '', questions: [] }];
            var groups = Vue.ref(initial);

            // BASE del merge de 3 vías cuando otra persona guarda en el medio: lo último que este
            // editor sabe que está en el server. Arranca en lo cargado y se re-ancla en cada
            // guardado propio exitoso y después de cada merge; si no, un cambio propio ya
            // guardado se confundiría con uno pendiente (y, por ejemplo, una pregunta que yo
            // agregué y guardé y la otra persona borró después reaparecería).
            var baseGroups = JSON.parse(JSON.stringify(seedGroups));
            // Lo mismo para la cabecera (título, especialidad, codificación del cuestionario).
            var baseHeader = {
                title: props.title || '',
                specialty: props.specialtyCode || '',
                code: props.code || null,
                codeDisplay: props.codeDisplay || null,
                term: props.term || null
            };

            // Estado del merge de conflictos (ver beginMerge/finishMerge más abajo).
            var merging = Vue.ref(false);
            var mergePlanAll = Vue.ref([]);     // plan completo (autos + conflictos)
            var mergeAutoCount = Vue.ref(0);
            var pendingTheirs = null;           // { concurrencyVersion, groups } de la otra persona
            var conflictEntries = Vue.computed(function () {
                return mergePlanAll.value.filter(function (e) { return e.needsChoice; });
            });

            // Solo para MOSTRAR de que tipo es una pregunta. Desde el catalogo de variables
            // el tipo ya no se elige aca: lo define el Administrador al cargar la variable, y
            // el panel de propiedades lo muestra con candado.
            var types = [
                { code: 'string',     label: 'Texto corto',     icon: 'bi-fonts' },
                { code: 'text',       label: 'Texto largo',     icon: 'bi-textarea-t' },
                // "Escala" vuelve a la lista: cuando ésta era el menú de tipos creables se la
                // excluía a propósito (es lo mismo que un Número con Min/Max), pero ahora sólo
                // sirve para MOSTRAR el tipo -y una variable de escala del catálogo apareciía
                // como "scale" crudo en el selector y en el panel de propiedades-.
                { code: 'scale',      label: 'Escala',          icon: 'bi-sliders' },
                { code: 'integer',    label: 'Número',          icon: 'bi-123' },
                { code: 'decimal',    label: 'Decimal',         icon: 'bi-percent' },
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

            // El panel de propiedades MUESTRA el tipo; ya no lo deja elegir.
            function typeLabel(code) {
                for (var i = 0; i < types.length; i++) if (types[i].code === code) return types[i].label;
                return code || '-';
            }

            // --- Wizard (5 pasos) ----------------------------------------------------
            // Meta / Estructura / Códigos / Vista previa / Publicación.
            // Ahora el stepper SÍ es candado: no se puede saltar a un paso si el
            // anterior no está completo. Cada paso define qué significa "listo" en
            // `isDone(i)` y `stepReachable(i)` compone la cadena.
            // La codificación SNOMED se resuelve automáticamente desde el panel
            // Propiedades del paso Estructura (verificación on-blur/on-input contra
            // Snowstorm). No hay un paso dedicado: publicar sin códigos es válido.
            var STEPS = [
                { key: 'meta',      label: 'Metadatos',    icon: 'bi-info-circle',
                  help: 'Ponele un título que un colega reconozca desde el listado. La especialidad determina bajo qué grupo aparece en los reportes.' },
                { key: 'structure', label: 'Estructura',   icon: 'bi-diagram-3',
                  help: 'Agregá secciones y elegí variables del catálogo. De cada una podés cambiar el enunciado y si es obligatoria; el tipo, la unidad, el rango y el código los define el Administrador. Podés condicionar preguntas a otras anteriores.' },
                { key: 'preview',   label: 'Vista previa', icon: 'bi-eye',
                  help: 'Así ven la ficha los clínicos cuando cargan una respuesta. Si una pregunta no aparece, revisá sus condiciones.' },
                { key: 'publish',   label: 'Publicación',  icon: 'bi-send',
                  help: 'Publicar congela una versión: los clínicos ven exactamente esta estructura. Guardá los cambios antes.' }
            ];
            var step = Vue.ref(props.isNew ? 'meta' : 'structure');
            var stepIndex = Vue.computed(function () {
                for (var i = 0; i < STEPS.length; i++) if (STEPS[i].key === step.value) return i;
                return 0;
            });
            // Vista previa no tiene condición objetiva: se marca vista cuando el
            // usuario la abre, así el paso siguiente (publicar) se destraba.
            var previewSeen = Vue.ref(false);
            function isStepDone(i) {
                if (i === 0) return !!(qTitle.value && qTitle.value.trim());
                if (i === 1) {
                    var hay = false;
                    var faltaTitulo = false;
                    groups.value.forEach(function (g) {
                        if (g.questions.length) hay = true;
                        if (!g.title || !g.title.trim()) faltaTitulo = true;
                    });
                    return hay && !faltaTitulo;
                }
                if (i === 2) return previewSeen.value;
                return false;
            }
            function stepReachable(i) {
                for (var k = 0; k < i; k++) if (!isStepDone(k)) return false;
                return true;
            }
            function stepStatus(i) {
                if (i === stepIndex.value) return 'active';
                if (isStepDone(i)) return 'done';
                if (stepReachable(i)) return 'available';
                return 'locked';
            }
            function gotoStep(k) {
                var idx = -1;
                for (var i = 0; i < STEPS.length; i++) if (STEPS[i].key === k) { idx = i; break; }
                if (idx < 0) return;
                if (!stepReachable(idx)) return;
                step.value = k;
                if (k === 'preview') previewSeen.value = true;
            }
            function stepNext() {
                var next = stepIndex.value + 1;
                if (next >= STEPS.length) return;
                if (!isStepDone(stepIndex.value)) return;
                step.value = STEPS[next].key;
                if (STEPS[next].key === 'preview') previewSeen.value = true;
            }
            function stepPrev() { if (stepIndex.value > 0) step.value = STEPS[stepIndex.value - 1].key; }
            var currentStepHelp = Vue.computed(function () { return STEPS[stepIndex.value].help; });
            var canGoNext = Vue.computed(function () {
                return stepIndex.value < STEPS.length - 1 && isStepDone(stepIndex.value);
            });
            var nextBlockedReason = Vue.computed(function () {
                if (canGoNext.value || stepIndex.value >= STEPS.length - 1) return '';
                var i = stepIndex.value;
                if (i === 0) return 'Cargá un título para continuar.';
                if (i === 1) {
                    var sinTitulo = groups.value.some(function (g) { return !g.title || !g.title.trim(); });
                    if (sinTitulo) return 'Todas las secciones necesitan un título.';
                    return 'Agregá al menos una pregunta para continuar.';
                }
                if (i === 2) return 'Abrí la vista previa para continuar.';
                return '';
            });

            // Vista de códigos (paso 3): índice plano de todas las preguntas con su
            // estado de codificación, para trabajarlas sin perder el contexto del árbol.
            var allQuestionsFlat = Vue.computed(function () {
                var out = [];
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) { out.push({ group: g, question: q }); });
                });
                return out;
            });
            // Blockers para publicar, calculados una vez y reusados en el paso 5.
            var publishBlockers = Vue.computed(function () {
                var out = [];
                if (isArchived.value) out.push('El cuestionario está archivado y no se puede publicar. Reactivalo desde el listado.');
                if (conflict.value)   out.push('Hay un conflicto de edición sin resolver.');
                if (dirty.value)      out.push('Hay cambios sin guardar. Guardá antes de publicar.');
                if (isNew.value)      out.push('El cuestionario todavía no se guardó por primera vez.');
                if (issues.value.length) out.push('Hay ' + issues.value.length + ' problema(s) en la estructura: ver el aviso de arriba.');
                if (!allQuestionsFlat.value.length) out.push('No hay preguntas cargadas.');
                return out;
            });

            // --- Lógica condicional (enableWhen) -------------------------------------
            // Solo se puede condicionar contra preguntas ANTERIORES: al responder, el
            // valor de una pregunta posterior todavía no existe, y además evita ciclos.
            // Una opción múltiple sirve como origen: el renderer evalúa la condición
            // contra el conjunto de opciones marcadas ("si alguna es X"), como pide
            // FHIR R5 para preguntas con varias respuestas.
            var conditionSources = Vue.computed(function () {
                if (!selected.value) return [];
                var list = [];
                var reached = false;
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (q === selected.value.question) { reached = true; return; }
                        // Un adjunto queda afuera: su respuesta es una referencia al archivo
                        // subido, no un valor comparable contra nada.
                        if (!reached && q.linkId && q.type !== 'attachment') list.push(q);
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

            // Operadores que el renderer sabe evaluar contra la pregunta de origen (mismo criterio
            // que EnableWhenEvaluator.Compare y QuestionnaireStructureRules en el server): igual y
            // distinto siempre; mayor y menor sólo contra números. Un "mayor que" sobre una opción
            // o un booleano daba siempre falso y la pregunta no se mostraba nunca.
            var OPERATORS = [
                { op: '=',  label: 'es igual a',     numericOnly: false },
                { op: '!=', label: 'es distinto de', numericOnly: false },
                { op: '>',  label: 'es mayor que',   numericOnly: true },
                { op: '<',  label: 'es menor que',   numericOnly: true }
            ];
            function esNumerica(q) { return !!q && ['integer', 'decimal', 'scale'].indexOf(q.type) >= 0; }
            function operatorAllowed(op, origen) {
                for (var i = 0; i < OPERATORS.length; i++) {
                    if (OPERATORS[i].op === op) return !OPERATORS[i].numericOnly || esNumerica(origen);
                }
                return false;
            }
            var conditionOps = Vue.computed(function () {
                var src = conditionSource.value;
                return OPERATORS.filter(function (o) { return !o.numericOnly || esNumerica(src); });
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
            // --- Integridad referencial de las condiciones ----------------------------
            // Una condición referencia otra pregunta por linkId. Si esa pregunta se borra,
            // o cambia a un tipo que ya no puede producir el valor esperado, la referencia
            // queda incoherente: el renderer no logra satisfacerla nunca y la pregunta
            // dependiente desaparece del formulario sin que nadie se entere.
            //
            // Toda operación que borre o mute preguntas pasa por estas funciones, así el
            // árbol no puede llegar a guardarse en ese estado. Una operación nueva (mover,
            // importar, pegar una sección) sólo tiene que llamarlas: la regla vive en un
            // único lugar en vez de repetirse en cada handler.

            function allQuestions() {
                var out = [];
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) { out.push(q); });
                });
                return out;
            }

            /// Preguntas cuya condición depende de alguno de los linkId dados.
            /// `exclude` deja afuera preguntas que están por desaparecer igual (borrar una
            /// sección entera no debería reportar sus propias condiciones internas).
            function dependentsOf(linkIds, exclude) {
                exclude = exclude || [];
                return allQuestions().filter(function (q) {
                    return q.enableWhen
                        && linkIds.indexOf(q.enableWhen.question) >= 0
                        && exclude.indexOf(q) < 0;
                });
            }

            /// Deja esas preguntas siempre visibles y lo informa. El aviso no es opcional:
            /// perder una condición sin enterarse es exactamente el problema que esta capa
            /// viene a resolver.
            function dropConditions(questions, motivo) {
                if (!questions.length) return;
                questions.forEach(function (q) { q.enableWhen = undefined; });
                toast((questions.length === 1
                    ? '"' + questions[0].text + '" quedó siempre visible'
                    : questions.length + ' preguntas quedaron siempre visibles') + ': ' + motivo, 'warning');
                dirty.value = true;
            }

            /// Familia de valor que produce cada tipo al responderse. Cambiar de tipo
            /// dentro de la misma familia no invalida una condición existente; cambiar de
            /// familia sí, porque el valor esperado guardado deja de ser comparable.
            // answerKind/onTypeChange se eliminaron con el catalogo de variables: el tipo de
            // una pregunta ya no se puede cambiar desde el editor -lo define la variable-, asi
            // que no queda ningun momento en que haya que reconciliar opciones ni condiciones
            // por un cambio de tipo hecho aca.

            // Condiciones ya rotas en lo que vino de la base: el árbol pudo haberse
            // guardado así antes de que existiera esta capa. Se listan en un cartel en vez
            // de arreglarse solas, porque tocar datos que la persona no editó en esta
            // sesión no es una decisión del editor.
            // Cualquier chequeo estructural nuevo se cuelga de esta lista.
            // `condition: true` marca los problemas que se arreglan dejando la pregunta siempre
            // visible (fixIssues); los demás piden otra acción.
            var issues = Vue.computed(function () {
                var known = {};
                allQuestions().forEach(function (q) { if (q.linkId) known[q.linkId] = q; });
                var out = [];
                allQuestions().forEach(function (q) {
                    var ew = q.enableWhen;
                    if (!ew || !ew.question) return;
                    var origen = known[ew.question];
                    var nombre = '"' + (q.text || q.linkId) + '"';
                    if (!origen) {
                        out.push({ question: q, condition: true, detail: nombre + ' depende de una pregunta que ya no existe: nunca se va a mostrar.' });
                    } else if (sinValor(ew.value)) {
                        out.push({ question: q, condition: true, detail: nombre + ' tiene una condición sin valor esperado: completala o quitala.' });
                    } else if (!operatorAllowed(ew.op || '=', origen)) {
                        out.push({ question: q, condition: true, detail: nombre + ' compara con "' + (ew.op || '=') + '" contra "' + (origen.text || origen.linkId) + '", que no es numérica: sólo admite "es igual a" o "es distinto de".' });
                    } else if ((origen.type === 'choice' || origen.type === 'openChoice')
                               && (origen.options || []).indexOf(ew.value) < 0) {
                        out.push({ question: q, condition: true, detail: nombre + ' depende de la opción "' + ew.value + '", que ya no está entre las opciones de "' + (origen.text || origen.linkId) + '".' });
                    }
                });

                // Sin variable el server rechaza el guardado ENTERO. Puede pasar al importar
                // preguntas de un cuestionario anterior al catálogo: se lista cuál es, en vez
                // de que el guardado falle sin decir dónde.
                allQuestions().forEach(function (q) {
                    if (!q.variableId) {
                        out.push({ question: q, condition: false, detail: '"' + (q.text || q.linkId) + '" no proviene de ninguna variable del catálogo: hay que reemplazarla o eliminarla.' });
                    }
                });
                return out;
            });
            var conditionIssues = Vue.computed(function () {
                return issues.value.filter(function (i) { return i.condition; });
            });

            // Deriva: preguntas armadas con una revision anterior a la vigente de su variable.
            // NO se re-sincroniza solo -eso cambiaria un cuestionario que nadie pidio cambiar,
            // y lo publicado debe ser reproducible-: se avisa y el jefe decide.
            // Toma la definición de la versión vigente sin cambiar el linkId, el enunciado, si es
            // obligatoria ni la condición. Las condiciones que dejen de tener sentido (por
            // ejemplo, sobre una opción que ya no existe) aparecen en el panel de problemas.
            var updatingVariable = Vue.reactive({});
            function updateToCurrentVersion(q) {
                if (!q || !q.variableId || updatingVariable[q.linkId]) return;
                updatingVariable[q.linkId] = true;
                var token = getToken();
                var headers = token ? { 'Authorization': 'Bearer ' + token } : {};
                fetch('/api/variables/' + q.variableId + '/versions/' + q.variableCurrentRevision,
                      { credentials: 'same-origin', headers: headers })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (v) {
                        applyVariableDefinition(q, v);
                        dirty.value = true;
                    })
                    .catch(function (err) {
                        window.cqToast && window.cqToast('No se pudo actualizar la pregunta: ' + err.message, 'error');
                    })
                    .then(function () { delete updatingVariable[q.linkId]; });
            }

            var staleVariables = Vue.computed(function () {
                return allQuestions().filter(function (q) {
                    return q.variableId
                        && typeof q.variableCurrentRevision === 'number'
                        && typeof q.variableRevision === 'number'
                        && q.variableCurrentRevision > q.variableRevision;
                });
            });

            function fixIssues() {
                dropConditions(
                    conditionIssues.value.map(function (i) { return i.question; }),
                    'su condición no se podía cumplir');
            }

            // --- Reordenar por drag & drop (sin dependencias) -------------------------
            // buildPayload deriva orderIndex de la posición en el arreglo, así que reordenar
            // el arreglo alcanza: al guardar, el orden nuevo queda persistido. Se usa el
            // drag nativo de HTML5 en vez de sumar SortableJS: la app ya carga librerías de
            // un CDN y eso es una deuda abierta; no hace falta agregar una más para mover
            // filas de una lista.
            var drag = Vue.ref(null);       // { type:'question'|'group', group, question }
            var dropTarget = Vue.ref(null); // objeto (pregunta o sección) resaltado al pasar por encima

            function onQuestionDragStart(g, q, ev) {
                drag.value = { type: 'question', group: g, question: q };
                if (ev.dataTransfer) {
                    ev.dataTransfer.effectAllowed = 'move';
                    try { ev.dataTransfer.setData('text/plain', q.linkId); } catch (e) { /* IE */ }
                }
            }
            function onGroupDragStart(g, ev) {
                drag.value = { type: 'group', group: g };
                if (ev.dataTransfer) {
                    ev.dataTransfer.effectAllowed = 'move';
                    try { ev.dataTransfer.setData('text/plain', g.title); } catch (e) { /* IE */ }
                }
            }
            function onDragEnd() { drag.value = null; dropTarget.value = null; }

            function onQuestionDragOver(q) {
                if (drag.value && drag.value.type === 'question') dropTarget.value = q;
            }
            function onGroupDragOver(g) {
                if (drag.value && drag.value.type === 'group') dropTarget.value = g;
            }

            // Soltar una pregunta ANTES de la pregunta objetivo. Sirve entre secciones:
            // sacar de la de origen e insertar en la de destino.
            function onQuestionDrop(targetGroup, targetQuestion) {
                var d = drag.value;
                if (!d || d.type !== 'question' || d.question === targetQuestion) { onDragEnd(); return; }
                var moving = d.question;
                d.group.questions = d.group.questions.filter(function (x) { return x !== moving; });
                var idx = targetGroup.questions.indexOf(targetQuestion);
                targetGroup.questions.splice(idx < 0 ? targetGroup.questions.length : idx, 0, moving);
                afterReorder();
                onDragEnd();
            }
            // Soltar una pregunta en una sección (al final): cubre secciones vacías y el
            // hueco debajo de la última pregunta.
            function onSectionDrop(targetGroup) {
                var d = drag.value;
                if (!d) { onDragEnd(); return; }
                if (d.type === 'group') { onGroupDrop(targetGroup); return; }
                if (d.type === 'question') {
                    var moving = d.question;
                    d.group.questions = d.group.questions.filter(function (x) { return x !== moving; });
                    targetGroup.questions.push(moving);
                    afterReorder();
                }
                onDragEnd();
            }
            // Soltar una sección: se inserta antes de la sección objetivo.
            function onGroupDrop(targetGroup) {
                var d = drag.value;
                if (!d || d.type !== 'group' || d.group === targetGroup) { onDragEnd(); return; }
                groups.value = groups.value.filter(function (x) { return x !== d.group; });
                var idx = groups.value.indexOf(targetGroup);
                groups.value.splice(idx < 0 ? groups.value.length : idx, 0, d.group);
                afterReorder();
                onDragEnd();
            }

            function afterReorder() {
                dirty.value = true;
                enforceConditionOrder();
            }

            // Una condición sólo puede apuntar a una pregunta ANTERIOR: al responder, el
            // valor de una posterior todavía no existe. Reordenar puede dejar la fuente
            // después de la dependiente; ahí la condición no se cumple nunca y la pregunta
            // desaparece del formulario. Misma respuesta que el resto de la capa: se libera
            // la condición y se avisa.
            function enforceConditionOrder() {
                var pos = {};
                allQuestions().forEach(function (q, i) { if (q.linkId) pos[q.linkId] = i; });
                var rotas = allQuestions().filter(function (q, i) {
                    return q.enableWhen && q.enableWhen.question
                        && pos[q.enableWhen.question] !== undefined
                        && pos[q.enableWhen.question] >= i;
                });
                dropConditions(rotas, 'la pregunta de la que dependían quedó después al reordenar');
            }

            function addGroup() {
                groups.value.push({ linkId: newSectionLinkId(), title: '', questions: [] });
                dirty.value = true;
            }
            // --- Selector de variables ---------------------------------------------
            // Reemplaza al viejo "Agregar pregunta" con su lista de tipos: una pregunta ya no
            // se escribe, se elige del catalogo que carga el Administrador.
            var varPickerOpen = Vue.ref(false);
            var varPickerGroup = null;
            var varPickerQuery = Vue.ref('');
            var varPickerLoading = Vue.ref(false);
            var varPickerError = Vue.ref(null);
            var varPickerResults = Vue.ref([]);
            var varPickerDebounce;

            function openVariablePicker(g) {
                varPickerGroup = g;
                varPickerOpen.value = true;
                varPickerQuery.value = '';
                varPickerError.value = null;
                searchVariables();
            }
            function closeVariablePicker() {
                varPickerOpen.value = false;
                varPickerGroup = null;
                clearTimeout(varPickerDebounce);
            }
            function onVariableQuery() {
                clearTimeout(varPickerDebounce);
                varPickerDebounce = setTimeout(searchVariables, 300);
            }

            // Un solo campo de busqueda: el server matchea nombre, codigo y concepto SNOMED.
            // Mismo patron de fetch que loadSpecialties (Bearer desde el meta cq-token).
            function searchVariables() {
                varPickerLoading.value = true;
                varPickerError.value = null;
                var token = getToken();
                var headers = token ? { 'Authorization': 'Bearer ' + token } : {};
                // Sólo publicadas, cada una con la definición de su versión vigente.
                fetch('/api/variables?_count=50&published=true&q=' + encodeURIComponent(varPickerQuery.value || ''),
                      { credentials: 'same-origin', headers: headers })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (data) { varPickerResults.value = data.results || []; })
                    .catch(function (err) {
                        varPickerError.value = 'No se pudo cargar el catalogo de variables: ' + err.message;
                        varPickerResults.value = [];
                    })
                    .then(function () { varPickerLoading.value = false; });
            }

            // linkId = código de la variable + el primer sufijo libre. Usar la misma variable
            // dos veces es un caso real ("Peso al ingreso" / "Peso al alta") y lo que las
            // distingue es ese linkId, que es la columna del reporte. Se busca un sufijo LIBRE,
            // no "cantidad de usos + 1": con peso-1 borrada y peso-2 en uso, contar daría
            // peso-2 otra vez.
            //
            // Tampoco se reusa un linkId que sigue guardado en el server (la base del merge):
            // el server conserva el snapshot de la variable para las preguntas que ya existían,
            // así que quitar una pregunta y volver a agregarla antes de guardar no tomaría la
            // definición nueva de la variable si conservara el mismo linkId.
            //
            // `reservados` suma linkIds ya asignados que todavía no están en el árbol (los de
            // una importación en curso, que se agregan todos juntos al final).
            function linkIdLibre(code, reservados) {
                var usados = usedLinkIds();
                Object.keys(indexQuestions(baseGroups)).forEach(function (l) { usados[l] = true; });
                Object.keys(reservados || {}).forEach(function (l) { usados[l] = true; });
                (props.publishedLinkIds || []).forEach(function (l) { usados[l] = true; });
                var n = 1;
                while (usados[code + '-' + n]) n++;
                return code + '-' + n;
            }

            // Lo que define la variable (de una versión publicada) sobre la pregunta. Viaja en
            // el payload para que el recurso FHIR sea autocontenido, pero el panel lo muestra
            // bloqueado y el servidor lo vuelve a tomar del catálogo al guardar.
            function applyVariableDefinition(q, v) {
                q.type = v.type;
                q.unit = v.unit || undefined;
                q.min = typeof v.min === 'number' ? v.min : undefined;
                q.max = typeof v.max === 'number' ? v.max : undefined;
                q.maxLength = typeof v.maxLength === 'number' ? v.maxLength : undefined;
                q.accept = v.accept || undefined;
                q.options = (v.options && v.options.length)
                    ? v.options.map(function (o) { return o.display || o.value; })
                    : undefined;
                q.code = v.snomedCode || undefined;
                q.codeDisplay = v.snomedDisplay || undefined;
                q.term = v.term || undefined;
                q.variableId = v.id;
                q.variableName = v.name;
                q.variableRevision = v.currentVersion;
                q.variableCurrentRevision = v.currentVersion;
            }

            function addQuestionFromVariable(v) {
                var g = varPickerGroup;
                if (!g) return;
                var q = {
                    linkId: linkIdLibre(v.code),
                    // El enunciado arranca en el de la variable y es lo unico, junto con
                    // required y enableWhen, que el jefe puede cambiar por cuestionario.
                    text: v.defaultText || v.name,
                    required: false
                };
                applyVariableDefinition(q, v);
                g.questions.push(q);
                selected.value = { group: g, question: q };
                dirty.value = true;
                closeVariablePicker();
            }
            function removeQuestion(g, q) {
                dropConditions(dependentsOf([q.linkId], [q]),
                    'dependían de "' + q.text + '", que se eliminó');
                g.questions = g.questions.filter(function (x) { return x !== q; });
                if (selected.value && selected.value.question === q) selected.value = null;
                dirty.value = true;
            }
            function removeGroup(g) {
                var borradas = g.questions.slice();
                dropConditions(
                    dependentsOf(borradas.map(function (q) { return q.linkId; }), borradas),
                    'dependían de preguntas de "' + g.title + '", que se eliminó');
                groups.value = groups.value.filter(function (x) { return x !== g; });
                if (selected.value && selected.value.group === g) selected.value = null;
                dirty.value = true;
            }
            function toast(msg, kind) { if (window.cqToast) window.cqToast(msg, kind); }

            // ── Importar preguntas de otro cuestionario de la misma especialidad ──────────
            // Se puede crear un cuestionario a partir de otro: se traen sus secciones/preguntas
            // y se agregan a la estructura actual, que después se edita libremente. Cada pregunta
            // importada conserva su variable (sin ella el server rechaza el guardado entero) y
            // recibe un linkId NUEVO con la misma convención que al agregarla desde el catálogo
            // (código de la variable + primer sufijo libre), para no colisionar con las de acá
            // y seguir siendo la misma columna de reporte que en el original. Sus
            // condiciones internas se re-apuntan a esos linkId nuevos; una condición que apunte a
            // una pregunta que no vino en la importación se descarta (misma política de integridad
            // que dropConditions/enforceConditionOrder para el resto del editor).
            var importOpen = Vue.ref(false);
            var importLoading = Vue.ref(false);   // trayendo la lista de candidatos
            var importBusy = Vue.ref(false);      // trayendo/insertando el elegido
            var importError = Vue.ref(null);
            var importCandidates = Vue.ref([]);

            function estadoLabel(s) {
                return s === 'published' ? 'Publicado' : (s === 'draft' ? 'Borrador' : (s || ''));
            }

            function openImport() {
                if (readOnly.value) return;
                importOpen.value = true;
                importError.value = null;
                importCandidates.value = [];
                importLoading.value = true;
                var url = '/Questionnaires/ImportCandidates?specialty=' + encodeURIComponent(specialtyCode.value || '')
                        + '&excludeId=' + encodeURIComponent(currentId.value || '');
                fetch(url, { credentials: 'same-origin' })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (list) { importCandidates.value = list || []; })
                    .catch(function (err) { importError.value = 'No se pudieron cargar los cuestionarios: ' + err.message; })
                    .then(function () { importLoading.value = false; });
            }

            function closeImport() { importOpen.value = false; }

            function usedLinkIds() {
                var used = {};
                allQuestions().forEach(function (q) { if (q.linkId) used[q.linkId] = true; });
                return used;
            }

            // El linkId del origen ya es "código-de-variable-N": se le saca el sufijo para
            // recuperar el código y se busca el primer N libre acá.
            function importedLinkId(sourceLinkId, reservados) {
                var code = (sourceLinkId || '').replace(/-\d+$/, '') || 'pregunta';
                var id = linkIdLibre(code, reservados);
                reservados[id] = true;
                return id;
            }

            // Mapea una pregunta del formato del server (mismo que props.groups / EditorModel) al
            // formato interno del editor, con un linkId nuevo. Es el mismo mapeo que hidrata
            // `initial` al arrancar, acá reutilizado para lo importado.
            function hydrateImportedQuestion(q, newLinkId) {
                return {
                    linkId: newLinkId,
                    text: q.text,
                    type: q.type,
                    required: !!q.required,
                    unit: q.unit,
                    min: typeof q.min === 'number' ? q.min : undefined,
                    max: typeof q.max === 'number' ? q.max : undefined,
                    maxLength: typeof q.maxLength === 'number' ? q.maxLength : undefined,
                    accept: q.accept,
                    enableWhen: q.enableWhen ? {
                        question: q.enableWhen.question,
                        op: q.enableWhen.op || '=',
                        value: q.enableWhen.value
                    } : undefined,
                    code: q.code,
                    codeDisplay: q.codeDisplay,
                    term: q.term,
                    options: q.options && q.options.length ? q.options.slice() : undefined,
                    variableId: q.variableId,
                    variableName: q.variableName,
                    variableRevision: q.variableRevision,
                    variableCurrentRevision: q.variableCurrentRevision
                };
            }

            function importFrom(candidate) {
                if (importBusy.value) return;
                importBusy.value = true;
                importError.value = null;
                fetch('/Questionnaires/EditorModel/' + candidate.id, { credentials: 'same-origin' })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (src) {
                        var srcGroups = src.groups || [];
                        var reservados = {};
                        // 1) linkId nuevo por pregunta + mapa viejo -> nuevo.
                        var remap = {};
                        var newGroups = srcGroups.map(function (g) {
                            var questions = (g.questions || []).map(function (q) {
                                var nl = importedLinkId(q.linkId, reservados);
                                if (q.linkId) remap[q.linkId] = nl;
                                return hydrateImportedQuestion(q, nl);
                            });
                            // linkId de sección nuevo: el del origen puede coincidir con uno de acá.
                            return { linkId: newSectionLinkId(reservados), title: g.title || 'Sección importada', questions: questions };
                        });
                        // 2) Re-apuntar condiciones internas; descartar las que quedan colgadas.
                        var count = 0, dropped = 0;
                        newGroups.forEach(function (g) {
                            g.questions.forEach(function (q) {
                                count++;
                                if (q.enableWhen && q.enableWhen.question) {
                                    var target = remap[q.enableWhen.question];
                                    if (target) q.enableWhen.question = target;
                                    else { q.enableWhen = undefined; dropped++; }
                                }
                            });
                        });
                        if (!count) { importError.value = 'Ese cuestionario no tiene preguntas para importar.'; return; }
                        // 3) Agregar al final de la estructura actual.
                        newGroups.forEach(function (g) { groups.value.push(g); });
                        dirty.value = true;
                        importOpen.value = false;
                        var msg = 'Se importaron ' + count + ' pregunta(s) de "' + (candidate.title || 'otro cuestionario') + '"';
                        if (dropped) msg += ' — ' + dropped + ' condición(es) que apuntaban afuera quedaron sin efecto';
                        toast(msg, 'success');
                    })
                    .catch(function (err) { importError.value = 'No se pudo importar: ' + err.message; })
                    .then(function () { importBusy.value = false; });
            }

            function getToken() { return window.ChequeARIslands.auth.token(); }

            // Catálogo de especialidades (CHEQUEAR.Specialty), reemplaza las 5 opciones que
            // antes venían hardcodeadas en este mismo archivo. /api/specialties exige Bearer
            // (mismo patrón que el buscador SNOMED) y, para un usuario no-admin, ya viene
            // recortado a sus especialidades.
            function loadSpecialties() {
                specialtiesLoading.value = true;
                specialtiesError.value = null;
                var token = getToken();
                var headers = token ? { 'Authorization': 'Bearer ' + token } : {};
                fetch('/api/specialties', { credentials: 'same-origin', headers: headers })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (data) {
                        specialties.value = data.results || [];
                        // Cuestionario nuevo: recién ahora sabemos cuál es la especialidad por
                        // defecto. Uno existente conserva SIEMPRE su código, aunque ya no esté
                        // en el catálogo (una especialidad dada de baja no debe reescribir
                        // fichas históricas).
                        if (!specialtyCode.value && data.defaultCode) specialtyCode.value = data.defaultCode;
                    })
                    .catch(function (err) {
                        specialtiesError.value = 'No se pudo cargar el catálogo de especialidades: ' + err.message;
                    })
                    .then(function () { specialtiesLoading.value = false; });
            }

            // --- Autocompletado SNOMED CT --------------------------------------------
            // Busca por el TÉRMINO CLÍNICO de la pregunta ("dolor torácico"), no por su
            // enunciado ("¿Presentó dolor en el pecho al esfuerzo?"): $expand hace matching
            // contra las descripciones de los conceptos, así que una oración con signos de
            // pregunta, negaciones y calificadores devuelve poco y mal. Separar ambos campos
            // además desacopla la redacción del significado: se puede reformular la pregunta
            // sin tocar el código del que dependen los reportes.
            // Elegir una sugerencia completa code + codeDisplay, que buildCoding() serializa
            // a Question.CodingJson junto al término.
            // Mismo criterio que diagnostico(): el detalle legible de un OperationOutcome
            // vive en issue[].diagnostics. Se declara aparte porque el buscador corre antes
            // de que diagnostico() este definido en el scope.
            // El buscador SNOMED vive ahora en Scripts/islands/shared/snomed.js: el ABM de
            // variables necesita exactamente la misma mecanica (debounce, backoff por 429,
            // cancelacion de la consulta en curso) y dos implementaciones del mismo contrato
            // contra $expand iban a divergir.
            //
            // Queda UNA sola instancia, la del cuestionario completo. La codificacion POR
            // PREGUNTA desaparecio del editor: cada pregunta la hereda de su variable, y quien
            // la define es el Administrador desde el catalogo.
            var questionnaireSnomed = window.ChequeARSnomed.createSearch(
                function () { return questionnaireCoding; },
                function () { dirty.value = true; });

            Vue.watch(function () { return questionnaireCoding.term; }, function (t) {
                questionnaireSnomed.schedule(t);
            });

            // ---- Serialización a FHIR R5 ----
            //
            // Desde la feature 009 el editor habla FHIR directamente: hay UNA sola
            // representación del cuestionario, no una interna y otra de interoperabilidad.
            // Lo que se emite acá tiene que poder reconstruirlo FhirQuestionnaireParser sin
            // pérdida; el par está cubierto por FhirRoundTripTests sobre los 11 tipos.

            var EXT = 'http://hl7.org/fhir/StructureDefinition/';
            var CQ_EXT = 'https://chequear.garrahan/StructureDefinition/';
            var SNOMED_SYSTEM = 'http://snomed.info/sct';
            var SPECIALTY_SYSTEM = 'https://chequear.garrahan/CodeSystem/specialty';

            // Vocabulario de las islas -> vocabulario del estándar. Mismo mapeo que
            // FhirQuestionnaireBuilder: R5 no tiene 'choice' ni 'open-choice'; ambos son
            // 'coding' sólo con opciones del catálogo (optionsOnly), y la selección múltiple
            // ('openChoice') es la que repite.
            function fhirType(t) {
                switch (t) {
                    case 'choice':
                    case 'openChoice': return 'coding';
                    case 'scale': return 'integer';   // FHIR no tiene tipo escala
                    default: return t;
                }
            }
            function answerConstraint(t) {
                return (t === 'choice' || t === 'openChoice') ? 'optionsOnly' : null;
            }
            function esEntero(t) { return t === 'integer' || t === 'scale'; }

            function ext(url, key, value) {
                var e = { url: url };
                e[key] = value;
                return e;
            }

            // Rango, unidad, patrón y formatos aceptados como extensiones ESTÁNDAR. maxLength
            // NO va acá: es campo nativo del item (lo agrega buildItem).
            function buildExtensions(q) {
                var out = [];
                var entero = esEntero(q.type);

                if (['integer', 'decimal', 'scale'].indexOf(q.type) >= 0) {
                    if (typeof q.min === 'number' && !isNaN(q.min))
                        out.push(ext(EXT + 'minValue', entero ? 'valueInteger' : 'valueDecimal', q.min));
                    if (typeof q.max === 'number' && !isNaN(q.max))
                        out.push(ext(EXT + 'maxValue', entero ? 'valueInteger' : 'valueDecimal', q.max));
                    if (q.unit)
                        out.push(ext(EXT + 'questionnaire-unit', 'valueCoding',
                                     { system: 'http://unitsofmeasure.org', code: q.unit, display: q.unit }));
                }

                // El marcador de escala hace al TIPO, no a la validación: sin él, al releer
                // la versión congelada una escala vuelve como número común.
                if (q.type === 'scale') {
                    out.push(ext(EXT + 'questionnaire-itemControl', 'valueCodeableConcept', {
                        coding: [{ system: 'http://hl7.org/fhir/questionnaire-item-control', code: 'slider' }]
                    }));
                }

                if (q.type === 'attachment' && q.accept)
                    out.push(ext(EXT + 'mimeType', 'valueCode', q.accept));


                // El término de autoría no tiene lugar nativo en FHIR y hasta la feature 009
                // no viajaba: se guarda aunque todavía no haya código elegido, porque deja
                // asentado qué mide la pregunta. Sin esta extensión se pierde en CADA guardado.
                var term = (q.term || '').trim();
                if (term) out.push(ext(CQ_EXT + 'question-term', 'valueString', term));

                // Todo lo que define la variable ya viajo denormalizado en campos nativos, asi
                // que el recurso queda autocontenido para un consumidor externo. Lo unico sin
                // lugar nativo es la referencia, y sin ella el parser la pierde en cada
                // guardado: el editor dejaria de saber de que variable viene cada pregunta.
                if (q.variableId) out.push(ext(CQ_EXT + 'question-variable-id', 'valueString', q.variableId));
                // La versión de la variable que usa la pregunta: si es distinta de la guardada
                // (porque se actualizó o se importó), el servidor aplica esa.
                if (q.variableId && q.variableRevision > 0)
                    out.push(ext(CQ_EXT + 'question-variable-version', 'valueInteger', q.variableRevision));

                return out;
            }

            function sinValor(v) { return v === undefined || v === null || v === ''; }

            // El answer[x] de la condición se tipa según la pregunta REFERENCIADA, no según
            // la que declara la condición.
            //
            // Una condición todavía sin valor viaja sin answer[x]: se conserva lo elegido hasta
            // ahora sin inventar un false o un 0 que cambiaría su sentido. El servidor no deja
            // publicarla así (ver QuestionnaireStructureRules).
            //
            // Para preguntas de opción viaja el texto de la opción, que es el valor con el que
            // trabajan el editor y el renderer. El Coding real (system + código) lo resuelve el
            // servidor al armar el recurso a partir de las opciones guardadas.
            function buildEnableWhen(q, porLinkId) {
                if (!q.enableWhen || !q.enableWhen.question) return null;

                var origen = porLinkId[q.enableWhen.question];
                var valor = q.enableWhen.value;
                var entrada = { question: q.enableWhen.question, operator: q.enableWhen.op || '=' };
                var tipo = origen ? origen.type : 'string';

                if (sinValor(valor)) return [entrada];

                if (tipo === 'boolean') entrada.answerBoolean = (valor === true || valor === 'true');
                else if (esEntero(tipo) && !isNaN(parseInt(valor, 10))) entrada.answerInteger = parseInt(valor, 10);
                else if (tipo === 'decimal' && !isNaN(parseFloat(valor))) entrada.answerDecimal = parseFloat(valor);
                else if (tipo === 'choice' || tipo === 'openChoice')
                    entrada.answerCoding = { code: String(valor), display: String(valor) };
                else entrada.answerString = String(valor);

                return [entrada];
            }

            function buildItem(q, porLinkId) {
                var item = {
                    linkId: q.linkId,
                    text: q.text,
                    type: fhirType(q.type),
                    required: !!q.required
                };

                var constraint = answerConstraint(q.type);
                if (constraint) item.answerConstraint = constraint;
                if (q.type === 'openChoice') item.repeats = true;

                // maxLength es campo NATIVO del item, no extensión.
                if ((q.type === 'string' || q.type === 'text')
                    && typeof q.maxLength === 'number' && !isNaN(q.maxLength) && q.maxLength > 0) {
                    item.maxLength = q.maxLength;
                }

                var extensiones = buildExtensions(q);
                if (extensiones.length) item.extension = extensiones;

                if (q.code) {
                    item.code = [{
                        system: SNOMED_SYSTEM,
                        code: q.code,
                        display: q.codeDisplay || (q.term || '').trim() || q.text
                    }];
                }

                var condicion = buildEnableWhen(q, porLinkId);
                if (condicion) {
                    item.enableWhen = condicion;
                    item.enableBehavior = 'all';
                }

                if (q.options && q.options.length) {
                    item.answerOption = q.options.map(function (o) {
                        return { valueCoding: { code: o, display: o } };
                    });
                }

                return item;
            }

            // Emite el recurso completo: secciones como item type=group con las preguntas
            // anidadas. El backend reemplaza la estructura guardada por ésta.
            function buildPayload() {
                // Índice para resolver el tipo de las preguntas referenciadas por condiciones.
                var porLinkId = {};
                groups.value.forEach(function (g) {
                    g.questions.forEach(function (q) { if (q.linkId) porLinkId[q.linkId] = q; });
                });

                var recurso = {
                    resourceType: 'Questionnaire',
                    // El estado lo decide el servidor (publicar, archivar); acá sólo se declara
                    // el que tiene, para no mandar un recurso que dice ser algo que no es.
                    status: isArchived.value ? 'retired' : (isPublished.value ? 'active' : 'draft'),
                    title: qTitle.value,
                    name: qTitle.value,
                    subjectType: ['Patient'],
                    item: groups.value.map(function (g, gi) {
                        return {
                            linkId: g.linkId || newSectionLinkId(),
                            text: g.title,
                            type: 'group',
                            item: g.questions.map(function (q) { return buildItem(q, porLinkId); })
                        };
                    })
                };

                if (specialtyCode.value) {
                    recurso.useContext = [{
                        code: { system: 'http://terminology.hl7.org/CodeSystem/usage-context-type', code: 'focus' },
                        valueCodeableConcept: {
                            coding: [{ system: SPECIALTY_SYSTEM, code: specialtyCode.value }]
                        }
                    }];
                }

                // Codificación del cuestionario completo. El concepto SNOMED va en el campo
                // nativo Questionnaire.code; el término de autoría, en la extensión propia
                // questionnaire-term (misma razón que la de la pregunta: no tiene lugar nativo
                // y se guarda aunque todavía no haya código). Es simétrico a buildItem.
                if (questionnaireCoding.code) {
                    recurso.code = [{
                        system: SNOMED_SYSTEM,
                        code: questionnaireCoding.code,
                        display: questionnaireCoding.codeDisplay || (questionnaireCoding.term || '').trim() || qTitle.value
                    }];
                }
                var qTerm = (questionnaireCoding.term || '').trim();
                if (qTerm) recurso.extension = [ext(CQ_EXT + 'questionnaire-term', 'valueString', qTerm)];

                return recurso;
            }

            // Los errores de la API son OperationOutcome: el detalle legible vive en
            // issue[].diagnostics. Con varios issues (validación contra perfil emite uno por
            // regla incumplida) se muestran todos, que es la diferencia entre "arreglá esto"
            // y "arreglá algo de esta lista".
            function diagnostico(body, porOmision) {
                var issues = body && body.issue;
                if (!issues || !issues.length) return porOmision;
                var textos = issues
                    .map(function (i) { return i.diagnostics; })
                    .filter(Boolean);
                return textos.length ? textos.join(' ') : porOmision;
            }

            // explicit = lo pidió el usuario (botón Guardar) -> confirmamos con un toast.
            // El autosave guarda en silencio: para eso está el indicador "Guardado hh:mm"
            // en el header. Los errores avisan siempre, vengan de donde vengan.
            // Un autosave que llega con otro guardado en vuelo no se pierde: se repite al terminar.
            var saveAgain = false;
            function save(options) {
                var explicit = !!(options && options.explicit);
                if (saving.value) { saveAgain = true; return Promise.resolve(false); }
                // Nada cambió desde el último guardado: no hay razón para pegarle al
                // server. Un cuestionario nuevo siempre tiene que crearse la primera vez,
                // aunque todavía no se haya tocado nada.
                if (!isNew.value && !dirty.value) {
                    // El usuario apreto Guardar pero no hay nada pendiente. No es
                    // un error: confirmamos con verde y wording positivo, en vez
                    // del azul info que se leia como cartel de bug.
                    if (explicit) toast('Ya esta todo guardado', 'success');
                    return Promise.resolve(true);
                }
                // El backend rechaza cualquier escritura sobre un archivado (500 si no se
                // corta acá) o sin el permiso questionnaire.write (403, QuestionnaireService):
                // cortar antes evita el viaje al server y explica por qué.
                if (isArchived.value) {
                    if (explicit) toast('Este cuestionario está archivado: no se puede editar. Publíquelo de nuevo desde el listado para reactivarlo.', 'error');
                    return Promise.resolve(false);
                }
                if (!canEdit) {
                    if (explicit) toast('Tu rol no permite editar cuestionarios.', 'error');
                    return Promise.resolve(false);
                }
                // Con un conflicto sin resolver no se vuelve a intentar: reintentar sólo
                // serviría para pisar a la otra persona en cuanto refresquemos la versión.
                if (conflict.value) return Promise.resolve(false);
                // El server rechaza un título vacío. Sin cortar acá, el autosave le pegaría cada
                // dos segundos y mostraría un error por cada intento; queda sucio (y beforeunload
                // avisa al salir) hasta que se cargue un título.
                if (!(qTitle.value || '').trim()) {
                    qTitleTouched.value = true;
                    if (explicit) toast('El cuestionario necesita un título para poder guardarse.', 'error');
                    return Promise.resolve(false);
                }
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
                    'Content-Type': 'application/fhir+json',
                    'Authorization': 'Bearer ' + token
                };
                // If-Match sólo al actualizar: al crear todavía no hay nada con qué chocar.
                if (!creating) headers['If-Match'] = 'W/"' + recordVersion.value + '"';

                // La foto que se manda se congela ACÁ. A partir de este punto marcamos
                // "limpio" de forma optimista: si el usuario edita algo mientras el PUT viaja
                // (ventana real en el Oracle lento de dev), markDirty vuelve a prender dirty y
                // ese cambio NO se pierde. Antes, dirty se apagaba recién en el .then —después
                // de la respuesta—, así que pisaba la edición hecha durante el vuelo y el
                // siguiente autosave la veía "sin cambios" (era el "no hay cambios para
                // guardar" al modificar el código SNOMED). En error se restaura (más abajo).
                var body = JSON.stringify(buildPayload());
                // Lo que queda en el server si este guardado sale bien: la base del próximo merge.
                var savedGroups = JSON.parse(JSON.stringify(groups.value));
                var savedHeader = currentHeader();
                dirty.value = false;

                // Devuelve una promesa que resuelve en true/false: publicar necesita saber
                // si el guardado previo salió bien antes de congelar una versión.
                return fetch(url, {
                    method: creating ? 'POST' : 'PUT',
                    headers: headers,
                    body: body
                }).then(function (res) {
                    if (res.status === 409) {
                        return res.json().catch(function () { return null; }).then(function (body) {
                            var err = new Error(diagnostico(body, 'Conflicto de edición'));
                            err.isConflict = true;
                            throw err;
                        });
                    }
                    // Un 422 trae en el OperationOutcome qué regla falló (título, linkId repetido,
                    // variable inactiva...): eso es lo que la persona necesita leer, no "HTTP 422".
                    if (!res.ok) {
                        return res.json().catch(function () { return null; }).then(function (body) {
                            throw new Error(diagnostico(body, 'HTTP ' + res.status));
                        });
                    }
                    return res.status === 204 ? null : res.json().catch(function () { return null; });
                }).then(function (data) {
                    // FHIR normaliza en "id" minúscula, así que ya no hace falta el fallback
                    // a "Id" que existía cuando viajaba la entidad serializada por .NET.
                    var newId = data && data.id;
                    if (creating && newId) {
                        currentId.value = newId;
                        isNew.value = false;
                        // Reflejar el id real en la URL sin recargar.
                        if (window.history && window.history.replaceState) {
                            window.history.replaceState(null, '', '/Questionnaires/Edit/' + newId);
                        }
                    }
                    // La versión del registro viaja en meta.versionId, que es el mecanismo
                    // nativo del estándar (y el mismo número que el ETag). Sin refrescarla,
                    // el segundo guardado seguido chocaría contra nuestro propio cambio.
                    var v = data && data.meta && parseInt(data.meta.versionId, 10);
                    if (typeof v === 'number' && !isNaN(v)) recordVersion.value = v;
                    baseGroups = savedGroups;
                    baseHeader = savedHeader;
                    lastSaved.value = new Date().toLocaleTimeString();
                    // dirty ya se apagó (optimista) antes del fetch; no se vuelve a tocar acá
                    // para no pisar una edición hecha mientras el guardado viajaba.
                    // Se guardó un cambio sobre un publicado: a partir de ahora hay que
                    // dejar publicar aunque "dirty" ya esté en false.
                    if (isPublished.value) hasPendingChanges.value = true;
                    if (creating) toast('Cuestionario creado', 'success');
                    else if (explicit) {
                        toast(isPublished.value
                            ? 'Cambios guardados. Publique de nuevo para que lleguen a los clínicos.'
                            : 'Cambios guardados', 'success');
                    }
                    return true;
                }).catch(function (err) {
                    // El guardado no llegó a persistir: revertimos el "limpio" optimista para
                    // que los cambios NO queden marcados como guardados (si no, se perderían en
                    // silencio). Si el usuario editó durante el vuelo, dirty ya está en true.
                    dirty.value = true;
                    if (err.isConflict) {
                        // Otra persona guardó en el medio: en lugar de pisar o descartar todo,
                        // se intenta combinar los dos trabajos (con descarte siempre disponible).
                        beginMerge();
                    } else {
                        toast('Error al guardar: ' + err.message, 'error');
                    }
                    return false;
                }).then(function (ok) {
                    saving.value = false;
                    if (saveAgain) {
                        saveAgain = false;
                        if (dirty.value && !conflict.value) { clearTimeout(debounce); debounce = setTimeout(save, 0); }
                    }
                    return ok;
                });
            }

            // Publicar desde el editor. Congela en una versión lo que está en la base, no lo
            // que está en pantalla: por eso exige que no haya cambios sin guardar (ver el
            // guard en publish()), en vez de guardar por su cuenta.
            // Descarta lo local y vuelve a cargar lo que hay en el server. Es la salida
            // honesta del conflicto: no hay merge automático de estructuras.
            function reloadFromServer() {
                dirty.value = false;   // que beforeunload no pregunte: la decisión ya se tomó
                window.location.reload();
            }

            // ── Merge de conflictos de edición (3 vías) ───────────────────────────────────
            // Cuando otra persona guardó mientras editábamos, en vez de obligar a descartar
            // todo, se combinan los dos trabajos: base = lo último que guardamos (o cargamos),
            // "lo mío" = lo local, "lo de la otra persona" = la versión actual del server. Se
            // auto-combina todo lo que sólo tocó una de las dos partes; sólo se pregunta donde
            // ambos modificaron lo MISMO: una pregunta (o uno la editó y el otro la borró), el
            // título, la especialidad o la codificación del cuestionario. Descartar sigue disponible.

            // Cabecera del cuestionario en la forma que se compara: la local y la del server.
            function currentHeader() {
                return {
                    title: qTitle.value || '',
                    specialty: specialtyCode.value || '',
                    code: questionnaireCoding.code || null,
                    codeDisplay: questionnaireCoding.codeDisplay || null,
                    term: (questionnaireCoding.term || '').trim() || null
                };
            }
            function headerFrom(model) {
                return {
                    title: model.title || '',
                    specialty: model.specialtyCode || '',
                    code: model.code || null,
                    codeDisplay: model.codeDisplay || null,
                    term: model.term || null
                };
            }
            // Campos de cabecera que se combinan por separado. La codificación va como unidad:
            // código, display y término describen un mismo concepto.
            var HEADER_FIELDS = [
                { key: 'title', label: 'Título del cuestionario', value: function (h) { return h.title; } },
                { key: 'specialty', label: 'Especialidad', value: function (h) { return h.specialty; } },
                { key: 'coding', label: 'Codificación del cuestionario',
                  value: function (h) { return JSON.stringify([h.code, h.codeDisplay, h.term]); } }
            ];

            function computeHeaderMerge(base, mine, theirs) {
                return HEADER_FIELDS.map(function (f) {
                    var nb = f.value(base), nm = f.value(mine), nt = f.value(theirs);
                    var mineChanged = nm !== nb, theirsChanged = nt !== nb;
                    var e = {
                        key: 'header:' + f.key, header: f.key, text: f.label,
                        needsChoice: false, choice: null, action: 'keep-theirs', auto: false, kind: null
                    };
                    if (mineChanged && !theirsChanged) { e.action = 'take-mine'; e.auto = true; }
                    else if (!mineChanged && theirsChanged) { e.auto = true; }
                    else if (mineChanged && theirsChanged && nm !== nt) { e.needsChoice = true; e.kind = 'edit-edit'; e.choice = 'mine'; }
                    return e;
                });
            }

            // Aplica a la cabecera local lo que resolvió el merge: donde ganó la otra persona,
            // se toma su valor; donde gané yo, queda el local.
            function applyHeaderMerge(theirs, plan) {
                plan.forEach(function (e) {
                    if (!e.header || resolveEntry(e) !== 'keep-theirs') return;
                    if (e.header === 'title') qTitle.value = theirs.title;
                    else if (e.header === 'specialty') specialtyCode.value = theirs.specialty;
                    else if (e.header === 'coding') {
                        questionnaireCoding.code = theirs.code || undefined;
                        questionnaireCoding.codeDisplay = theirs.codeDisplay || undefined;
                        questionnaireCoding.term = theirs.term || '';
                    }
                });
            }

            function numOrNull(v) { return typeof v === 'number' && !isNaN(v) ? v : null; }

            // Foto canónica de una pregunta para comparar entre las 3 fuentes. Incluye la sección
            // en la que está: mover una pregunta de sección es un cambio real, y antes el merge
            // no lo veía (la pregunta volvía a donde la tenía la otra persona sin avisar). El
            // orden dentro de la sección se compara aparte (ver orderEntry).
            function normQ(q, section) {
                if (!q) return null;
                return JSON.stringify({
                    section: section || null,
                    text: q.text || '',
                    type: q.type || 'string',
                    required: !!q.required,
                    options: (q.options || []).slice(),
                    min: numOrNull(q.min), max: numOrNull(q.max),
                    unit: q.unit || null, accept: q.accept || null,
                    maxLength: numOrNull(q.maxLength),
                    enableWhen: (q.enableWhen && q.enableWhen.question)
                        ? { question: q.enableWhen.question, op: q.enableWhen.op || '=', value: q.enableWhen.value === undefined ? null : q.enableWhen.value }
                        : null,
                    code: q.code || null, codeDisplay: q.codeDisplay || null, term: q.term || null,
                    // Cambiar una pregunta de variable es un cambio real y tiene que poder
                    // entrar en conflicto como cualquier otro campo.
                    variableId: q.variableId || null,
                    variableRevision: q.variableRevision || 0
                });
            }

            // linkId -> { q, section (linkId), sectionTitle, prev (linkId de la pregunta anterior
            // en la misma sección, o null) }. `prev` es lo que permite ubicar una pregunta nueva o
            // movida en el resultado sin depender de índices que la otra persona pudo correr.
            function indexQuestions(grps) {
                var map = {};
                (grps || []).forEach(function (g) {
                    var prev = null;
                    (g.questions || []).forEach(function (q) {
                        if (!q || !q.linkId) return;
                        map[q.linkId] = { q: q, section: g.linkId || null, sectionTitle: g.title || '', prev: prev };
                        prev = q.linkId;
                    });
                });
                return map;
            }

            // linkId -> { title, prev (linkId de la sección anterior, o null) }.
            function indexSections(grps) {
                var map = {};
                var prev = null;
                (grps || []).forEach(function (g) {
                    if (!g || !g.linkId) return;
                    map[g.linkId] = { title: g.title || '', prev: prev };
                    prev = g.linkId;
                });
                return map;
            }

            function unionKeys(a, b, c) {
                var keys = {};
                [a, b, c].forEach(function (idx) { Object.keys(idx).forEach(function (k) { keys[k] = true; }); });
                return Object.keys(keys);
            }

            // Una entrada del plan en la que sólo cuenta quién cambió el valor respecto de la base.
            // Es la misma regla que la cabecera: si cambió una sola parte, gana esa; si cambiaron
            // las dos distinto, se pregunta.
            function twoWayEntry(e, nb, nm, nt) {
                var mineChanged = nm !== nb, theirsChanged = nt !== nb;
                if (mineChanged && !theirsChanged) { e.action = 'take-mine'; e.auto = true; }
                else if (!mineChanged && theirsChanged) { e.auto = true; }
                else if (mineChanged && theirsChanged && nm !== nt) { e.needsChoice = true; e.kind = 'edit-edit'; e.choice = 'mine'; }
                return e;
            }

            function newEntry(key, text) {
                return { key: key, text: text, needsChoice: false, choice: null, action: 'keep-theirs', auto: false, kind: null };
            }

            // Orden relativo de los elementos que existen en las TRES fotos: agregar o quitar algo
            // no cuenta como reordenar (eso ya lo resuelven las entradas de alta y baja).
            function commonOrder(lista, comunes) {
                return JSON.stringify(lista.filter(function (k) { return comunes[k]; }));
            }

            function sectionsOf(grps) { return (grps || []).map(function (g) { return g.linkId; }).filter(Boolean); }
            function questionsOf(grps, sectionLinkId) {
                for (var i = 0; i < (grps || []).length; i++) {
                    if (grps[i].linkId === sectionLinkId) {
                        return (grps[i].questions || []).map(function (q) { return q.linkId; }).filter(Boolean);
                    }
                }
                return [];
            }

            // Plan de combinación. Además de cada pregunta y de la cabecera, compara las SECCIONES
            // (título, alta y baja) y el ORDEN (de las secciones y de las preguntas dentro de cada
            // una). Antes sólo comparaba preguntas y tomaba como esqueleto la versión de la otra
            // persona, así que un cambio de nombre de sección, una pregunta movida a otra sección,
            // una sección nueva vacía o un reordenamiento propios se perdían sin avisar.
            function computeMerge(base, mine, theirs, baseH, mineH, theirsH) {
                var plan = computeHeaderMerge(baseH, mineH, theirsH);

                // Secciones: título, alta y baja.
                var bs = indexSections(base), ms = indexSections(mine), ts = indexSections(theirs);
                unionKeys(bs, ms, ts).forEach(function (k) {
                    var b = bs[k], m = ms[k], t = ts[k];
                    var e = newEntry('section:' + k, 'Nombre de la sección "' + ((m || t || b).title || 'sin título') + '"');
                    e.section = k;
                    e.mineTitle = m ? m.title : null;
                    if (m && t) {
                        if (b) twoWayEntry(e, b.title, m.title, t.title);
                    } else if (m && !t) {
                        // La agregué yo: va. La borró la otra persona: se resuelve por sus
                        // preguntas (si alguna mía sobrevive, la sección vuelve con ella).
                        if (!b) { e.action = 'take-mine'; e.auto = true; } else e.action = 'noop';
                    } else if (!m && t) {
                        // La borré yo: se quita sólo si queda vacía. Si la otra persona le agregó o
                        // editó preguntas que ganan, borrar la sección se las llevaría puestas.
                        if (b) { e.action = 'delete-if-empty'; e.auto = true; }
                    } else {
                        e.action = 'noop';
                    }
                    plan.push(e);
                });

                // Orden de las secciones.
                var comunesS = {};
                Object.keys(bs).forEach(function (k) { if (ms[k] && ts[k]) comunesS[k] = true; });
                var ordenS = twoWayEntry(newEntry('order:sections', 'Orden de las secciones'),
                    commonOrder(sectionsOf(base), comunesS), commonOrder(sectionsOf(mine), comunesS), commonOrder(sectionsOf(theirs), comunesS));
                ordenS.order = 'sections';
                if (ordenS.auto || ordenS.needsChoice) plan.push(ordenS);

                // Preguntas.
                var b = indexQuestions(base), m = indexQuestions(mine), t = indexQuestions(theirs);
                unionKeys(b, m, t).forEach(function (k) {
                    var bi = b[k], mi = m[k], ti = t[k];
                    var nb = normQ(bi && bi.q, bi && bi.section), nm = normQ(mi && mi.q, mi && mi.section), nt = normQ(ti && ti.q, ti && ti.section);
                    var mineChanged = nm !== nb, theirsChanged = nt !== nb;
                    var e = newEntry(k, (mi && mi.q.text) || (ti && ti.q.text) || k);
                    e.mine = mi || null;

                    if (mi && ti) {
                        if (!mineChanged && theirsChanged) { e.action = 'keep-theirs'; e.auto = true; }
                        else if (mineChanged && !theirsChanged) { e.action = 'take-mine'; e.auto = true; }
                        else if (mineChanged && theirsChanged && nm !== nt) { e.needsChoice = true; e.kind = 'edit-edit'; e.choice = 'mine'; }
                        // (ninguno cambió, o el mismo cambio) -> keep-theirs, sin marcar auto
                    } else if (mi && !ti) {
                        if (!bi) { e.action = 'take-mine'; e.auto = true; }        // la agregué yo
                        else if (mineChanged) { e.needsChoice = true; e.kind = 'mine-vs-delete'; e.choice = 'mine'; }  // la borraron; yo la edité
                        else { e.action = 'delete'; e.auto = true; }               // la borraron; no la toqué
                    } else if (!mi && ti) {
                        if (!bi) { e.action = 'keep-theirs'; }                     // la agregaron ellos
                        else if (theirsChanged) { e.needsChoice = true; e.kind = 'delete-vs-theirs'; e.choice = 'delete'; }  // yo la borré; ellos la editaron
                        else { e.action = 'delete'; e.auto = true; }               // yo la borré; no la tocaron
                    } else { e.action = 'noop'; }
                    plan.push(e);
                });

                // Orden de las preguntas dentro de cada sección que existe en las tres fotos.
                Object.keys(comunesS).forEach(function (sec) {
                    var qb = questionsOf(base, sec), qm = questionsOf(mine, sec), qt = questionsOf(theirs, sec);
                    var comunes = {};
                    qb.forEach(function (k) { if (qm.indexOf(k) >= 0 && qt.indexOf(k) >= 0) comunes[k] = true; });
                    var e = twoWayEntry(newEntry('order:' + sec, 'Orden de las preguntas en "' + (ms[sec].title || 'sin título') + '"'),
                        commonOrder(qb, comunes), commonOrder(qm, comunes), commonOrder(qt, comunes));
                    e.order = sec;
                    if (e.auto || e.needsChoice) plan.push(e);
                });

                return {
                    plan: plan,
                    conflicts: plan.filter(function (x) { return x.needsChoice; }),
                    autoCount: plan.filter(function (x) { return x.auto; }).length
                };
            }

            function resolveEntry(e) {
                if (!e.needsChoice) return e.action;
                if (e.kind === 'edit-edit') return e.choice === 'mine' ? 'take-mine' : 'keep-theirs';
                if (e.kind === 'mine-vs-delete') return e.choice === 'mine' ? 'take-mine' : 'delete';
                if (e.kind === 'delete-vs-theirs') return e.choice === 'theirs' ? 'keep-theirs' : 'delete';
                return 'keep-theirs';
            }

            // Esqueleto = la versión de la otra persona; encima se aplica lo mío que ganó, ubicado
            // por identidad (linkId de sección y de pregunta) y no por título ni por índice.
            function buildMerged(theirs, mine, plan) {
                var merged = JSON.parse(JSON.stringify(theirs || []));
                var mineSections = indexSections(mine);

                function sectionIndex(linkId) {
                    for (var i = 0; i < merged.length; i++) if (merged[i].linkId === linkId) return i;
                    return -1;
                }
                // Inserta después de `prev` si está; si no hay anterior, al principio; si el
                // anterior ya no existe, al final.
                function insertAfter(list, item, prev, keyOf) {
                    if (!prev) { list.unshift(item); return; }
                    for (var i = 0; i < list.length; i++) {
                        if (keyOf(list[i]) === prev) { list.splice(i + 1, 0, item); return; }
                    }
                    list.push(item);
                }
                function sectionKey(g) { return g.linkId; }
                function questionKey(q) { return q.linkId; }
                // La sección con ese linkId; si la otra persona la borró, se recrea con lo mío.
                function ensureSection(linkId, title) {
                    var i = sectionIndex(linkId);
                    if (i >= 0) return merged[i];
                    var g = { linkId: linkId, title: title || 'Nueva sección', questions: [] };
                    var prev = mineSections[linkId] ? mineSections[linkId].prev : null;
                    insertAfter(merged, g, prev, sectionKey);
                    return g;
                }
                function findQ(linkId) {
                    for (var i = 0; i < merged.length; i++) {
                        var qs = merged[i].questions || [];
                        for (var j = 0; j < qs.length; j++) if (qs[j].linkId === linkId) return { g: merged[i], idx: j };
                    }
                    return null;
                }
                function upsert(loc) {
                    var clone = JSON.parse(JSON.stringify(loc.q));
                    var found = findQ(loc.q.linkId);
                    // Misma sección: se reemplaza en su lugar, sin tocar el orden de la otra persona.
                    if (found && found.g.linkId === loc.section) { found.g.questions.splice(found.idx, 1, clone); return; }
                    if (found) found.g.questions.splice(found.idx, 1);
                    insertAfter(ensureSection(loc.section, loc.sectionTitle).questions, clone, loc.prev, questionKey);
                }
                function removeQ(linkId) { var f = findQ(linkId); if (f) f.g.questions.splice(f.idx, 1); }
                // Lo de `orden` primero, en ese orden; lo que no figura, después, como estaba.
                function reorder(list, orden, keyOf) {
                    var pos = {};
                    orden.forEach(function (k, i) { pos[k] = i; });
                    var dentro = list.filter(function (x) { return pos[keyOf(x)] !== undefined; })
                                     .sort(function (a, b) { return pos[keyOf(a)] - pos[keyOf(b)]; });
                    var fuera = list.filter(function (x) { return pos[keyOf(x)] === undefined; });
                    return dentro.concat(fuera);
                }

                var porBorrar = [];
                // 1) Secciones: nombres y altas propias.
                plan.forEach(function (e) {
                    if (!e.section) return;
                    var r = resolveEntry(e);
                    if (r === 'take-mine') ensureSection(e.section, e.mineTitle).title = e.mineTitle;
                    else if (r === 'delete-if-empty') porBorrar.push(e.section);
                });
                // 2) Preguntas.
                plan.forEach(function (e) {
                    if (e.header || e.section || e.order) return;
                    var r = resolveEntry(e);
                    if (r === 'take-mine') upsert(e.mine);
                    else if (r === 'delete') removeQ(e.key);
                    // 'keep-theirs' / 'noop' -> ya está en el esqueleto
                });
                // 3) Orden, donde ganó el mío.
                plan.forEach(function (e) {
                    if (!e.order || resolveEntry(e) !== 'take-mine') return;
                    if (e.order === 'sections') {
                        merged = reorder(merged, sectionsOf(mine), sectionKey);
                    } else {
                        var i = sectionIndex(e.order);
                        if (i >= 0) merged[i].questions = reorder(merged[i].questions, questionsOf(mine, e.order), questionKey);
                    }
                });
                // 4) Secciones que borré yo, sólo si quedaron vacías.
                return merged.filter(function (g) {
                    return porBorrar.indexOf(g.linkId) < 0 || (g.questions || []).length > 0;
                });
            }

            // Trae la versión de la otra persona (mismo formato que props.groups) y arma el plan.
            function beginMerge() {
                fetch('/Questionnaires/EditorModel/' + currentId.value, { credentials: 'same-origin' })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (theirs) {
                        var res = computeMerge(baseGroups, groups.value, theirs.groups || [],
                                               baseHeader, currentHeader(), headerFrom(theirs));
                        pendingTheirs = theirs;
                        mergePlanAll.value = res.plan;
                        mergeAutoCount.value = res.autoCount;
                        if (!res.conflicts.length) finishMerge(true);   // nada que decidir: combinar y guardar
                        else merging.value = true;                      // hay conflictos: abrir el modal
                    })
                    .catch(function () {
                        // Sin la otra versión no se puede combinar: se cae al camino honesto de descartar.
                        conflict.value = true;
                    });
            }

            function finishMerge(silent) {
                if (!pendingTheirs) return;
                var merged = buildMerged(pendingTheirs.groups || [], groups.value, mergePlanAll.value);
                var theirsHeader = headerFrom(pendingTheirs);
                applyHeaderMerge(theirsHeader, mergePlanAll.value);
                groups.value = merged;
                // La base pasa a ser lo que hay en el server (la versión de la otra persona):
                // si el guardado de lo combinado vuelve a chocar, el próximo merge parte de ahí.
                baseGroups = JSON.parse(JSON.stringify(pendingTheirs.groups || []));
                baseHeader = theirsHeader;
                recordVersion.value = pendingTheirs.concurrencyVersion;
                merging.value = false; conflict.value = false;
                pendingTheirs = null; mergePlanAll.value = []; mergeAutoCount.value = 0;
                dirty.value = true;
                save({ explicit: true }).then(function (ok) {
                    if (ok) toast(silent
                        ? 'Se combinaron tus cambios con los de la otra persona.'
                        : 'Cambios combinados y guardados.', 'success');
                });
            }

            function confirmMerge() { finishMerge(false); }
            function cancelMerge() { merging.value = false; }   // cierra el modal; nada se descarta

            function publish() {
                if (saving.value || publishing.value) return;
                // Ya publicado y el borrador es igual a esa versión (nada en esta sesión
                // ni guardado antes sin publicar): publicar de nuevo sólo crearía una
                // versión idéntica a la actual.
                if (isPublished.value && !dirty.value && !hasPendingChanges.value) {
                    toast('No hay cambios para publicar', 'info');
                    return;
                }
                if (conflict.value) {
                    toast('Resuelva el conflicto de edición antes de publicar.', 'error');
                    return;
                }
                // Publicar congela la estructura para los clínicos: una condición rota se
                // vuelve una pregunta que nadie va a poder ver, y ya no hay forma de
                // corregir esa versión.
                if (issues.value.length) {
                    toast('Hay ' + issues.value.length + ' problema(s) en la estructura del cuestionario. Resolvelos antes de publicar.', 'error');
                    return;
                }
                // Publicar exige haber guardado antes: no queremos congelar en una versión
                // algo que sólo está en pantalla, y el guardado implícito de antes, hecho en
                // el mismo instante que la publicación, dejaba "editado" y "publicado" con la
                // misma marca de tiempo y se veían fuera de orden en auditoría. Guardar es
                // una decisión explícita y previa.
                if (dirty.value || isNew.value) {
                    toast('Guarde los cambios antes de publicar.', 'error');
                    return;
                }
                var token = getToken();
                if (!token) { toast('No hay sesión: no se puede publicar (token ausente).', 'error'); return; }

                publishing.value = true;
                fetch('/fhir/r5/Questionnaire/' + currentId.value + '/$publish', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': 'Bearer ' + token,
                        // Se publica lo que está en la base: hay que demostrar que es lo que
                        // este editor guardó por última vez, no algo que otra persona guardó después.
                        'If-Match': 'W/"' + recordVersion.value + '"'
                    },
                    body: '{}'
                }).then(function (res) {
                    if (res.ok) return null;
                    if (res.status === 409) {
                        // Otra persona guardó después de nuestro último guardado: se combina como
                        // en un guardado, y recién después se puede publicar.
                        beginMerge();
                        throw new Error('otra persona guardó cambios. Revisá la combinación y volvé a publicar.');
                    }
                    // El API devuelve 422 con OperationOutcome cuando la publicación no es
                    // válida (una regla de negocio, o el perfil de conformidad).
                    return res.json().catch(function () { return null; }).then(function (body) {
                        throw new Error(diagnostico(body, 'HTTP ' + res.status));
                    });
                }).then(function () {
                    dirty.value = false;
                    // Publicar es un punto final del flujo de edición: volver al listado en
                    // vez de recargar el editor, que es donde se ve el resultado (versión,
                    // estado) sobre todos los cuestionarios, no sólo este.
                    window.location.href = '/Questionnaires';
                }).catch(function (err) {
                    toast('No se pudo publicar: ' + err.message, 'error');
                    publishing.value = false;
                });
            }

            function saveExplicit() { save({ explicit: true }); }

            // Autosave (2s debounce) sólo para borradores ya persistidos:
            //  - uno nuevo se guarda a mano la primera vez, para no crear duplicados;
            //  - uno publicado no se autoguarda nunca (ver isPublished);
            //  - uno archivado no se guarda nunca: el backend no lo permite (ver isArchived).
            var debounce;
            function markDirty() {
                dirty.value = true;
                if (isNew.value || isPublished.value || readOnly.value || conflict.value) return;
                clearTimeout(debounce);
                debounce = setTimeout(save, 2000);
            }
            Vue.watch(groups, markDirty, { deep: true });
            // Título y especialidad viven fuera de `groups`: sin este watcher, editar sólo
            // la cabecera no marcaba nada como sucio, así que no se autoguardaba y
            // beforeunload dejaba salir de la página perdiendo el cambio en silencio. La
            // codificación del cuestionario (objeto reactivo) va con deep por lo mismo:
            // tipear el término o el código a mano tiene que marcar la ficha como sucia.
            Vue.watch([qTitle, specialtyCode], markDirty);
            Vue.watch(questionnaireCoding, markDirty, { deep: true });

            // Sin autosave en publicados, salir de la página con cambios pendientes los
            // perdía en silencio. El navegador pide confirmación. En un archivado nada se
            // guarda nunca, así que preguntar sería confuso ("¿perder cambios?" cuando ya
            // sabemos que no se iban a guardar).
            function beforeUnload(e) {
                if (!dirty.value || readOnly.value) return;
                e.preventDefault();
                e.returnValue = '';
                return '';
            }

            // Engancha los botones del header (viven fuera de la isla). Ojo: el listener
            // recibe el Event, así que va el wrapper y no save directamente.
            Vue.onMounted(function () {
                loadSpecialties();
                var btn = document.getElementById('saveBtn');
                if (btn) btn.addEventListener('click', saveExplicit);
                var pub = document.getElementById('publishBtn');
                if (pub) pub.addEventListener('click', publish);
                window.addEventListener('beforeunload', beforeUnload);

                // Deshabilita los botones mientras hay un guardado/publicación en curso:
                // sin esto, clickear varias veces mientras el primer POST todavía no
                // volvió disparaba una creación por click (creating quedaba en true hasta
                // que resolvía el primero).
                // Publicar queda deshabilitado mientras haya cambios sin guardar (o sea un
                // cuestionario nuevo aún sin persistir): publicar exige guardar antes, así el
                // botón lo comunica y no sólo el toast del guard.
                // Guardar: sólo si hay cambios sin guardar (dirty). Sin dirty no hay
                // nada que persistir, y el botón deshabilitado sirve además como
                // confirmación visual de "todo persistido".
                // Publicar: no permitir si hay cambios sin guardar ni si es un
                // cuestionario nuevo sin persistir; y si ya está publicado, exigir
                // que haya cambios pendientes (si no, publicar sería un no-op).
                Vue.watch([saving, publishing, dirty, isNew, isPublished, hasPendingChanges], function () {
                    var busy = saving.value || publishing.value;
                    if (btn) btn.disabled = busy || !dirty.value;
                    if (pub) {
                        var noPendientes = isPublished.value && !hasPendingChanges.value;
                        pub.disabled = busy || dirty.value || isNew.value || noPendientes;
                    }
                }, { immediate: true });
            });
            Vue.onUnmounted(function () {
                clearTimeout(debounce);
                questionnaireSnomed.cancel();
                clearTimeout(varPickerDebounce);
                var btn = document.getElementById('saveBtn');
                if (btn) btn.removeEventListener('click', saveExplicit);
                var pub = document.getElementById('publishBtn');
                if (pub) pub.removeEventListener('click', publish);
                window.removeEventListener('beforeunload', beforeUnload);
            });

            return {
                groups: groups, selected: selected,
                dirty: dirty, lastSaved: lastSaved, saving: saving, publishing: publishing,
                qTitle: qTitle, qTitleTouched: qTitleTouched, specialtyCode: specialtyCode, isNew: isNew,
                specialties: specialties, specialtiesLoading: specialtiesLoading, specialtiesError: specialtiesError,
                isPublished: isPublished, isArchived: isArchived, canEdit: canEdit, readOnly: readOnly, publish: publish,
                conflict: conflict, reloadFromServer: reloadFromServer,
                merging: merging, mergeAutoCount: mergeAutoCount, conflictEntries: conflictEntries,
                confirmMerge: confirmMerge, cancelMerge: cancelMerge,
                issues: issues, conditionIssues: conditionIssues, fixIssues: fixIssues,
                conditionSources: conditionSources, conditionSource: conditionSource, conditionOps: conditionOps,
                onConditionQuestion: onConditionQuestion, onConditionValue: onConditionValue,
                questionnaireCoding: questionnaireCoding, questionnaireSnomed: questionnaireSnomed,
                selectQuestion: selectQuestion, addGroup: addGroup,
                staleVariables: staleVariables, updateToCurrentVersion: updateToCurrentVersion, updatingVariable: updatingVariable,
                varPickerOpen: varPickerOpen, varPickerQuery: varPickerQuery,
                varPickerLoading: varPickerLoading, varPickerError: varPickerError,
                varPickerResults: varPickerResults,
                openVariablePicker: openVariablePicker, closeVariablePicker: closeVariablePicker,
                onVariableQuery: onVariableQuery, addQuestionFromVariable: addQuestionFromVariable,
                typeLabel: typeLabel,
                removeQuestion: removeQuestion, removeGroup: removeGroup, save: saveExplicit,
                importOpen: importOpen, importLoading: importLoading, importBusy: importBusy,
                importError: importError, importCandidates: importCandidates,
                openImport: openImport, closeImport: closeImport, importFrom: importFrom,
                estadoLabel: estadoLabel,
                drag: drag, dropTarget: dropTarget,
                onQuestionDragStart: onQuestionDragStart, onGroupDragStart: onGroupDragStart,
                onDragEnd: onDragEnd, onQuestionDragOver: onQuestionDragOver, onGroupDragOver: onGroupDragOver,
                onQuestionDrop: onQuestionDrop, onSectionDrop: onSectionDrop,
                STEPS: STEPS, step: step, stepIndex: stepIndex, gotoStep: gotoStep,
                stepNext: stepNext, stepPrev: stepPrev, currentStepHelp: currentStepHelp,
                stepStatus: stepStatus, isStepDone: isStepDone, stepReachable: stepReachable,
                canGoNext: canGoNext, nextBlockedReason: nextBlockedReason,
                allQuestionsFlat: allQuestionsFlat,
                publishBlockers: publishBlockers
            };
        },
        template: '\
<div>\
<div v-if="conflict" class="cq-card mb-2" style="border-left:3px solid var(--cq-danger);">\
    <div class="d-flex align-items-start gap-2">\
        <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-danger);"></i>\
        <div class="flex-fill">\
            <div class="cq-text-sm"><strong>Otra persona guardó cambios mientras usted editaba.</strong></div>\
            <div class="cq-text-sm cq-text-muted">Sus cambios NO se guardaron, y los de la otra persona siguen intactos. No se pudo traer su versión para combinarlas, así que debe elegir: anote lo suyo y vuelva a cargar, o abra el cuestionario en otra pestaña para comparar.</div>\
            <button class="btn btn-sm btn-danger mt-2" @click="reloadFromServer"><i class="bi bi-arrow-clockwise me-1"></i> Descartar lo mío y recargar</button>\
        </div>\
    </div>\
</div>\
<div v-if="merging" style="position:fixed; inset:0; background:rgba(0,0,0,.45); z-index:1050; display:flex; align-items:center; justify-content:center; padding:16px;" @click.self="cancelMerge">\
    <div class="cq-card" style="max-width:640px; width:100%; max-height:85vh; overflow:auto;">\
        <h2 class="cq-section-title" style="margin-top:0;">Otra persona editó este cuestionario</h2>\
        <p class="cq-text-sm cq-text-muted">Se pueden combinar los dos trabajos. <strong>{{ mergeAutoCount }}</strong> cambio(s) se integran automáticamente. Elija qué versión conservar donde ambos modificaron la misma pregunta:</p>\
        <div v-if="!conflictEntries.length" class="cq-text-sm cq-text-muted mb-2">No hay diferencias que resolver a mano.</div>\
        <div v-for="e in conflictEntries" :key="e.key" class="mb-3" style="border-left:3px solid var(--cq-warning); padding-left:.75rem;">\
            <div class="cq-text-sm fw-semibold">{{ e.text }}</div>\
            <div v-if="e.kind === \'edit-edit\'" class="cq-text-xs cq-text-muted mb-1">Ambos la modificaron.</div>\
            <div v-else-if="e.kind === \'mine-vs-delete\'" class="cq-text-xs cq-text-muted mb-1">La otra persona la eliminó; usted la modificó.</div>\
            <div v-else class="cq-text-xs cq-text-muted mb-1">Usted la eliminó; la otra persona la modificó.</div>\
            <div class="btn-group btn-group-sm" role="group">\
                <template v-if="e.kind === \'edit-edit\'">\
                    <input type="radio" class="btn-check" :name="\'mc_\'+e.key" :id="\'mc_\'+e.key+\'_m\'" :checked="e.choice===\'mine\'" @change="e.choice=\'mine\'" />\
                    <label class="btn btn-outline-primary" :for="\'mc_\'+e.key+\'_m\'">La mía</label>\
                    <input type="radio" class="btn-check" :name="\'mc_\'+e.key" :id="\'mc_\'+e.key+\'_t\'" :checked="e.choice===\'theirs\'" @change="e.choice=\'theirs\'" />\
                    <label class="btn btn-outline-primary" :for="\'mc_\'+e.key+\'_t\'">La de la otra persona</label>\
                </template>\
                <template v-else-if="e.kind === \'mine-vs-delete\'">\
                    <input type="radio" class="btn-check" :name="\'mc_\'+e.key" :id="\'mc_\'+e.key+\'_m\'" :checked="e.choice===\'mine\'" @change="e.choice=\'mine\'" />\
                    <label class="btn btn-outline-primary" :for="\'mc_\'+e.key+\'_m\'">Mantener la mía</label>\
                    <input type="radio" class="btn-check" :name="\'mc_\'+e.key" :id="\'mc_\'+e.key+\'_d\'" :checked="e.choice===\'delete\'" @change="e.choice=\'delete\'" />\
                    <label class="btn btn-outline-primary" :for="\'mc_\'+e.key+\'_d\'">Aceptar el borrado</label>\
                </template>\
                <template v-else>\
                    <input type="radio" class="btn-check" :name="\'mc_\'+e.key" :id="\'mc_\'+e.key+\'_d\'" :checked="e.choice===\'delete\'" @change="e.choice=\'delete\'" />\
                    <label class="btn btn-outline-primary" :for="\'mc_\'+e.key+\'_d\'">Eliminar</label>\
                    <input type="radio" class="btn-check" :name="\'mc_\'+e.key" :id="\'mc_\'+e.key+\'_t\'" :checked="e.choice===\'theirs\'" @change="e.choice=\'theirs\'" />\
                    <label class="btn btn-outline-primary" :for="\'mc_\'+e.key+\'_t\'">Mantener la de la otra persona</label>\
                </template>\
            </div>\
        </div>\
        <div class="d-flex justify-content-end gap-2 border-top pt-3 mt-3">\
            <button class="btn btn-outline-danger" @click="reloadFromServer"><i class="bi bi-arrow-clockwise me-1"></i> Descartar lo mío y recargar</button>\
            <button class="btn btn-primary" @click="confirmMerge"><i class="bi bi-check2 me-1"></i> Combinar y guardar</button>\
        </div>\
    </div>\
</div>\
<div v-if="varPickerOpen" style="position:fixed; inset:0; background:rgba(0,0,0,.35); z-index:1050; display:flex; align-items:flex-start; justify-content:center; padding:48px 16px;" @click.self="closeVariablePicker">\
    <div class="cq-card" style="max-width:640px; width:100%; max-height:85vh; overflow:auto;">\
        <div class="d-flex justify-content-between align-items-center mb-2">\
            <h3 class="m-0">Agregar una variable</h3>\
            <button type="button" class="cq-icon-btn" @click="closeVariablePicker" aria-label="Cerrar"><i class="bi bi-x-lg"></i></button>\
        </div>\
        <input v-model="varPickerQuery" @input="onVariableQuery" class="form-control form-control-sm mb-2" placeholder="Nombre, código o concepto SNOMED…" autofocus />\
        <div v-if="varPickerLoading" class="cq-text-sm cq-text-muted"><span class="spinner-border spinner-border-sm me-1"></span> Buscando…</div>\
        <div v-else-if="varPickerError" class="cq-text-sm text-danger">{{ varPickerError }}</div>\
        <div v-else-if="!varPickerResults.length" class="cq-text-sm cq-text-muted">No hay variables que coincidan.</div>\
        <ul v-else class="list-unstyled mb-0">\
            <li v-for="v in varPickerResults" :key="v.id" class="border-bottom py-2 d-flex justify-content-between align-items-center gap-2" style="cursor:pointer;" @click="addQuestionFromVariable(v)">\
                <div>\
                    <div>{{ v.name }}</div>\
                    <div class="cq-text-xs cq-text-muted">\
                        {{ typeLabel(v.type) }}<template v-if="v.unit"> · {{ v.unit }}</template>\
                        <template v-if="v.snomedCode"> · <code>{{ v.snomedCode }}</code> {{ v.snomedDisplay }}</template>\
                        <template v-else> · sin codificar</template>\
                    </div>\
                </div>\
                <i class="bi bi-plus-lg"></i>\
            </li>\
        </ul>\
    </div>\
</div>\
<div v-if="importOpen" style="position:fixed; inset:0; background:rgba(0,0,0,.45); z-index:1050; display:flex; align-items:center; justify-content:center; padding:16px;" @click.self="closeImport">\
    <div class="cq-card" style="max-width:560px; width:100%; max-height:85vh; overflow:auto;">\
        <h2 class="cq-section-title" style="margin-top:0;">Importar preguntas de otro cuestionario</h2>\
        <p class="cq-text-sm cq-text-muted">Se agregan las preguntas del cuestionario elegido (misma especialidad) a la estructura actual. Después podés editar, reordenar o quitar lo que quieras.</p>\
        <div v-if="importLoading" class="cq-text-sm cq-text-muted py-3"><span class="spinner-border spinner-border-sm me-1"></span> Cargando…</div>\
        <div v-else-if="importError" class="cq-text-sm text-danger py-2">{{ importError }}</div>\
        <div v-else-if="!importCandidates.length" class="cq-text-sm cq-text-muted py-3">No hay otros cuestionarios de esta especialidad para importar.</div>\
        <ul v-else class="list-unstyled mb-0">\
            <li v-for="c in importCandidates" :key="c.id" class="d-flex align-items-center gap-2 py-2 border-bottom">\
                <div class="flex-fill">\
                    <div class="cq-text-sm fw-semibold">{{ c.title }}</div>\
                    <div class="cq-text-xs cq-text-muted">v{{ c.version }} · {{ estadoLabel(c.status) }}</div>\
                </div>\
                <button class="btn btn-sm btn-primary" :disabled="importBusy" @click="importFrom(c)"><i class="bi bi-box-arrow-in-down me-1"></i> Importar</button>\
            </li>\
        </ul>\
        <div class="d-flex justify-content-end gap-2 border-top pt-3 mt-3">\
            <button class="btn btn-outline-secondary" @click="closeImport">Cerrar</button>\
        </div>\
    </div>\
</div>\
<div v-if="staleVariables.length" class="alert alert-info cq-text-sm" role="status">\
    <i class="bi bi-info-circle me-1"></i>\
    <strong>{{ staleVariables.length }}</strong> pregunta(s) usan una versión anterior de su variable.\
    <ul class="mb-0 mt-1 ps-3">\
        <li v-for="q in staleVariables" :key="q.linkId">{{ q.text }} <span class="cq-text-xs cq-text-muted">({{ q.variableName }})</span>\
            <button v-if="!readOnly" type="button" class="btn btn-link btn-sm p-0 ms-1 align-baseline" :disabled="updatingVariable[q.linkId]" @click="updateToCurrentVersion(q)">Actualizar a v{{ q.variableCurrentRevision }}</button>\
        </li>\
    </ul>\
</div>\
<div v-if="issues.length" class="cq-card mb-2" style="border-left:3px solid var(--cq-warning);">\
    <div class="d-flex align-items-start gap-2">\
        <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-warning);"></i>\
        <div class="flex-fill">\
            <div class="cq-text-sm"><strong>Hay problemas en la estructura del cuestionario.</strong></div>\
            <ul class="cq-text-sm cq-text-muted mb-0 mt-1 ps-3">\
                <li v-for="(i, ix) in issues" :key="ix">{{ i.detail }}</li>\
            </ul>\
            <button v-if="conditionIssues.length" class="btn btn-sm btn-outline-secondary-cq mt-2" @click="fixIssues"><i class="bi bi-wrench me-1"></i> Quitar las condiciones que no se pueden cumplir</button>\
        </div>\
    </div>\
</div>\
<div v-if="isPublished && !conflict" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-info);">\
    <i class="bi bi-info-circle" style="color:var(--cq-info);"></i>\
    <span class="cq-text-sm">Este cuestionario está <strong>publicado</strong>. Los cambios se guardan como pendientes: los clínicos siguen viendo la versión publicada hasta que publique una nueva.</span>\
</div>\
<div v-if="isArchived && !conflict" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-warning);">\
    <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-warning);"></i>\
    <span class="cq-text-sm">Este cuestionario está <strong>archivado</strong> y es de sólo lectura: no se puede editar ni guardar. Para reactivarlo, publíquelo de nuevo desde el listado de Cuestionarios.</span>\
</div>\
<div v-if="!canEdit && !isArchived" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-info);">\
    <i class="bi bi-eye" style="color:var(--cq-info);"></i>\
    <span class="cq-text-sm">Estás viendo este cuestionario en <strong>modo lectura</strong>: tu rol no permite editarlo.</span>\
</div>\
<ol class="cq-timeline mb-3" aria-label="Pasos para crear un cuestionario">\
    <li v-for="(s, i) in STEPS" :key="s.key"\
        class="cq-timeline__item"\
        :class="[\'is-\' + stepStatus(i)]"\
        :aria-current="step === s.key ? \'step\' : null">\
        <button type="button" class="cq-timeline__dot"\
                @click="gotoStep(s.key)"\
                :disabled="!stepReachable(i) && step !== s.key"\
                :aria-label="(i + 1) + \'. \' + s.label + (stepStatus(i) === \'locked\' ? \' (bloqueado)\' : \'\')"\
                :title="stepStatus(i) === \'locked\' ? \'Completá los pasos anteriores para desbloquear\' : s.label">\
            <i v-if="isStepDone(i) && step !== s.key" class="bi bi-check-lg"></i>\
            <i v-else-if="stepStatus(i) === \'locked\'" class="bi bi-lock-fill"></i>\
            <span v-else>{{ i + 1 }}</span>\
        </button>\
        <div class="cq-timeline__label">\
            <div class="cq-timeline__title">{{ s.label }}</div>\
            <div class="cq-timeline__sub"><i :class="\'bi \' + s.icon"></i></div>\
        </div>\
    </li>\
</ol>\
<div class="cq-wizard-nav mb-3">\
    <div class="cq-wizard-help__nav">\
        <button class="btn btn-sm btn-outline-secondary-cq" :disabled="stepIndex === 0" @click="stepPrev"><i class="bi bi-chevron-left"></i> Anterior</button>\
        <button class="btn btn-sm btn-secondary" :disabled="!canGoNext" @click="stepNext" :title="nextBlockedReason">Siguiente <i class="bi bi-chevron-right"></i></button>\
    </div>\
</div>\
\
<div v-if="step === \'meta\'">\
    <div class="cq-card">\
        <h3 class="cq-section-title mt-0">Datos generales</h3>\
        <div class="row g-3">\
            <div class="col-md-8">\
                <label class="form-label cq-text-sm">Título del cuestionario <span style="color:var(--cq-danger);">*</span></label>\
                <input v-model="qTitle" class="form-control" :class="{ \'is-invalid\': (qTitleTouched || !isNew) && !qTitle.trim() }" placeholder="Ej: Cardio basal" required @blur="qTitleTouched = true" />\
                <p v-if="(qTitleTouched || !isNew) && !qTitle.trim()" class="cq-text-xs mt-1 mb-0" style="color:var(--cq-danger);">Obligatorio: sin título el cuestionario no se puede guardar.</p>\
            </div>\
            <div class="col-md-4">\
                <label class="form-label cq-text-sm">Especialidad</label>\
                <!-- data-cq-noenhance: cq-select-enhance.js clona las <option> una sola vez\
                     al cargar la página (DOMContentLoaded) y no se entera de que Vue las\
                     repuebla después con /api/specialties -- el usuario ve para siempre\
                     "Cargando…" + "(fuera de catálogo)", que es la foto de ANTES del fetch. -->\
                <select v-model="specialtyCode" class="form-select" :disabled="specialtiesLoading" data-cq-noenhance>\
                    <option v-if="specialtiesLoading" value="">Cargando…</option>\
                    <option v-if="!specialtiesLoading && !specialtyCode" value="" disabled>Elegí una especialidad</option>\
                    <option v-for="s in specialties" :key="s.code" :value="s.code">{{ s.name }}</option>\
                    <option v-if="specialtyCode && !specialties.some(function(s){return s.code===specialtyCode;})" :value="specialtyCode">{{ specialtyCode }} (fuera de catálogo)</option>\
                </select>\
                <p v-if="specialtiesError" class="cq-text-xs mt-1 mb-0" style="color:var(--cq-danger);">{{ specialtiesError }}</p>\
            </div>\
            <hr class="my-2" />\
            <h4 class="cq-text-sm fw-semibold mb-2">Codificación clínica del cuestionario <span class="cq-text-xs cq-text-muted fw-normal">(opcional)</span></h4>\
            <div class="row g-2">\
                <div class="col-7">\
                    <label class="form-label cq-text-sm">Término clínico</label>\
                    <div class="input-group input-group-sm">\
                        <input v-model="questionnaireCoding.term" class="form-control form-control-sm" placeholder="ej: control cardiológico" />\
                        <button class="btn btn-outline-primary" type="button" @click="questionnaireSnomed.search" :disabled="questionnaireSnomed.searching" title="Buscar el término en SNOMED CT">\
                            <span v-if="questionnaireSnomed.searching" class="spinner-border spinner-border-sm"></span>\
                            <template v-else><i class="bi bi-search"></i> Buscar</template>\
                        </button>\
                    </div>\
                    <div v-if="questionnaireSnomed.error" class="cq-text-xs text-danger mt-1">{{ questionnaireSnomed.error }}</div>\
                    <ul v-if="questionnaireSnomed.open && questionnaireSnomed.results.length" class="list-unstyled border rounded mt-1 mb-0" style="max-height:200px; overflow:auto;">\
                        <li v-for="r in questionnaireSnomed.results" :key="r.code" class="px-2 py-1 cq-text-sm" style="cursor:pointer;" @click="questionnaireSnomed.pick(r)"\
                            onmouseover="this.style.background=\'var(--cq-primary-100)\'" onmouseout="this.style.background=\'transparent\'">\
                            {{ r.display }} <span class="cq-text-xs cq-text-muted">{{ r.code }}</span>\
                        </li>\
                    </ul>\
                    <div v-else-if="questionnaireSnomed.open && !questionnaireSnomed.searching && !questionnaireSnomed.error" class="cq-text-xs cq-text-muted mt-1">Sin resultados.</div>\
                </div>\
                <div class="col-5">\
                    <label class="form-label cq-text-sm">Código SNOMED CT</label>\
                    <input v-model="questionnaireCoding.code" class="form-control form-control-sm" placeholder="ej: 302760001" />\
                    <div v-if="questionnaireCoding.codeDisplay" class="cq-text-xs mt-1">\
                        <i class="bi bi-check-circle-fill text-success"></i> {{ questionnaireCoding.codeDisplay }}\
                        <a href="javascript:" class="ms-1" @click="questionnaireSnomed.clear">quitar</a>\
                    </div>\
                </div>\
            </div>\
        </div>\
    </div>\
    <div class="cq-card mt-3" v-if="!readOnly">\
        <h3 class="cq-section-title mt-0">Importar desde otro cuestionario</h3>\
        <button class="btn btn-sm btn-outline-primary" @click="openImport"><i class="bi bi-box-arrow-in-down me-1"></i> Importar preguntas de otro cuestionario</button>\
    </div>\
</div>\
\
<div v-if="step === \'structure\'" class="row g-3">\
    <div class="col-lg-6">\
        <div class="d-flex justify-content-between align-items-center mb-2" style="min-height:31px;">\
            <h3 class="m-0">Estructura</h3>\
            <span class="cq-text-xs" style="color:var(--cq-warning);" v-if="dirty">Cambios sin guardar</span>\
            <span class="cq-text-xs cq-text-muted" v-else-if="lastSaved">Guardado {{ lastSaved }}</span>\
        </div>\
        <div v-for="(g, gi) in groups" :key="gi" class="cq-card mb-2"\
             @dragover.prevent="onGroupDragOver(g)" @drop.prevent="onSectionDrop(g)"\
             :style="{ outline: (dropTarget === g && drag && drag.type === \'group\') ? \'2px dashed var(--cq-primary)\' : \'none\' }">\
            <div class="d-flex align-items-center gap-2 mb-2">\
                <i class="bi bi-grip-vertical cq-text-muted" style="cursor:grab;" draggable="true"\
                   @dragstart="onGroupDragStart(g, $event)" @dragend="onDragEnd"\
                   :aria-label="\'Arrastrar para reordenar la sección \' + g.title" title="Arrastrar para reordenar la sección"></i>\
                <i class="bi bi-folder text-primary"></i>\
                <input v-model="g.title" class="form-control form-control-sm fw-semibold" :class="{ \'is-invalid\': g.titleTouched && !g.title.trim() }" style="max-width:320px;" placeholder="Nombre de la sección" required @blur="g.titleTouched = true" :title="(g.titleTouched && !g.title.trim()) ? \'Obligatorio: cargá un nombre para la sección\' : \'\'" />\
                <span class="cq-text-xs cq-text-muted">{{ g.questions.length }} preguntas</span>\
                <button type="button" class="cq-icon-btn cq-icon-btn--danger ms-auto" @click="removeGroup(g)" :aria-label="\'Eliminar sección \' + g.title" :title="\'Eliminar sección \' + g.title">\
                    <i class="bi bi-trash cq-icon-btn__idle"></i>\
                    <i class="bi bi-trash-fill cq-icon-btn__hover"></i>\
                </button>\
            </div>\
            <ul class="list-unstyled mb-2">\
                <li v-for="q in g.questions" :key="q.linkId"\
                    draggable="true"\
                    @dragstart.stop="onQuestionDragStart(g, q, $event)"\
                    @dragover.prevent.stop="onQuestionDragOver(q)"\
                    @drop.prevent.stop="onQuestionDrop(g, q)"\
                    @dragend="onDragEnd"\
                    class="cq-question-card"\
                    :class="{ \'is-selected\': selected && selected.question === q, \'is-drop-target\': dropTarget === q && drag && drag.type === \'question\' }"\
                    @click="selectQuestion(g, q)">\
                    <i class="bi bi-grip-vertical cq-question-card__grip"></i>\
                    <span class="cq-question-card__text">{{ q.text || \'Nueva pregunta\' }}<span v-if="q.required" class="cq-question-card__req">*</span></span>\
                    <button type="button" class="cq-icon-btn cq-icon-btn--danger" @click.stop="removeQuestion(g, q)" :aria-label="\'Eliminar \' + q.text" :title="\'Eliminar pregunta\'">\
                        <i class="bi bi-trash cq-icon-btn__idle"></i>\
                        <i class="bi bi-trash-fill cq-icon-btn__hover"></i>\
                    </button>\
                </li>\
            </ul>\
            <button class="btn btn-sm btn-primary" @click="openVariablePicker(g)"><i class="bi bi-plus-lg me-1"></i>Agregar variable</button>\
        </div>\
        <button class="btn btn-primary w-100" @click="addGroup"><i class="bi bi-plus-lg me-1"></i> Agregar sección</button>\
    </div>\
\
    <div class="col-lg-6">\
        <div class="d-flex justify-content-between align-items-center mb-2" style="min-height:31px;">\
            <h3 class="m-0">Propiedades</h3>\
            <span v-if="!selected" class="cq-text-xs cq-text-muted">Elegí una pregunta</span>\
        </div>\
        <div v-if="selected" class="cq-card" style="position:sticky; top:84px;">\
            <div class="mb-2">\
                <label class="form-label cq-text-sm">Texto</label>\
                <input v-model="selected.question.text" class="form-control form-control-sm" placeholder="Nueva pregunta" />\
            </div>\
            <div class="form-check mb-2">\
                <input v-model="selected.question.required" type="checkbox" class="form-check-input" id="reqCk" />\
                <label class="form-check-label cq-text-sm" for="reqCk">Obligatoria</label>\
            </div>\
            <hr />\
            <dl class="row cq-text-sm mb-0">\
                <dt class="col-5">Variable</dt><dd class="col-7">{{ selected.question.variableName || \'—\' }}</dd>\
                <dt class="col-5">Tipo</dt><dd class="col-7">{{ typeLabel(selected.question.type) }}</dd>\
                <template v-if="selected.question.unit"><dt class="col-5">Unidad</dt><dd class="col-7">{{ selected.question.unit }}</dd></template>\
                <template v-if="selected.question.min !== undefined || selected.question.max !== undefined">\
                    <dt class="col-5">Rango</dt>\
                    <dd class="col-7">{{ selected.question.min === undefined ? \'−∞\' : selected.question.min }} – {{ selected.question.max === undefined ? \'∞\' : selected.question.max }}</dd>\
                </template>\
                <template v-if="selected.question.maxLength"><dt class="col-5">Largo máximo</dt><dd class="col-7">{{ selected.question.maxLength }}</dd></template>\
                <template v-if="selected.question.accept"><dt class="col-5">Formatos</dt><dd class="col-7">{{ selected.question.accept }}</dd></template>\
                <template v-if="selected.question.options && selected.question.options.length">\
                    <dt class="col-5">Opciones</dt>\
                    <dd class="col-7"><span v-for="(opt, oi) in selected.question.options" :key="oi" class="cq-chip me-1">{{ opt }}</span></dd>\
                </template>\
                <dt class="col-5">SNOMED CT</dt>\
                <dd class="col-7">\
                    <template v-if="selected.question.code"><code>{{ selected.question.code }}</code> <span class="cq-text-xs cq-text-muted">{{ selected.question.codeDisplay }}</span></template>\
                    <span v-else class="cq-text-muted">Sin codificar</span>\
                </dd>\
            </dl>\
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
                        <option v-for="o in conditionOps" :key="o.op" :value="o.op">{{ o.label }}</option>\
                    </select>\
                </div>\
                <div class="col-8">\
                    <select v-if="conditionSource && conditionSource.type === \'boolean\'" class="form-select form-select-sm"\
                            :value="String(selected.question.enableWhen.value)" @change="onConditionValue($event.target.value)">\
                        <option value="" disabled>Elegí…</option>\
                        <option value="true">Sí</option>\
                        <option value="false">No</option>\
                    </select>\
                    <select v-else-if="conditionSource && conditionSource.options && conditionSource.options.length" class="form-select form-select-sm"\
                            :value="selected.question.enableWhen.value" @change="onConditionValue($event.target.value)">\
                        <option value="" disabled>Elegí…</option>\
                        <option v-for="opt in conditionSource.options" :key="opt" :value="opt">{{ opt }}</option>\
                    </select>\
                    <input v-else class="form-control form-control-sm" :value="selected.question.enableWhen.value"\
                           :type="conditionSource && conditionSource.type === \'date\' ? \'date\' : (conditionSource && conditionSource.type === \'dateTime\' ? \'datetime-local\' : \'text\')"\
                           @input="onConditionValue($event.target.value)" placeholder="valor esperado" />\
                </div>\
            </div>\
            </template>\
\
        </div>\
        <div v-else class="cq-card cq-text-sm cq-text-muted">Seleccione una pregunta para editar sus propiedades.</div>\
    </div>\
</div>\
\
<div v-if="step === \'preview\'">\
    <div v-if="!groups.length || !allQuestionsFlat.length" class="cq-card cq-text-sm cq-text-muted">No hay preguntas para previsualizar. Volvé al paso <a href="javascript:" @click="gotoStep(\'structure\')">Estructura</a>.</div>\
    <div v-else>\
        <div class="cq-card">\
            <div style="border-bottom:1px solid var(--cq-neutral-200); padding-bottom:16px;">\
                <h2 class="mt-0 mb-0">{{ qTitle }}</h2>\
            </div>\
            <div v-for="(g, gi) in groups" :key="gi">\
                <h3 class="cq-section-title"><i class="bi bi-folder me-1"></i>{{ g.title }}</h3>\
            <div v-if="!g.questions.length" class="cq-text-xs cq-text-muted">Sección sin preguntas.</div>\
            <div v-for="q in g.questions" :key="q.linkId" class="mb-3">\
                <label class="form-label cq-text-sm fw-semibold">{{ q.text }} <span v-if="q.required" class="text-danger">*</span></label>\
                <input v-if="[\'string\',\'text\'].indexOf(q.type) < 0 && [\'choice\',\'openChoice\',\'boolean\'].indexOf(q.type) < 0 && q.type !== \'attachment\'"\
                       class="form-control form-control-sm" :placeholder="q.type" disabled />\
                <input v-if="q.type === \'string\'" class="form-control form-control-sm" placeholder="Respuesta corta" disabled />\
                <textarea v-if="q.type === \'text\'" class="form-control form-control-sm" rows="2" placeholder="Respuesta larga" disabled></textarea>\
                <div v-if="q.type === \'boolean\'" class="form-check">\
                    <input class="form-check-input" type="checkbox" disabled />\
                    <label class="form-check-label cq-text-sm">Sí / No</label>\
                </div>\
                <div v-if="[\'choice\',\'openChoice\'].indexOf(q.type) >= 0">\
                    <div v-for="opt in (q.options || [])" :key="opt" class="form-check">\
                        <input class="form-check-input" :type="q.type === \'choice\' ? \'radio\' : \'checkbox\'" disabled />\
                        <label class="form-check-label cq-text-sm">{{ opt }}</label>\
                    </div>\
                </div>\
                <div v-if="q.type === \'attachment\'" class="cq-text-xs cq-text-muted"><i class="bi bi-paperclip"></i> Adjunto ({{ q.accept || \'imagen o PDF\' }})</div>\
                <div v-if="q.code" class="cq-text-xs cq-text-muted mt-1"><i class="bi bi-tag"></i> {{ q.code }} — {{ q.codeDisplay || q.term }}</div>\
                <div v-if="q.enableWhen" class="cq-text-xs cq-text-muted mt-1"><i class="bi bi-diagram-2"></i> Se muestra sólo si otra respuesta lo habilita.</div>\
            </div>\
            </div>\
        </div>\
    </div>\
</div>\
\
<div v-if="step === \'publish\'">\
    <div class="cq-card mb-2">\
        <h3 class="cq-section-title mt-0"><i class="bi bi-send me-1"></i>Publicación</h3>\
        <div v-if="publishBlockers.length" class="cq-card mb-0" style="border-left:3px solid var(--cq-warning); background:var(--cq-neutral-50);">\
            <div class="cq-text-sm"><strong>Antes de publicar, resolvé:</strong></div>\
            <ul class="cq-text-sm cq-text-muted mb-0 mt-1 ps-3">\
                <li v-for="(b, bi) in publishBlockers" :key="bi">{{ b }}</li>\
            </ul>\
        </div>\
        <div v-else class="cq-card mb-0" style="border-left:3px solid var(--cq-success); background:var(--cq-neutral-50);">\
            <div class="cq-text-sm"><i class="bi bi-check-circle-fill" style="color:var(--cq-success);"></i> Todo listo para publicar.</div>\
            <div v-if="isPublished" class="cq-text-xs cq-text-muted mt-1">Ya hay una versión publicada. Publicar de nuevo crea una versión posterior.</div>\
        </div>\
    </div>\
</div>\
\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-editor', QuestionnaireEditor);
})();
