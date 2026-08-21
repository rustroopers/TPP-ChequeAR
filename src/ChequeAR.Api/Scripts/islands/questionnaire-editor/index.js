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

            // Foto de la estructura tal como se cargó: es la BASE del merge de 3 vías cuando
            // otra persona guarda en el medio. Se re-ancla después de un merge exitoso.
            var baseGroups = JSON.parse(JSON.stringify(props.groups || []));

            // Estado del merge de conflictos (ver beginMerge/finishMerge más abajo).
            var merging = Vue.ref(false);
            var mergePlanAll = Vue.ref([]);     // plan completo (autos + conflictos)
            var mergeAutoCount = Vue.ref(0);
            var pendingTheirs = null;           // { concurrencyVersion, groups } de la otra persona
            var conflictEntries = Vue.computed(function () {
                return mergePlanAll.value.filter(function (e) { return e.needsChoice; });
            });

            // Los códigos coinciden con QuestionType del backend (bindeo por nombre,
            // case-insensitive) y con los que dibuja questionnaire-renderer. Antes de
            // sumar uno acá hay que poder renderizarlo allá, o queda una pregunta que se
            // puede crear pero no responder.
            var types = [
                { code: 'string',     label: 'Texto corto',     icon: 'bi-fonts' },
                { code: 'text',       label: 'Texto largo',     icon: 'bi-textarea-t' },
                // "Escala" no se ofrece más para preguntas nuevas: es lo mismo que un Número
                // con Min/Max. Se deja fuera de esta lista pero no del resto del editor/lectura,
                // para no romper las preguntas ya publicadas con este tipo.
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

            // ── Importar preguntas de otro cuestionario de la misma especialidad ──────────
            // Se puede crear un cuestionario a partir de otro: se traen sus secciones/preguntas
            // y se agregan a la estructura actual, que después se edita libremente. Cada pregunta
            // importada recibe un linkId NUEVO para no colisionar con las de acá, y sus
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
                if (isArchived.value) return;
                importOpen.value = true;
                importError.value = null;
                importCandidates.value = [];
                importLoading.value = true;
                var url = '/Questionnaires/ImportCandidates?specialty=' + encodeURIComponent(specialtyCode.value || 'general')
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

            function freshLinkId(used) {
                var id;
                do { id = 'q_' + Math.random().toString(36).slice(2, 7); } while (used[id]);
                used[id] = true;
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
            }

            function importFrom(candidate) {
                if (importBusy.value) return;
                importBusy.value = true;
                importError.value = null;
                fetch('/Questionnaires/EditorModel/' + candidate.id, { credentials: 'same-origin' })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (src) {
                        var srcGroups = src.groups || [];
                        var used = usedLinkIds();
                        // 1) linkId nuevo por pregunta + mapa viejo -> nuevo.
                        var remap = {};
                        var newGroups = srcGroups.map(function (g) {
                            var questions = (g.questions || []).map(function (q) {
                                var nl = freshLinkId(used);
                                if (q.linkId) remap[q.linkId] = nl;
                                return hydrateImportedQuestion(q, nl);
                            });
                            return { title: g.title || 'Sección importada', questions: questions };
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

            // Aviso que tiene que sobrevivir al window.location.reload() de publicar: un
            // toast disparado antes de recargar se pinta y desaparece en el mismo instante.
            // Queda anotado acá y lo levanta onMounted, ya con la versión nueva en pantalla.
            // Se guarda el id para no mostrar el aviso de un cuestionario sobre otro si la
            // recarga no llegó a ocurrir (por ejemplo, si se navegó a otra ficha).
            var PUBLISH_TOAST_KEY = 'cq.publishedToast';
            function stashPublishToast() {
                try { sessionStorage.setItem(PUBLISH_TOAST_KEY, String(currentId.value)); }
                catch (e) { /* sessionStorage no disponible: se pierde el aviso, sin más */ }
            }
            function flushPublishToast() {
                var pendiente = null;
                try {
                    pendiente = sessionStorage.getItem(PUBLISH_TOAST_KEY);
                    sessionStorage.removeItem(PUBLISH_TOAST_KEY);
                } catch (e) { return; }
                if (!pendiente || pendiente !== String(currentId.value)) return;
                toast('Cuestionario publicado exitosamente', 'success');
            }

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
            // Mismo criterio que diagnostico(): el detalle legible de un OperationOutcome
            // vive en issue[].diagnostics. Se declara aparte porque el buscador corre antes
            // de que diagnostico() este definido en el scope.
            function diagnosticoTerminologia(body, porOmision) {
                var issues = body && body.issue;
                if (!issues || !issues.length) return porOmision;
                return issues[0].diagnostics || porOmision;
            }

            // Fábrica de buscadores SNOMED. Un cuestionario tiene DOS puntos de codificación
            // —cada pregunta y el cuestionario entero— con idéntica mecánica: autocompletar
            // por término, elegir un concepto, quitarlo. En vez de duplicar toda la lógica
            // (debounce, backoff por 429, cancelación de la consulta en curso), se crea una
            // instancia por punto. `getActive()` devuelve el objeto sobre el que se escribe
            // code/codeDisplay (una pregunta, o el objeto de codificación del cuestionario);
            // ambos exponen las mismas tres propiedades term/code/codeDisplay.
            function createSnomedSearch(getActive) {
                var results = Vue.ref([]);
                var searching = Vue.ref(false);
                var error = Vue.ref(null);
                var open = Vue.ref(false);
                var abort = null;
                var debounce;
                // Backoff ante 429: hasta este instante (ms epoch) no autocompletamos solos,
                // para no seguir golpeando un server que ya nos está limitando.
                var cooldownUntil = 0;

                function reset() {
                    results.value = []; open.value = false; error.value = null;
                    clearTimeout(debounce);
                }

                // Autosugerencia asíncrona: mientras se edita el término clínico buscamos
                // solos (debounce) para no golpear el terminológico en cada tecla. El
                // enunciado de la pregunta NO dispara búsquedas: sólo el término.
                function schedule(term) {
                    clearTimeout(debounce);
                    if (!term || term.trim().length < 3) { reset(); return; }
                    if (Date.now() < cooldownUntil) return;   // en backoff por 429
                    debounce = setTimeout(search, 600);
                }

                function search() {
                    var target = getActive();
                    var term = target ? (target.term || '').trim() : '';
                    error.value = null;
                    if (term.length < 3) {
                        error.value = 'Escriba al menos 3 caracteres en el término clínico (ej: "dolor torácico").';
                        results.value = []; open.value = true;
                        return;
                    }
                    var token = getToken();
                    if (!token) { toast('No hay sesión: no se puede consultar SNOMED (token ausente).', 'error'); return; }

                    // Si el usuario busca de nuevo con una consulta en curso, cancelamos la vieja.
                    if (abort) abort.abort();
                    abort = ('AbortController' in window) ? new AbortController() : null;
                    searching.value = true;

                    fetch('/fhir/r5/ValueSet/$expand?count=15&filter=' + encodeURIComponent(term), {
                        headers: { 'Authorization': 'Bearer ' + token },
                        signal: abort ? abort.signal : undefined
                    }).then(function (res) {
                        return res.json().catch(function () { return null; }).then(function (body) {
                            if (res.status === 429) {
                                // El server nos limita: entramos en cooldown y frenamos el autosugerir.
                                // El tiempo de espera viene dentro del diagnostics del
                                // OperationOutcome: el estandar no tiene un campo para eso.
                                var texto = diagnosticoTerminologia(body,
                                    'Demasiadas consultas a SNOMED. Reintente en unos segundos.');
                                var m = /(\d+)\s*segundos/.exec(texto);
                                cooldownUntil = Date.now() + ((m ? parseInt(m[1], 10) : 8) * 1000);
                                throw new Error(texto);
                            }
                            if (!res.ok) throw new Error(diagnosticoTerminologia(body, 'HTTP ' + res.status));
                            return body;
                        });
                    }).then(function (data) {
                        results.value = (data && data.results) || [];
                        open.value = true;
                    }).catch(function (err) {
                        if (err.name === 'AbortError') return;
                        error.value = err.message;
                        results.value = []; open.value = true;
                    }).then(function () {
                        searching.value = false;
                    });
                }

                function pick(r) {
                    var target = getActive();
                    if (!target) return;
                    target.code = r.code;
                    target.codeDisplay = r.display;
                    open.value = false;
                    results.value = [];
                    dirty.value = true;
                }

                // Saca el código pero conserva el término: qué mide no deja de ser cierto
                // porque se descarte el concepto elegido, y así se puede volver a buscar sin
                // reescribirlo.
                function clear() {
                    var target = getActive();
                    if (!target) return;
                    target.code = undefined;
                    target.codeDisplay = undefined;
                    dirty.value = true;
                }

                function cancel() { clearTimeout(debounce); if (abort) abort.abort(); }

                // Envuelto en reactive (no un objeto plano): al exponerse anidado bajo
                // `questionSnomed`/`questionnaireSnomed` en el return de setup, Vue NO desenvuelve
                // los refs sueltos —`.searching` llegaría al template como el objeto Ref, siempre
                // truthy, y el spinner quedaría fijo—. reactive los desenvuelve al accederlos.
                return Vue.reactive({
                    results: results, searching: searching, error: error, open: open,
                    schedule: schedule, search: search, pick: pick, clear: clear, reset: reset, cancel: cancel
                });
            }

            // Buscador por PREGUNTA: escribe sobre la pregunta seleccionada.
            var questionSnomed = createSnomedSearch(function () {
                return selected.value && selected.value.question ? selected.value.question : null;
            });
            // Al cambiar de pregunta, el término "cambia" sin que nadie tipee: no queremos
            // disparar una búsqueda por eso. Esta bandera suprime ese primer disparo.
            var questionSnomedSuppress = false;
            // Cambiar de pregunta no debe arrastrar los resultados de la anterior.
            Vue.watch(selected, function () {
                questionSnomed.reset();
                questionSnomedSuppress = true;
                Vue.nextTick(function () { questionSnomedSuppress = false; });
            });
            Vue.watch(function () {
                return selected.value && selected.value.question ? selected.value.question.term : null;
            }, function (term) {
                if (questionSnomedSuppress) return;   // el cambio vino de seleccionar otra pregunta
                questionSnomed.schedule(term);
            });

            // Buscador del CUESTIONARIO completo: escribe sobre el objeto de codificación
            // reactivo. No hay "cambio de selección" que suprimir; el término lo edita una
            // sola caja fija en el encabezado.
            var questionnaireSnomed = createSnomedSearch(function () { return questionnaireCoding; });
            Vue.watch(function () { return questionnaireCoding.term; }, function (term) {
                questionnaireSnomed.schedule(term);
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
                    if (explicit) toast('Este cuestionario está archivado: no se puede editar. Publíquelo de nuevo desde el listado para reactivarlo.', 'error');
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

                // La foto que se manda se congela ACÁ. A partir de este punto marcamos
                // "limpio" de forma optimista: si el usuario edita algo mientras el PUT viaja
                // (ventana real en el Oracle lento de dev), markDirty vuelve a prender dirty y
                // ese cambio NO se pierde. Antes, dirty se apagaba recién en el .then —después
                // de la respuesta—, así que pisaba la edición hecha durante el vuelo y el
                // siguiente autosave la veía "sin cambios" (era el "no hay cambios para
                // guardar" al modificar el código SNOMED). En error se restaura (más abajo).
                var body = JSON.stringify(buildPayload());
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
            // todo, se combinan los dos trabajos: base = lo que cargamos, "lo mío" = lo local,
            // "lo de la otra persona" = la versión actual del server. Se auto-combina todo lo
            // que sólo tocó una de las dos partes; sólo se pregunta donde ambos modificaron la
            // MISMA pregunta (o uno la editó y el otro la borró). Descartar sigue disponible.
            function numOrNull(v) { return typeof v === 'number' && !isNaN(v) ? v : null; }

            // Foto canónica de una pregunta para comparar entre las 3 fuentes (ignora orden y
            // campos de presentación como optionsText).
            function normQ(q) {
                if (!q) return null;
                return JSON.stringify({
                    text: q.text || '',
                    type: q.type || 'string',
                    required: !!q.required,
                    options: (q.options || []).slice(),
                    min: numOrNull(q.min), max: numOrNull(q.max),
                    unit: q.unit || null, accept: q.accept || null,
                    maxLength: numOrNull(q.maxLength), pattern: q.pattern || null,
                    enableWhen: (q.enableWhen && q.enableWhen.question)
                        ? { question: q.enableWhen.question, op: q.enableWhen.op || '=', value: q.enableWhen.value === undefined ? null : q.enableWhen.value }
                        : null,
                    code: q.code || null, codeDisplay: q.codeDisplay || null, term: q.term || null
                });
            }

            function indexQuestions(grps) {
                var map = {};
                (grps || []).forEach(function (g) {
                    (g.questions || []).forEach(function (q) {
                        if (q && q.linkId) map[q.linkId] = { q: q, section: g.title || '' };
                    });
                });
                return map;
            }

            function computeMerge(base, mine, theirs) {
                var b = indexQuestions(base), m = indexQuestions(mine), t = indexQuestions(theirs);
                var keys = {};
                [b, m, t].forEach(function (idx) { Object.keys(idx).forEach(function (k) { keys[k] = true; }); });

                var plan = [];
                Object.keys(keys).forEach(function (k) {
                    var bi = b[k], mi = m[k], ti = t[k];
                    var nb = normQ(bi && bi.q), nm = normQ(mi && mi.q), nt = normQ(ti && ti.q);
                    var mineChanged = nm !== nb, theirsChanged = nt !== nb;
                    var e = {
                        key: k, text: (mi && mi.q.text) || (ti && ti.q.text) || k,
                        mine: mi ? mi.q : null, mineSection: mi ? mi.section : null,
                        needsChoice: false, choice: null, action: 'keep-theirs', auto: false, kind: null
                    };

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

            // Esqueleto = la versión de la otra persona; encima se aplican sólo mis cambios que
            // ganaron. Así el orden y las secciones siguen a la versión del server (predecible),
            // y lo mío nuevo se agrega a su sección por título (o se crea si no existe).
            function buildMerged(theirs, plan) {
                var merged = JSON.parse(JSON.stringify(theirs || []));
                function findQ(linkId) {
                    for (var i = 0; i < merged.length; i++) {
                        var qs = merged[i].questions || [];
                        for (var j = 0; j < qs.length; j++) if (qs[j].linkId === linkId) return { g: merged[i], idx: j };
                    }
                    return null;
                }
                function sectionByTitle(title) {
                    for (var i = 0; i < merged.length; i++) if ((merged[i].title || '') === (title || '')) return merged[i];
                    var g = { title: title || 'Nueva sección', questions: [] };
                    merged.push(g);
                    return g;
                }
                function upsert(q, sectionTitle) {
                    var clone = JSON.parse(JSON.stringify(q));
                    var found = findQ(q.linkId);
                    if (found) found.g.questions.splice(found.idx, 1, clone);
                    else sectionByTitle(sectionTitle).questions.push(clone);
                }
                function removeQ(linkId) { var f = findQ(linkId); if (f) f.g.questions.splice(f.idx, 1); }

                plan.forEach(function (e) {
                    var r = resolveEntry(e);
                    if (r === 'take-mine') upsert(e.mine, e.mineSection);
                    else if (r === 'delete') removeQ(e.key);
                    // 'keep-theirs' / 'noop' -> ya está en el esqueleto
                });
                merged.forEach(function (g) {
                    g.questions.forEach(function (q) { q.optionsText = (q.options || []).join('\n'); });
                });
                return merged;
            }

            // Trae la versión de la otra persona (mismo formato que props.groups) y arma el plan.
            function beginMerge() {
                fetch('/Questionnaires/EditorModel/' + currentId.value, { credentials: 'same-origin' })
                    .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
                    .then(function (theirs) {
                        var res = computeMerge(baseGroups, groups.value, theirs.groups || []);
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
                var merged = buildMerged(pendingTheirs.groups || [], mergePlanAll.value);
                var theirsVersion = pendingTheirs.concurrencyVersion;
                groups.value = merged;
                baseGroups = JSON.parse(JSON.stringify(merged));   // la base ahora es lo combinado
                recordVersion.value = theirsVersion;               // anclado a la versión del server
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
                    toast('Hay ' + issues.value.length + ' condición(es) que apuntan a preguntas inexistentes. Resuélvalas antes de publicar.', 'error');
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
                    stashPublishToast();
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
                if (!dirty.value || isArchived.value) return;
                e.preventDefault();
                e.returnValue = '';
                return '';
            }

            // Engancha los botones del header (viven fuera de la isla). Ojo: el listener
            // recibe el Event, así que va el wrapper y no save directamente.
            Vue.onMounted(function () {
                flushPublishToast();
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
                questionSnomed.cancel();
                questionnaireSnomed.cancel();
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
                merging: merging, mergeAutoCount: mergeAutoCount, conflictEntries: conflictEntries,
                confirmMerge: confirmMerge, cancelMerge: cancelMerge,
                issues: issues, fixIssues: fixIssues,
                conditionSources: conditionSources, conditionSource: conditionSource,
                onConditionQuestion: onConditionQuestion, onConditionValue: onConditionValue,
                questionSnomed: questionSnomed,
                questionnaireCoding: questionnaireCoding, questionnaireSnomed: questionnaireSnomed,
                selectQuestion: selectQuestion, addGroup: addGroup, addQuestion: addQuestion,
                removeQuestion: removeQuestion, removeGroup: removeGroup, save: saveExplicit,
                importOpen: importOpen, importLoading: importLoading, importBusy: importBusy,
                importError: importError, importCandidates: importCandidates,
                openImport: openImport, closeImport: closeImport, importFrom: importFrom,
                estadoLabel: estadoLabel,
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
            <div class="cq-text-sm"><strong>Otra persona guardó cambios mientras usted editaba.</strong></div>\
            <div class="cq-text-sm cq-text-muted">Sus cambios NO se guardaron, y los de la otra persona siguen intactos. No hay forma automática de combinar las dos versiones, así que debe elegir: anote lo suyo y vuelva a cargar, o abra el cuestionario en otra pestaña para comparar.</div>\
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
    <span class="cq-text-sm">Este cuestionario está <strong>publicado</strong>. Los cambios se guardan como pendientes: los clínicos siguen viendo la versión publicada hasta que publique una nueva.</span>\
</div>\
<div v-if="isArchived && !conflict" class="cq-card mb-2 d-flex align-items-center gap-2" style="border-left:3px solid var(--cq-warning);">\
    <i class="bi bi-exclamation-triangle-fill" style="color:var(--cq-warning);"></i>\
    <span class="cq-text-sm">Este cuestionario está <strong>archivado</strong> y es de sólo lectura: no se puede editar ni guardar. Para reactivarlo, publíquelo de nuevo desde el listado de Cuestionarios.</span>\
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
            <p class="cq-text-xs cq-text-muted mt-3 mb-0">Haga clic en + dentro de una sección para agregar.</p>\
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
            <hr class="my-2" />\
            <h4 class="cq-text-sm fw-semibold mb-2">Codificación clínica del cuestionario <span class="cq-text-xs cq-text-muted fw-normal">(opcional)</span></h4>\
            <p class="cq-text-xs cq-text-muted mb-2">Asociar un concepto de SNOMED CT a la ficha</p>\
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
        <div class="mb-2" v-if="!isArchived">\
            <button class="btn btn-sm btn-primary" @click="openImport"><i class="bi bi-box-arrow-in-down me-1"></i> Importar preguntas de otro cuestionario</button>\
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
                    <button class="btn btn-outline-primary" type="button" @click="questionSnomed.search" :disabled="questionSnomed.searching" title="Buscar el término en SNOMED CT">\
                        <span v-if="questionSnomed.searching" class="spinner-border spinner-border-sm"></span>\
                        <template v-else><i class="bi bi-search"></i> Buscar</template>\
                    </button>\
                </div>\
                <div v-if="questionSnomed.error" class="cq-text-xs text-danger mt-1">{{ questionSnomed.error }}</div>\
                <ul v-if="questionSnomed.open && questionSnomed.results.length" class="list-unstyled border rounded mt-1 mb-0" style="max-height:200px; overflow:auto;">\
                    <li v-for="r in questionSnomed.results" :key="r.code" class="px-2 py-1 cq-text-sm" style="cursor:pointer;" @click="questionSnomed.pick(r)"\
                        onmouseover="this.style.background=\'var(--cq-primary-100)\'" onmouseout="this.style.background=\'transparent\'">\
                        {{ r.display }} <span class="cq-text-xs cq-text-muted">{{ r.code }}</span>\
                    </li>\
                </ul>\
                <div v-else-if="questionSnomed.open && !questionSnomed.searching && !questionSnomed.error" class="cq-text-xs cq-text-muted mt-1">Sin resultados.</div>\
            </div>\
            <div class="mb-2">\
                <label class="form-label cq-text-sm">Código SNOMED CT</label>\
                <input v-model="selected.question.code" class="form-control form-control-sm" placeholder="ej: 50373000" />\
                <div v-if="selected.question.codeDisplay" class="cq-text-xs mt-1">\
                    <i class="bi bi-check-circle-fill text-success"></i> {{ selected.question.codeDisplay }}\
                    <a href="javascript:" class="ms-1" @click="questionSnomed.clear">quitar</a>\
                </div>\
            </div>\
\
            <hr />\
            <button class="btn btn-sm btn-outline-danger" @click="removeQuestion(selected.group, selected.question)"><i class="bi bi-trash me-1"></i> Eliminar pregunta</button>\
        </div>\
        <div v-else class="cq-card cq-text-sm cq-text-muted">Seleccione una pregunta para editar sus propiedades.</div>\
    </div>\
</div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-editor', QuestionnaireEditor);
})();
