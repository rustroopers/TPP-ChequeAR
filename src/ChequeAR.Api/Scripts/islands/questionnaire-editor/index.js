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
            // Si ya hay cambios guardados sin publicar de una sesión anterior (el server
            // lo calcula comparando con la versión publicada). "dirty" sólo sabe de esta
            // sesión, así que publish() necesita este flag además para no bloquear una
            // publicación legítima cuando recién se abre la página.
            var hasPendingChanges = Vue.ref(!!props.hasPendingChanges);
            // Un archivado no se puede editar: el backend lo rechaza (Retired questionnaires
            // cannot be edited). Sin esto el editor lo trataba como un borrador cualquiera
            // -autosave incluido- y cada guardado terminaba en un 500 silencioso.
            var isArchived = Vue.ref(props.status === 'archived');
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
                            maxLength: typeof q.maxLength === 'number' ? q.maxLength : undefined,
                            pattern: q.pattern,
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
            function answerKind(type) {
                if (type === 'boolean') return 'boolean';
                if (['integer', 'decimal', 'scale'].indexOf(type) >= 0) return 'number';
                if (['choice', 'openChoice'].indexOf(type) >= 0) return 'choice';
                if (type === 'attachment') return 'attachment';   // no comparable: familia propia
                return 'text';
            }

            function onTypeChange(newType) {
                var q = selected.value.question;
                var previo = q.type;
                if (!newType || newType === previo) return;
                q.type = newType;

                // Las opciones pertenecen a los tipos de opción. Si quedan colgadas al
                // salir de esa familia, buildPayload las sigue mandando y el cuestionario
                // se publica con answerOption en una pregunta de texto.
                if (answerKind(newType) === 'choice') {
                    if (!(q.options && q.options.length)) {
                        q.options = ['Opción 1', 'Opción 2'];
                        q.optionsText = q.options.join('\n');
                    }
                } else if (answerKind(previo) === 'choice') {
                    q.options = undefined;
                    q.optionsText = '';
                }

                if (answerKind(newType) !== answerKind(previo)) {
                    dropConditions(dependentsOf([q.linkId]),
                        'dependían de "' + q.text + '", que cambió de tipo');
                }
                dirty.value = true;
            }

            // Condiciones ya rotas en lo que vino de la base: el árbol pudo haberse
            // guardado así antes de que existiera esta capa. Se listan en un cartel en vez
            // de arreglarse solas, porque tocar datos que la persona no editó en esta
            // sesión no es una decisión del editor.
            // Cualquier chequeo estructural nuevo se cuelga de esta lista.
            var issues = Vue.computed(function () {
                var known = {};
                allQuestions().forEach(function (q) { if (q.linkId) known[q.linkId] = true; });
                return allQuestions()
                    .filter(function (q) {
                        return q.enableWhen && q.enableWhen.question && !known[q.enableWhen.question];
                    })
                    .map(function (q) {
                        return { question: q, detail: '"' + q.text + '" depende de una pregunta que ya no existe: nunca se va a mostrar.' };
                    });
            });

            function fixIssues() {
                dropConditions(
                    issues.value.map(function (i) { return i.question; }),
                    'la pregunta de la que dependían ya no existe');
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

            function getToken() {
                var meta = document.querySelector('meta[name="cq-token"]');
                return meta ? meta.getAttribute('content') : null;
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
            var snomedResults = Vue.ref([]);
            var snomedSearching = Vue.ref(false);
            var snomedError = Vue.ref(null);
            var snomedOpen = Vue.ref(false);
            var snomedAbort = null;
            var snomedDebounce;
            // Al cambiar de pregunta, el término "cambia" sin que nadie tipee: no queremos
            // disparar una búsqueda por eso. Esta bandera suprime ese primer disparo.
            var snomedSuppress = false;
            // Backoff ante 429: hasta este instante (ms epoch) no autocompletamos solos,
            // para no seguir golpeando un server que ya nos está limitando.
            var snomedCooldownUntil = 0;

            // Cambiar de pregunta no debe arrastrar los resultados de la anterior.
            Vue.watch(selected, function () {
                snomedResults.value = []; snomedOpen.value = false; snomedError.value = null;
                clearTimeout(snomedDebounce);
                snomedSuppress = true;
                Vue.nextTick(function () { snomedSuppress = false; });
            });

            // Autosugerencia asíncrona: mientras se edita el término clínico buscamos solos
            // (debounce) para no golpear el terminológico en cada tecla. Escribir el
            // enunciado ya no dispara búsquedas: sólo el término las provoca.
            Vue.watch(function () {
                return selected.value && selected.value.question ? selected.value.question.term : null;
            }, function (term) {
                if (snomedSuppress) return;   // el cambio vino de seleccionar otra pregunta
                clearTimeout(snomedDebounce);
                if (!term || term.trim().length < 3) {
                    snomedResults.value = []; snomedOpen.value = false; snomedError.value = null;
                    return;
                }
                if (Date.now() < snomedCooldownUntil) return;   // en backoff por 429
                snomedDebounce = setTimeout(searchSnomed, 600);
            });

            function searchSnomed() {
                var q = selected.value && selected.value.question;
                var term = q ? (q.term || '').trim() : '';
                snomedError.value = null;
                if (term.length < 3) {
                    snomedError.value = 'Escribí al menos 3 caracteres en el término clínico (ej: "dolor torácico").';
                    snomedResults.value = []; snomedOpen.value = true;
                    return;
                }
                var token = getToken();
                if (!token) { toast('No hay sesión: no se puede consultar SNOMED (token ausente).', 'error'); return; }

                // Si el usuario busca de nuevo con una consulta en curso, cancelamos la vieja.
                if (snomedAbort) snomedAbort.abort();
                snomedAbort = ('AbortController' in window) ? new AbortController() : null;
                snomedSearching.value = true;

                fetch('/fhir/r5/ValueSet/$expand?count=15&filter=' + encodeURIComponent(term), {
                    headers: { 'Authorization': 'Bearer ' + token },
                    signal: snomedAbort ? snomedAbort.signal : undefined
                }).then(function (res) {
                    return res.json().catch(function () { return null; }).then(function (body) {
                        if (res.status === 429) {
                            // El server nos limita: entramos en cooldown y frenamos el autosugerir.
                            var secs = (body && body.retryAfter) || 8;
                            snomedCooldownUntil = Date.now() + secs * 1000;
                            throw new Error((body && body.error) ||
                                'Demasiadas consultas a SNOMED. Reintentá en unos segundos.');
                        }
                        if (!res.ok) throw new Error((body && body.error) || ('HTTP ' + res.status));
                        return body;
                    });
                }).then(function (data) {
                    snomedResults.value = (data && data.results) || [];
                    snomedOpen.value = true;
                }).catch(function (err) {
                    if (err.name === 'AbortError') return;
                    snomedError.value = err.message;
                    snomedResults.value = []; snomedOpen.value = true;
                }).then(function () {
                    snomedSearching.value = false;
                });
            }

            function pickSnomed(r) {
                var q = selected.value.question;
                q.code = r.code;
                q.codeDisplay = r.display;
                snomedOpen.value = false;
                snomedResults.value = [];
                dirty.value = true;
            }

            // Saca el código pero conserva el término: qué mide la pregunta no deja de ser
            // cierto porque se descarte el concepto elegido, y así se puede volver a buscar
            // sin reescribirlo.
            function clearSnomed() {
                var q = selected.value.question;
                q.code = undefined;
                q.codeDisplay = undefined;
                dirty.value = true;
            }
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

            // Vocabulario de las islas -> vocabulario del estándar.
            // OJO: R5 eliminó 'choice' y 'open-choice' (eran de R4). Ambos son 'coding', y
            // lo que los distingue es answerConstraint.
            function fhirType(t) {
                switch (t) {
                    case 'choice':
                    case 'openChoice': return 'coding';
                    case 'scale': return 'integer';   // FHIR no tiene tipo escala
                    default: return t;
                }
            }
            function answerConstraint(t) {
                if (t === 'choice') return 'optionsOnly';
                if (t === 'openChoice') return 'optionsOrString';
                return null;
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

                if ((q.type === 'string' || q.type === 'text') && q.pattern)
                    out.push(ext(EXT + 'regex', 'valueString', q.pattern));

                // El término de autoría no tiene lugar nativo en FHIR y hasta la feature 009
                // no viajaba: se guarda aunque todavía no haya código elegido, porque deja
                // asentado qué mide la pregunta. Sin esta extensión se pierde en CADA guardado.
                var term = (q.term || '').trim();
                if (term) out.push(ext(CQ_EXT + 'question-term', 'valueString', term));

                return out;
            }

            // El answer[x] de la condición se tipa según la pregunta REFERENCIADA, no según
            // la que declara la condición.
            function buildEnableWhen(q, porLinkId) {
                if (!q.enableWhen || !q.enableWhen.question) return null;

                var origen = porLinkId[q.enableWhen.question];
                var valor = q.enableWhen.value;
                var entrada = { question: q.enableWhen.question, operator: q.enableWhen.op || '=' };
                var tipo = origen ? origen.type : 'string';

                if (tipo === 'boolean') entrada.answerBoolean = (valor === true || valor === 'true');
                else if (esEntero(tipo)) entrada.answerInteger = parseInt(valor, 10) || 0;
                else if (tipo === 'decimal') entrada.answerDecimal = parseFloat(valor) || 0;
                else if (tipo === 'choice' || tipo === 'openChoice')
                    entrada.answerCoding = { code: String(valor), display: String(valor) };
                else entrada.answerString = valor == null ? null : String(valor);

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
                    status: 'active',
                    title: qTitle.value,
                    name: qTitle.value,
                    subjectType: ['Patient'],
                    item: groups.value.map(function (g, gi) {
                        return {
                            linkId: g.linkId || ('seccion-' + (gi + 1)),
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
            function save(options) {
                var explicit = !!(options && options.explicit);
                if (saving.value) return Promise.resolve(false);
                // Nada cambió desde el último guardado: no hay razón para pegarle al
                // server. Un cuestionario nuevo siempre tiene que crearse la primera vez,
                // aunque todavía no se haya tocado nada.
                if (!isNew.value && !dirty.value) {
                    if (explicit) toast('No hay cambios para guardar', 'info');
                    return Promise.resolve(true);
                }
                // El backend rechaza cualquier escritura sobre un archivado: cortar acá
                // evita el viaje al server (que hoy devolvía 500) y explica por qué.
                if (isArchived.value) {
                    if (explicit) toast('Este cuestionario está archivado: no se puede editar. Publicalo de nuevo desde el listado para reactivarlo.', 'error');
                    return Promise.resolve(false);
                }
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
                    'Content-Type': 'application/fhir+json',
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
                            var err = new Error(diagnostico(body, 'Conflicto de edición'));
                            err.isConflict = true;
                            throw err;
                        });
                    }
                    if (!res.ok) { throw new Error('HTTP ' + res.status); }
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
                    lastSaved.value = new Date().toLocaleTimeString();
                    dirty.value = false;
                    // Se guardó un cambio sobre un publicado: a partir de ahora hay que
                    // dejar publicar aunque "dirty" ya esté en false (recién se limpió arriba).
                    if (isPublished.value) hasPendingChanges.value = true;
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

            // Publicar desde el editor. Congela en una versión lo que está en la base, no lo
            // que está en pantalla: por eso exige que no haya cambios sin guardar (ver el
            // guard en publish()), en vez de guardar por su cuenta.
            // Descarta lo local y vuelve a cargar lo que hay en el server. Es la salida
            // honesta del conflicto: no hay merge automático de estructuras.
            function reloadFromServer() {
                dirty.value = false;   // que beforeunload no pregunte: la decisión ya se tomó
                window.location.reload();
            }

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
                    toast('Resolvé el conflicto de edición antes de publicar.', 'error');
                    return;
                }
                // Publicar congela la estructura para los clínicos: una condición rota se
                // vuelve una pregunta que nadie va a poder ver, y ya no hay forma de
                // corregir esa versión.
                if (issues.value.length) {
                    toast('Hay ' + issues.value.length + ' condición(es) que apuntan a preguntas inexistentes. Resolvelas antes de publicar.', 'error');
                    return;
                }
                // Publicar exige haber guardado antes: no queremos congelar en una versión
                // algo que sólo está en pantalla, y el guardado implícito de antes, hecho en
                // el mismo instante que la publicación, dejaba "editado" y "publicado" con la
                // misma marca de tiempo y se veían fuera de orden en auditoría. Guardar es
                // una decisión explícita y previa.
                if (dirty.value || isNew.value) {
                    toast('Guardá los cambios antes de publicar.', 'error');
                    return;
                }
                var token = getToken();
                if (!token) { toast('No hay sesión: no se puede publicar (token ausente).', 'error'); return; }

                publishing.value = true;
                fetch('/fhir/r5/Questionnaire/' + currentId.value + '/$publish', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token },
                    body: '{}'
                }).then(function (res) {
                    if (res.ok) return null;
                    // El API devuelve 422 con OperationOutcome cuando la publicación no es
                    // válida (una regla de negocio, o el perfil de conformidad).
                    return res.json().catch(function () { return null; }).then(function (body) {
                        throw new Error(diagnostico(body, 'HTTP ' + res.status));
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
            //  - uno publicado no se autoguarda nunca (ver isPublished);
            //  - uno archivado no se guarda nunca: el backend no lo permite (ver isArchived).
            var debounce;
            function markDirty() {
                dirty.value = true;
                if (isNew.value || isPublished.value || isArchived.value || conflict.value) return;
                clearTimeout(debounce);
                debounce = setTimeout(save, 2000);
            }
            Vue.watch(groups, markDirty, { deep: true });
            // Título y especialidad viven fuera de `groups`: sin este watcher, editar sólo
            // la cabecera no marcaba nada como sucio, así que no se autoguardaba y
            // beforeunload dejaba salir de la página perdiendo el cambio en silencio.
            Vue.watch([qTitle, specialtyCode], markDirty);

            // Sin autosave en publicados, salir de la página con cambios pendientes los
            // perdía en silencio. El navegador pide confirmación. En un archivado nada se
            // guarda nunca, así que preguntar sería confuso ("¿perder cambios?" cuando ya
            // sabemos que no se iban a guardar).
            function beforeUnload(e) {
                if (!dirty.value || isArchived.value) return;
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

                // Deshabilita los botones mientras hay un guardado/publicación en curso:
                // sin esto, clickear varias veces mientras el primer POST todavía no
                // volvió disparaba una creación por click (creating quedaba en true hasta
                // que resolvía el primero).
                // Publicar queda deshabilitado mientras haya cambios sin guardar (o sea un
                // cuestionario nuevo aún sin persistir): publicar exige guardar antes, así el
                // botón lo comunica y no sólo el toast del guard.
                Vue.watch([saving, publishing, dirty, isNew], function () {
                    var busy = saving.value || publishing.value;
                    if (btn) btn.disabled = busy;
                    if (pub) pub.disabled = busy || dirty.value || isNew.value;
                }, { immediate: true });
            });
            Vue.onUnmounted(function () {
                clearTimeout(debounce);
                clearTimeout(snomedDebounce);
                if (snomedAbort) snomedAbort.abort();
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
                isPublished: isPublished, isArchived: isArchived, publish: publish,
                conflict: conflict, reloadFromServer: reloadFromServer,
                issues: issues, fixIssues: fixIssues,
                conditionSources: conditionSources, conditionSource: conditionSource,
                onConditionQuestion: onConditionQuestion, onConditionValue: onConditionValue,
                snomedResults: snomedResults, snomedSearching: snomedSearching,
                snomedError: snomedError, snomedOpen: snomedOpen,
                searchSnomed: searchSnomed, pickSnomed: pickSnomed, clearSnomed: clearSnomed,
                onTypeChange: onTypeChange,
                selectQuestion: selectQuestion, addGroup: addGroup, addQuestion: addQuestion,
                removeQuestion: removeQuestion, removeGroup: removeGroup, save: saveExplicit,
                drag: drag, dropTarget: dropTarget,
                onQuestionDragStart: onQuestionDragStart, onGroupDragStart: onGroupDragStart,
                onDragEnd: onDragEnd, onQuestionDragOver: onQuestionDragOver, onGroupDragOver: onGroupDragOver,
                onQuestionDrop: onQuestionDrop, onSectionDrop: onSectionDrop
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
<div v-if="issues.length" class="cq-card mb-2" style="border-left:3px solid var(--cq-warning);">\
    <div class="d-flex align-items-start gap-2">\
        <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-warning);"></i>\
        <div class="flex-fill">\
            <div class="cq-text-sm"><strong>Hay condiciones que apuntan a preguntas que ya no existen.</strong></div>\
            <ul class="cq-text-sm cq-text-muted mb-0 mt-1 ps-3">\
                <li v-for="(i, ix) in issues" :key="ix">{{ i.detail }}</li>\
            </ul>\
            <button class="btn btn-sm btn-outline-secondary mt-2" @click="fixIssues"><i class="bi bi-wrench me-1"></i> Dejarlas siempre visibles</button>\
        </div>\
    </div>\
</div>\
<div v-if="isPublished && !conflict" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-info);">\
    <i class="bi bi-info-circle" style="color:var(--cq-info);"></i>\
    <span class="cq-text-sm">Este cuestionario está <strong>publicado</strong>. Los cambios se guardan como pendientes: los clínicos siguen viendo la versión publicada hasta que publiques una nueva.</span>\
</div>\
<div v-if="isArchived && !conflict" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-warning);">\
    <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-warning);"></i>\
    <span class="cq-text-sm">Este cuestionario está <strong>archivado</strong> y es de sólo lectura: no se puede editar ni guardar. Para reactivarlo, publicalo de nuevo desde el listado de Cuestionarios.</span>\
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
                        <option value="reuma">Reumatología</option>\
                    </select>\
                </div>\
            </div>\
        </div>\
        <div class="d-flex justify-content-between align-items-center mb-2">\
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
                <input v-model="g.title" class="form-control form-control-sm fw-semibold" style="max-width:320px;" />\
                <span class="cq-text-xs cq-text-muted">{{ g.questions.length }} preguntas</span>\
                <button class="btn btn-sm btn-link text-danger ms-auto" @click="removeGroup(g)" :aria-label="\'Eliminar sección \' + g.title"><i class="bi bi-trash"></i></button>\
            </div>\
            <ul class="list-unstyled mb-2">\
                <li v-for="q in g.questions" :key="q.linkId"\
                    draggable="true"\
                    @dragstart.stop="onQuestionDragStart(g, q, $event)"\
                    @dragover.prevent.stop="onQuestionDragOver(q)"\
                    @drop.prevent.stop="onQuestionDrop(g, q)"\
                    @dragend="onDragEnd"\
                    class="d-flex align-items-center gap-2 py-1 px-2"\
                    :style="{ borderRadius: \'6px\', cursor: \'pointer\', background: selected && selected.question === q ? \'var(--cq-primary-100)\' : \'transparent\', borderTop: (dropTarget === q && drag && drag.type === \'question\') ? \'2px solid var(--cq-primary)\' : \'2px solid transparent\' }"\
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
                <select :value="selected.question.type" @change="onTypeChange($event.target.value)" class="form-select form-select-sm">\
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
            <div v-if="[\'string\',\'text\'].indexOf(selected.question.type) >= 0">\
                <div class="mb-2">\
                    <label class="form-label cq-text-sm">Largo máximo <span class="cq-text-xs cq-text-muted fw-normal">(opcional)</span></label>\
                    <input v-model.number="selected.question.maxLength" type="number" min="1" class="form-control form-control-sm" placeholder="sin límite" />\
                </div>\
                <div class="mb-2">\
                    <label class="form-label cq-text-sm">Patrón <span class="cq-text-xs cq-text-muted fw-normal">(regex, opcional)</span></label>\
                    <input v-model="selected.question.pattern" class="form-control form-control-sm" placeholder="ej: ^[0-9]{7,8}$" />\
                    <p class="cq-text-xs cq-text-muted mt-1">La respuesta tiene que coincidir con esta expresión regular.</p>\
                </div>\
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
                <label class="form-label cq-text-sm">Término clínico</label>\
                <div class="input-group input-group-sm">\
                    <input v-model="selected.question.term" class="form-control form-control-sm" placeholder="ej: dolor torácico" />\
                    <button class="btn btn-outline-primary" type="button" @click="searchSnomed" :disabled="snomedSearching" title="Buscar el término en SNOMED CT">\
                        <span v-if="snomedSearching" class="spinner-border spinner-border-sm"></span>\
                        <template v-else><i class="bi bi-search"></i> Buscar</template>\
                    </button>\
                </div>\
                <div v-if="snomedError" class="cq-text-xs text-danger mt-1">{{ snomedError }}</div>\
                <ul v-if="snomedOpen && snomedResults.length" class="list-unstyled border rounded mt-1 mb-0" style="max-height:200px; overflow:auto;">\
                    <li v-for="r in snomedResults" :key="r.code" class="px-2 py-1 cq-text-sm" style="cursor:pointer;" @click="pickSnomed(r)"\
                        onmouseover="this.style.background=\'var(--cq-primary-100)\'" onmouseout="this.style.background=\'transparent\'">\
                        {{ r.display }} <span class="cq-text-xs cq-text-muted">{{ r.code }}</span>\
                    </li>\
                </ul>\
                <div v-else-if="snomedOpen && !snomedSearching && !snomedError" class="cq-text-xs cq-text-muted mt-1">Sin resultados.</div>\
            </div>\
            <div class="mb-2">\
                <label class="form-label cq-text-sm">Código SNOMED CT</label>\
                <input v-model="selected.question.code" class="form-control form-control-sm" placeholder="ej: 50373000" />\
                <div v-if="selected.question.codeDisplay" class="cq-text-xs mt-1">\
                    <i class="bi bi-check-circle-fill text-success"></i> {{ selected.question.codeDisplay }}\
                    <a href="javascript:" class="ms-1" @click="clearSnomed">quitar</a>\
                </div>\
                <p v-else class="cq-text-xs cq-text-muted mt-1">Elegí una sugerencia de arriba, o escribí el código a mano si ya lo sabés.</p>\
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
