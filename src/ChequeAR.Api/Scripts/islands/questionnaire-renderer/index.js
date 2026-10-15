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
            // Número de la versión vigente si esta ficha está en una anterior; 0 si ya está
            // en la última. Habilita la opción de pasar a la versión nueva.
            newerVersion: { type: Number, default: 0 },
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
                                maxLength: q.maxLength,
                                pattern: q.pattern,
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

            // --- Completado asincrónico: guardado parcial + lock de sesión -------------
            // La ficha puede quedar InProgress durante semanas (ej. seguimiento telefónico).
            // responseId/concurrencyVersion identifican la ficha que se está completando;
            // lockToken es la credencial de ESTA sesión de edición (la devuelve $lock),
            // exigida en cada guardado para que dos médicos no puedan completarla a la vez.
            var responseId = Vue.ref(null);
            // Espejo de meta.versionId del recurso FHIR: es el reemplazo FHIR-idiomático del
            // viejo ConcurrencyVersion crudo, mismo mecanismo ETag/If-Match ya usado para
            // Questionnaire.
            var concurrencyVersion = Vue.ref(null);
            var lockToken = Vue.ref(null);
            var lockExpiresAt = Vue.ref(null);
            // Respuestas tal como estaban al ABRIR esta sesión (lo resumido, o [] si la ficha
            // es nueva), en forma interna (LinkId/ValueXxx). Se manda al cerrar
            // deliberadamente (guardar avance / finalizar) para que el server audite UN solo
            // evento con el diff de lo completado en la sesión. No es la representación FHIR
            // del recurso -- $unlock sigue esperando el formato interno de guardado parcial.
            var sessionBaseline = Vue.ref([]);
            // Alguien más tiene el lock: {userId, expiresAt}. Mientras esté seteado, el
            // formulario queda de solo lectura.
            var lockedByOther = Vue.ref(null);
            var sessionReady = Vue.ref(false);
            var closedByOther = Vue.ref(false);
            var showRenewPrompt = Vue.ref(false);
            var lockSecondsLeft = Vue.ref(null);
            var autosaveTimer = null;
            var lockCountdownTimer = null;
            var AUTOSAVE_DEBOUNCE_MS = 2500;
            var LOCK_WARNING_MS = 60000;

            function token() { return window.ChequeARIslands.auth.token(); }
            function authHeaders() { return window.ChequeARIslands.auth.headers(true); }

            function readOnly() {
                return !sessionReady.value || !!lockedByOther.value || closedByOther.value;
            }

            function errorFrom(r, fallback) {
                return r.json().catch(function () { return null; }).then(function (body) {
                    var msg = body && (body.error ||
                        (body.issue && body.issue[0] && body.issue[0].diagnostics));
                    var err = new Error(msg || fallback);
                    err.status = r.status;
                    throw err;
                });
            }

            function silentError() {
                var err = new Error('');
                err.silent = true;
                return err;
            }

            function notifyError(prefix, err) {
                if (err && err.silent) return;
                window.cqToast && window.cqToast(prefix + (err && err.message ? ': ' + err.message : ''), 'error');
            }

            var writeQueue = Promise.resolve();
            function enqueueWrite(fn) {
                var p = writeQueue.then(fn);
                writeQueue = p.catch(function () { });
                return p;
            }


            // --- Evaluación de condiciones (enableWhen): ver enable-when.js -------------
            var EnableWhen = window.ChequeAREnableWhen;
            var questionsByLinkId = {};
            questionnaire.value.groups.forEach(function (g) {
                g.questions.forEach(function (q) { if (!questionsByLinkId[q.linkId]) questionsByLinkId[q.linkId] = q; });
            });

            function isVisible(q) {
                return EnableWhen.isVisible(q, questionsByLinkId, answers);
            }

            // Adjuntos que fallaron al subir, por linkId. Van aparte de `errors` porque
            // validate() reescribe ese mapa entero: como la pregunta queda sin respuesta,
            // el submit borraba el error y finalizaba la ficha sin el archivo.
            var uploadErrors = Vue.reactive({});

            // El input de decimales es de texto para que la coma de es-AR llegue hasta acá
            // (un input[type=number] la descarta y entrega '', sin forma de saber qué se tipeó).
            var parseNumeric = EnableWhen.parseNumeric;

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
                if ((q.type === 'string' || q.type === 'text') && typeof value === 'string' && value !== '') {
                    if (q.maxLength && value.length > q.maxLength) {
                        errors[q.linkId] = 'Máximo ' + q.maxLength + ' caracteres'; return;
                    }
                    if (q.pattern) {
                        var re = null;
                        // Un patrón inválido es error del autor del cuestionario, no de quien
                        // responde: no debe bloquear la carga, así que se ignora.
                        try { re = new RegExp(q.pattern); } catch (e) { re = null; }
                        if (re && !re.test(value)) { errors[q.linkId] = 'Formato inválido'; return; }
                    }
                }
                delete errors[q.linkId];
            }

            function onChange(q, value) {
                if (readOnly()) return;
                answers[q.linkId] = value;
                validate(q, value);
                // Las condiciones pueden encadenarse: se revalidan todas las condicionales.
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (qq) {
                        if (qq.enableWhen && qq !== q) validate(qq, answers[qq.linkId]);
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
                    if (!r.ok) throw new Error('error del servidor');
                    return r.json();
                }).then(function (att) {
                    onChange(q, { attachmentId: att.id, fileName: file.name, size: att.size });
                }).catch(function (err) {
                    uploadErrors[q.linkId] = 'No se pudo subir: ' + err.message + '. Seleccione el archivo de nuevo.';
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

            // El link directo a /api/attachments/{id} no sirve: el endpoint pide JWT por
            // Authorization header, y una navegación de <a href> no lo manda. Se trae el
            // archivo por fetch (mismo header que el resto de la app) y se abre como blob.
            var viewingAttachment = Vue.reactive({});
            function viewAttachment(attachmentId, linkId) {
                if (!attachmentId) return;
                viewingAttachment[linkId] = true;
                fetch('/api/attachments/' + attachmentId, { headers: { 'Authorization': 'Bearer ' + token() } })
                    .then(function (r) {
                        if (!r.ok) throw new Error();
                        return r.blob();
                    })
                    .then(function (blob) {
                        var url = URL.createObjectURL(blob);
                        window.open(url, '_blank');
                        setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
                    })
                    .catch(function () {
                        window.cqToast && window.cqToast('No se pudo abrir el adjunto.', 'error');
                    })
                    .then(function () { viewingAttachment[linkId] = false; });
            }

            // Las fichas anteriores guardaron el texto de la opción y no su código: al
            // retomarlas se traduce, y el próximo guardado ya manda el código.
            function optionCode(q, stored) {
                if (stored === undefined || stored === null) return stored;
                var opts = q.options || [];
                for (var i = 0; i < opts.length; i++) if (opts[i].code === stored) return stored;
                var match = opts.filter(function (o) { return (o.aliases || []).indexOf(stored) >= 0; });
                return match.length === 1 ? match[0].code : stored;
            }

            function questionByLinkId(linkId) {
                var found = null;
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) { if (!found && q.linkId === linkId) found = q; });
                });
                return found;
            }

            // Opción múltiple: la respuesta es un array de opciones marcadas.
            function onToggleMulti(q, option, checked) {
                var current = (answers[q.linkId] || []).slice();
                var i = current.indexOf(option);
                if (checked && i < 0) current.push(option);
                if (!checked && i >= 0) current.splice(i, 1);
                onChange(q, current);
            }

            // Autosave real, debounced: cada cambio reprograma el guardado un poco más
            // adelante en vez de mandar un PUT por tecla. Con esto la ventana de "algo
            // tipeado que todavía no llegó al server" es de segundos, no de toda la llamada.
            function triggerAutosave() {
                savingState.value = 'dirty';
                if (readOnly()) return;
                if (autosaveTimer) clearTimeout(autosaveTimer);
                autosaveTimer = setTimeout(doSave, AUTOSAVE_DEBOUNCE_MS);
            }

            function collectItems() {
                var items = [];
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        // Oculta: se manda VACÍA, no se saltea. El servidor mezcla por linkId y
                        // conserva lo que no le llega, así que una respuesta guardada antes de que
                        // la pregunta se ocultara terminaba en la ficha finalizada. El valor sigue
                        // en `answers`: si la condición vuelve a cumplirse, reaparece.
                        if (!isVisible(q)) { items.push({ LinkId: q.linkId, QuestionType: q.type }); return; }
                        toItems(q).forEach(function (it) { items.push(it); });
                    });
                });
                return items;
            }

            // Reconstruye `answers` a partir del item[] del recurso FHIR, al resumir una
            // ficha en progreso. Un item con varias answer (opción múltiple) ya viene
            // agrupado por linkId -- a diferencia del formato interno, FHIR no lo separa en
            // varios registros.
            function applyExistingItems(fhirItems) {
                var byLinkId = {};
                (fhirItems || []).forEach(function (it) {
                    if (it.linkId && it.answer && it.answer.length) byLinkId[it.linkId] = it;
                });
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        var it = byLinkId[q.linkId];
                        if (!it) return;
                        var value = valueFromFhirItem(q, it);
                        if (value !== undefined) answers[q.linkId] = value;
                    });
                });
            }

            function valueFromFhirItem(q, it) {
                if (q.type === 'openChoice') {
                    return it.answer
                        .map(function (a) { return a.valueCoding && optionCode(q, a.valueCoding.code); })
                        .filter(function (c) { return !!c; });
                }
                var a = it.answer[0];
                if (a.valueBoolean !== undefined) return a.valueBoolean;
                if (a.valueInteger !== undefined) return a.valueInteger;
                if (a.valueDecimal !== undefined) return a.valueDecimal;
                if (a.valueDateTime !== undefined) {
                    // El server serializa fecha y fecha-hora igual (FhirDateTime); acá se
                    // recorta según lo que espera cada control HTML nativo.
                    if (q.type === 'date') return a.valueDateTime.slice(0, 10);
                    if (q.type === 'dateTime') return a.valueDateTime.slice(0, 16);
                    return a.valueDateTime;
                }
                if (a.valueCoding) return optionCode(q, a.valueCoding.code);
                if (a.valueAttachment) {
                    return { attachmentId: attachmentIdFromUrl(a.valueAttachment.url), fileName: a.valueAttachment.title };
                }
                if (a.valueString !== undefined) return a.valueString;
                return undefined;
            }

            function attachmentIdFromUrl(url) {
                if (!url) return null;
                var sinBarra = url.replace(/\/+$/, '');
                return sinBarra.substring(sinBarra.lastIndexOf('/') + 1) || null;
            }

            // Convierte item[] FHIR al formato interno (LinkId/ValueXxx) que espera $unlock
            // para el baseline de la sesión. No es la representación canónica del recurso,
            // es sólo el formato del diff de auditoría de guardado parcial.
            function fhirItemsToDomain(fhirItems) {
                var result = [];
                (fhirItems || []).forEach(function (it) {
                    if (!it.answer || !it.answer.length) { result.push({ LinkId: it.linkId }); return; }
                    var q = questionByLinkId(it.linkId);
                    it.answer.forEach(function (a) {
                        var d = { LinkId: it.linkId };
                        if (a.valueBoolean !== undefined) d.ValueBoolean = a.valueBoolean;
                        else if (a.valueInteger !== undefined) d.ValueInteger = a.valueInteger;
                        else if (a.valueDecimal !== undefined) d.ValueDecimal = a.valueDecimal;
                        else if (a.valueDateTime !== undefined) d.ValueDate = a.valueDateTime;
                        else if (a.valueCoding) d.ValueCode = q ? optionCode(q, a.valueCoding.code) : a.valueCoding.code;
                        else if (a.valueAttachment) {
                            d.AttachmentId = attachmentIdFromUrl(a.valueAttachment.url);
                            d.ValueString = a.valueAttachment.title;
                        } else if (a.valueString !== undefined) d.ValueString = a.valueString;
                        result.push(d);
                    });
                });
                return result;
            }

            function formatTime(iso) {
                try { return new Date(iso).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' }); }
                catch (e) { return ''; }
            }

            // Crea la ficha (InProgress) la primera vez que hay algo para guardar. Llamadas
            // siguientes son no-op: ya existe. El body es un recurso FHIR QuestionnaireResponse
            // real -- Create habla FHIR igual que Search/Get/$finalize.
            function ensureResponse() {
                if (responseId.value) return Promise.resolve();
                return fetch('/fhir/r5/QuestionnaireResponse', {
                    method: 'POST',
                    headers: authHeaders(),
                    body: JSON.stringify({
                        resourceType: 'QuestionnaireResponse',
                        status: 'in-progress',
                        questionnaire: 'https://chequear.garrahan/Questionnaire/' + props.questionnaireId,
                        subject: {
                            identifier: {
                                system: 'https://chequear.garrahan/patient-hospital-id',
                                value: props.patientId
                            }
                        },
                        // Ancla la ficha a la version que se rendrizo, no a la que este
                        // publicada al momento de guardar. No tiene lugar nativo en FHIR:
                        // el dominio ancla por identificador de version, no por numero.
                        extension: [{
                            url: 'https://chequear.garrahan/StructureDefinition/questionnaire-version-id',
                            valueString: props.questionnaireVersionId
                        }],
                        item: []
                    })
                }).then(function (r) {
                    if (!r.ok) return errorFrom(r, 'no se pudo crear la ficha');
                    return r.json();
                }).then(function (created) {
                    responseId.value = created.id;
                    concurrencyVersion.value = versionOf(created);
                });
            }

            function versionOf(resource) {
                var v = resource && resource.meta && resource.meta.versionId;
                return v === undefined || v === null ? null : Number(v);
            }

            function markClosedByOther() {
                closedByOther.value = true;
                lockToken.value = null;
                if (autosaveTimer) { clearTimeout(autosaveTimer); autosaveTimer = null; }
                if (lockCountdownTimer) clearInterval(lockCountdownTimer);
                showRenewPrompt.value = false;
            }

            function reloadFromServer() {
                return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value, { headers: authHeaders() })
                    .then(function (r) {
                        if (!r.ok) return errorFrom(r, 'no se pudo leer la ficha');
                        return r.json();
                    }).then(function (resource) {
                        if (resource.status && resource.status !== 'in-progress') {
                            markClosedByOther();
                            return;
                        }
                        var items = resource.item || [];
                        Object.keys(answers).forEach(function (k) { delete answers[k]; });
                        Object.keys(errors).forEach(function (k) { delete errors[k]; });
                        applyExistingItems(items);
                        sessionBaseline.value = fhirItemsToDomain(items);
                        concurrencyVersion.value = versionOf(resource);
                        savingState.value = 'idle';
                    });
            }

            function reloadAfterForeignChange() {
                return reloadFromServer().then(function () {
                    if (!closedByOther.value) {
                        window.cqToast && window.cqToast(
                            'Otro usuario modificó esta ficha. Se cargaron los datos actuales: revisalos antes de seguir.', 'warning');
                    }
                    throw silentError();
                });
            }

            function ensureLock() {
                if (lockToken.value && lockExpiresAt.value && lockExpiresAt.value.getTime() > Date.now()) {
                    return Promise.resolve();
                }
                var known = concurrencyVersion.value;
                return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$lock', {
                    method: 'POST',
                    headers: authHeaders()
                }).then(function (r) {
                    if (r.status === 409) {
                        return r.json().then(function (body) {
                            lockedByOther.value = { userId: body.lockedBy, expiresAt: body.expiresAt };
                            throw new Error(body.error || 'Lo está completando otro usuario.');
                        });
                    }
                    if (r.status === 422) {
                        markClosedByOther();
                        throw silentError();
                    }
                    if (!r.ok) return errorFrom(r, 'no se pudo abrir la sesión de edición');
                    return r.json();
                }).then(function (body) {
                    lockToken.value = body.lockToken;
                    lockExpiresAt.value = new Date(body.expiresAt);
                    lockedByOther.value = null;
                    scheduleLockCountdown();
                    var fresh = Number(body.concurrencyVersion);
                    if (known !== null && fresh !== known + 1) return reloadAfterForeignChange();
                    concurrencyVersion.value = fresh;
                });
            }

            // Guardado parcial: manda TODO lo respondido hasta ahora (no sólo lo nuevo de
            // esta sesión). Es idempotente -- volver a mandar una respuesta ya guardada no
            // hace nada -- y así no hace falta llevar un registro aparte de "qué cambió
            // desde el último autosave". La respuesta SÍ es el recurso FHIR completo (con
            // meta.versionId); el vencimiento del lock, sin lugar nativo en FHIR, viaja en
            // un header propio.
            function putProgress(isRetry) {
                return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value, {
                    method: 'PUT',
                    headers: Object.assign({}, authHeaders(), {
                        'If-Match': 'W/"' + concurrencyVersion.value + '"',
                        'X-Lock-Token': lockToken.value
                    }),
                    body: JSON.stringify({ Items: collectItems() })
                }).then(function (r) {
                    if (r.status === 409) {
                        return r.json().then(function (body) {
                            if (body.lockedBy) {
                                lockedByOther.value = { userId: body.lockedBy, expiresAt: body.expiresAt };
                                lockToken.value = null;
                                throw new Error(body.error || 'Lo está completando otro usuario.');
                            }
                            return reloadAfterForeignChange();
                        });
                    }
                    if (r.status === 422 && !isRetry) {
                        lockToken.value = null;
                        return ensureLock().then(function () { return putProgress(true); });
                    }
                    if (!r.ok) return errorFrom(r, 'error del servidor');
                    var lockExpiresHeader = r.headers.get('X-Lock-Expires-At');
                    return r.json().then(function (updated) {
                        concurrencyVersion.value = versionOf(updated);
                        if (lockExpiresHeader) {
                            lockExpiresAt.value = new Date(lockExpiresHeader);
                            showRenewPrompt.value = false;
                        }
                        savingState.value = 'idle';
                    });
                });
            }

            function saveAll() {
                return ensureResponse()
                    .then(function () { return ensureLock(); })
                    .then(function () { return putProgress(); });
            }

            function doSave() {
                autosaveTimer = null;
                if (readOnly() || !token()) return;
                enqueueWrite(function () {
                    if (readOnly()) return;
                    return saveAll();
                }).catch(function (err) {
                    if (err && err.silent) return;
                    savingState.value = 'error';
                    if (lockedByOther.value) return;
                    notifyError('No se pudo guardar', err);
                });
            }

            // Timer de 1s para el aviso de "1 minuto para que se cierre". No vence el lock
            // del lado del cliente: eso lo decide el server en el próximo guardado (con el
            // grace period si nadie más lo tomó mientras tanto).
            function scheduleLockCountdown() {
                if (lockCountdownTimer) clearInterval(lockCountdownTimer);
                lockCountdownTimer = setInterval(function () {
                    if (!lockExpiresAt.value) return;
                    var msLeft = lockExpiresAt.value.getTime() - Date.now();
                    lockSecondsLeft.value = Math.max(0, Math.round(msLeft / 1000));
                    showRenewPrompt.value = msLeft <= LOCK_WARNING_MS;
                }, 1000);
            }

            function renewLock() {
                if (!responseId.value || !lockToken.value) return;
                enqueueWrite(function () {
                    if (!lockToken.value) return;
                    return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$renew-lock', {
                        method: 'POST',
                        headers: Object.assign({}, authHeaders(), { 'X-Lock-Token': lockToken.value })
                    }).then(function (r) { return r.ok ? r.json() : null; })
                      .then(function (body) {
                          if (body && body.expiresAt) {
                              lockExpiresAt.value = new Date(body.expiresAt);
                              showRenewPrompt.value = false;
                              if (body.concurrencyVersion !== undefined) concurrencyVersion.value = Number(body.concurrencyVersion);
                          }
                      });
                }).catch(function () { });
            }

            function retryLock() {
                if (!responseId.value) { initSession(); return; }
                enqueueWrite(function () {
                    lockToken.value = null;
                    return ensureLock();
                }).catch(function (err) {
                    if (err && err.silent) return;
                    if (!lockedByOther.value) notifyError('No se pudo retomar la ficha', err);
                });
            }

            // Salir del editor por una vía NO deliberada (cerrar pestaña, botón atrás,
            // navegar afuera). Manda el baseline igual que el "Guardar avance" para que el
            // server deje UN evento de auditoría con lo agregado en la sesión. Es best-effort:
            // keepalive (no sendBeacon, que no permite headers de auth ni X-Lock-Token); si el
            // navegador matan el proceso de golpe puede no llegar, y ahí el TTL libera el lock
            // solo unos minutos después. El chequeo de lockToken evita el doble disparo
            // (beforeunload + onUnmounted): el segundo ve el token ya en null y no hace nada.
            function releaseLock() {
                if (!responseId.value || !lockToken.value) return;
                fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$unlock', {
                    method: 'POST',
                    headers: Object.assign({}, authHeaders(), { 'X-Lock-Token': lockToken.value }),
                    body: JSON.stringify({ Items: sessionBaseline.value }),
                    keepalive: true
                }).catch(function () { /* nada que hacer del lado del cliente */ });
                lockToken.value = null;
                if (lockCountdownTimer) clearInterval(lockCountdownTimer);
            }

            // Busca una ficha InProgress ya empezada para este paciente+cuestionario (ej.
            // se completó parte en el consultorio y ahora sigue por teléfono) y la resume;
            // si no hay ninguna, se crea recién en el primer autosave. El Bundle y sus
            // entries son recursos FHIR reales (entry[].resource), igual que cualquier
            // cliente estándar esperaría.
            function initSession() {
                // No bloquea el render con `loading`: el formulario ya es usable de
                // entrada (ficha nueva) y, si había algo para resumir, se completa solo
                // un instante después sin parpadeo de spinner.
                var query = '/fhir/r5/QuestionnaireResponse?subject=' + encodeURIComponent(props.patientId) +
                    '&questionnaire=' + encodeURIComponent(props.questionnaireId) + '&status=InProgress&_count=1';
                fetch(query, { headers: authHeaders() }).then(function (r) {
                    if (!r.ok) return errorFrom(r, 'no se pudo buscar la ficha en curso');
                    return r.json();
                }).then(function (bundle) {
                    var entry = (bundle.entry || [])[0];
                    var existing = entry && entry.resource;
                    if (!existing) return;   // ficha nueva: el baseline queda [] (nada previo)
                    responseId.value = existing.id;
                    concurrencyVersion.value = versionOf(existing);
                    var existingItems = existing.item || [];
                    // Foto del punto de partida para el diff de auditoría de esta sesión.
                    sessionBaseline.value = fhirItemsToDomain(existingItems);
                    applyExistingItems(existingItems);
                    return enqueueWrite(function () { return ensureLock(); });
                }).catch(function (err) {
                    if (err && err.silent) return;
                    if (!lockedByOther.value) notifyError('No se pudo retomar la ficha', err);
                }).then(function () {
                    sessionReady.value = true;
                });
            }

            Vue.onMounted(function () {
                if (blockedByBack.value) return;
                if (!token()) { sessionReady.value = true; return; }
                initSession();
            });
            Vue.onUnmounted(function () {
                window.removeEventListener('beforeunload', releaseLock);
                if (autosaveTimer) clearTimeout(autosaveTimer);
                releaseLock();
            });
            window.addEventListener('beforeunload', releaseLock);

            function totalQuestions() {
                var n = 0;
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) { if (isVisible(q)) n++; });
                });
                return n;
            }
            // Misma noción de "vacío" que validate(): una opción múltiple sin nada tildado
            // llega como [], que no es '' ni null pero tampoco es una respuesta.
            var isEmptyAnswer = EnableWhen.isEmpty;
            function answeredCount() {
                var n = 0;
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (isVisible(q) && !isEmptyAnswer(answers[q.linkId])) n++;
                    });
                });
                return n;
            }
            // Todas las preguntas obligatorias visibles están respondidas: habilita "Finalizar
            // y enviar" (las opcionales pueden quedar vacías, igual que en submit()/validate()).
            // "Guardar y continuar después" sigue disponible igual, por si el médico prefiere
            // dejarla pendiente (ej. quiere revisarla o completar algo más luego).
            function isComplete() {
                var ok = true;
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (isVisible(q) && q.required && isEmptyAnswer(answers[q.linkId])) ok = false;
                    });
                });
                return ok;
            }

            // Un valor del formulario -> la columna tipada que espera
            // QuestionnaireResponseItem en el backend (formato interno de guardado parcial,
            // no el recurso FHIR -- ver comentario en putProgress/collectItems).
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
                if (submitting.value || readOnly()) return;

                var allErrors = [];
                questionnaire.value.groups.forEach(function (g) {
                    g.questions.forEach(function (q) {
                        if (!isVisible(q)) return;
                        validate(q, answers[q.linkId]);
                        if (errors[q.linkId]) allErrors.push(q.text + ': ' + errors[q.linkId]);
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
                if (autosaveTimer) { clearTimeout(autosaveTimer); autosaveTimer = null; }

                // Crea/toma el lock si hace falta y guarda todo antes de finalizar: mismo
                // camino que el autosave, así $finalize siempre corre sobre lo último.
                enqueueWrite(function () {
                    return saveAll().then(function () {
                        return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$finalize', {
                            method: 'POST',
                            headers: Object.assign({}, authHeaders(), { 'X-Lock-Token': lockToken.value }),
                            // Además de la foto completa, el server audita el diff de lo que
                            // cambió en esta última sesión: se manda el baseline (respuestas al
                            // abrirla) igual que en "Guardar avance", para que el Detalle muestre A→B.
                            body: JSON.stringify({ Items: sessionBaseline.value })
                        });
                    }).then(function (r) {
                        if (!r.ok) return errorFrom(r, 'no se pudo finalizar');
                        return responseId.value;
                    });
                }).then(function (id) {
                    savingState.value = 'idle';
                    lockToken.value = null;   // $finalize ya liberó el lock del lado del server
                    if (lockCountdownTimer) clearInterval(lockCountdownTimer);
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
                    if (err && err.silent) return;
                    savingState.value = 'error';
                    notifyError('No se pudo guardar la ficha', err);
                }).then(function () {
                    submitting.value = false;
                });
            }

            function goToHistory() {
                window.location.href = '/Encounters/Timeline?patientId=' + encodeURIComponent(props.patientId);
            }

            // --- Pasar la ficha a la versión más reciente del cuestionario ----------------
            // Sólo aplica a una ficha que se está completando en una versión anterior a la
            // vigente (props.newerVersion > 0). Se muestra un preview del merge y, si el
            // médico confirma, se re-ancla a la versión nueva (descartando lo que esa versión
            // eliminó) y se recarga la página para renderizar el formulario nuevo.
            var retargetPreview = Vue.ref(null);
            var showRetargetModal = Vue.ref(false);
            var retargeting = Vue.ref(false);

            function openRetargetPreview() {
                if (!responseId.value) return;
                fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$retarget-preview', {
                    headers: authHeaders()
                }).then(function (r) {
                    if (!r.ok) return errorFrom(r, 'error del servidor');
                    return r.json();
                }).then(function (preview) {
                    retargetPreview.value = preview;
                    showRetargetModal.value = true;
                }).catch(function (err) {
                    notifyError('No se pudo calcular la vista previa', err);
                });
            }

            function closeRetargetModal() { showRetargetModal.value = false; }

            function confirmRetarget() {
                if (retargeting.value || !responseId.value || !lockToken.value) return;
                retargeting.value = true;
                enqueueWrite(function () {
                    return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$retarget-version', {
                        method: 'POST',
                        headers: Object.assign({}, authHeaders(), { 'X-Lock-Token': lockToken.value })
                    }).then(function (r) {
                        if (!r.ok) return errorFrom(r, 'error del servidor');
                        return r.json();
                    });
                }).then(function () {
                    // Se anula el lock del lado del cliente para que el beforeunload de la
                    // recarga no dispare un "Avance guardado" espurio (el diff sería contra un
                    // baseline que ya no corresponde). Al recargar, la sesión se retoma sola en
                    // la versión nueva.
                    lockToken.value = null;
                    if (lockCountdownTimer) clearInterval(lockCountdownTimer);
                    window.cqToast && window.cqToast('Ficha actualizada a la versión v' + props.newerVersion, 'success');
                    window.location.reload();
                }).catch(function (err) {
                    retargeting.value = false;
                    notifyError('No se pudo actualizar la versión', err);
                });
            }

            // "Guardar y continuar después": guarda el avance, cierra la sesión de edición
            // (la ficha queda Pendiente) y vuelve al historial del paciente. A diferencia del
            // autosave silencioso, este cierre deliberado sí deja UN evento en auditoría con
            // el diff de lo completado en la sesión (mandando el baseline al $unlock).
            function finishPartial() {
                if (submitting.value || readOnly()) return;
                if (!token()) {
                    window.cqToast && window.cqToast('No hay sesión: no se puede guardar.', 'error');
                    return;
                }
                // Nada cargado y ni siquiera se creó la ficha: no tiene sentido crear una vacía.
                if (!responseId.value && answeredCount() === 0) { goToHistory(); return; }

                submitting.value = true;
                savingState.value = 'saving';
                if (autosaveTimer) { clearTimeout(autosaveTimer); autosaveTimer = null; }

                enqueueWrite(function () {
                    return saveAll().then(function () {
                        return fetch('/fhir/r5/QuestionnaireResponse/' + responseId.value + '/$unlock', {
                            method: 'POST',
                            headers: Object.assign({}, authHeaders(), { 'X-Lock-Token': lockToken.value }),
                            body: JSON.stringify({ Items: sessionBaseline.value })
                        });
                    });
                }).then(function () {
                    savingState.value = 'idle';
                    lockToken.value = null;   // ya liberado; el beforeunload no reintenta
                    if (lockCountdownTimer) clearInterval(lockCountdownTimer);
                    window.cqToast && window.cqToast('Avance guardado. Podés retomar esta ficha desde el historial.', 'success');
                    goToHistory();
                }).catch(function (err) {
                    if (err && err.silent) return;
                    savingState.value = 'error';
                    notifyError('No se pudo guardar el avance', err);
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
                viewAttachment: viewAttachment, viewingAttachment: viewingAttachment,
                totalQuestions: totalQuestions, answeredCount: answeredCount, isComplete: isComplete,
                submit: submit, finishPartial: finishPartial,
                lockedByOther: lockedByOther, showRenewPrompt: showRenewPrompt,
                sessionReady: sessionReady, closedByOther: closedByOther, readOnly: readOnly,
                goToHistory: goToHistory,
                lockSecondsLeft: lockSecondsLeft, renewLock: renewLock,
                retryLock: retryLock, formatTime: formatTime,
                newerVersion: props.newerVersion, responseId: responseId,
                retargetPreview: retargetPreview, showRetargetModal: showRetargetModal,
                retargeting: retargeting, openRetargetPreview: openRetargetPreview,
                closeRetargetModal: closeRetargetModal, confirmRetarget: confirmRetarget
            };
        },
        template: '\
<div v-if="blockedByBack" class="cq-card">\
    <div class="cq-empty">\
        <div class="cq-empty__icon"><i class="bi bi-check2-circle"></i></div>\
        <div class="cq-empty__title">Ya se cargó esta ficha</div>\
        <div class="cq-empty__desc">Si se necesita otra, entrar de nuevo desde Pacientes.</div>\
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
    <div v-if="closedByOther" class="col-12">\
        <div class="alert alert-secondary d-flex align-items-center gap-2 mb-0">\
            <i class="bi bi-info-circle"></i>\
            <div class="flex-grow-1">Esta ficha ya no está en curso: otro usuario la finalizó o la descartó.</div>\
            <button class="btn btn-sm btn-outline-secondary" @click="goToHistory">Ir al historial</button>\
        </div>\
    </div>\
    <div v-else-if="lockedByOther" class="col-12">\
        <div class="alert alert-warning d-flex align-items-center gap-2 mb-0">\
            <i class="bi bi-lock-fill"></i>\
            <div class="flex-grow-1">Lo está completando <strong>{{ lockedByOther.userId }}</strong>\
                <span v-if="lockedByOther.expiresAt"> hasta las {{ formatTime(lockedByOther.expiresAt) }}</span>.\
            </div>\
            <button class="btn btn-sm btn-outline-secondary" @click="retryLock">Reintentar</button>\
        </div>\
    </div>\
    <div v-else-if="showRenewPrompt" class="col-12">\
        <div class="alert alert-info d-flex align-items-center gap-2 mb-0">\
            <i class="bi bi-clock-history"></i>\
            <div class="flex-grow-1">Su sesión de edición se cierra en {{ lockSecondsLeft }}s por inactividad.</div>\
            <button class="btn btn-sm btn-primary" @click="renewLock">Seguir completando</button>\
        </div>\
    </div>\
    <div v-if="newerVersion && responseId && !lockedByOther && !closedByOther" class="col-12">\
        <div class="alert alert-info d-flex align-items-center gap-2 mb-0">\
            <i class="bi bi-arrow-up-circle"></i>\
            <div class="flex-grow-1">Hay una versión más nueva (<strong>v{{ newerVersion }}</strong>) de este cuestionario. Esta ficha se sigue completando en la v{{ questionnaire.version }}, con la que se inició.</div>\
            <button class="btn btn-sm btn-outline-primary" @click="openRetargetPreview">Ver cambios y actualizar</button>\
        </div>\
    </div>\
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
        <div class="cq-card" :style="(lockedByOther || closedByOther) ? { opacity: 0.6 } : {}">\
            <fieldset :disabled="readOnly()" class="border-0 p-0 m-0">\
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
                            <option value="">Seleccione…</option>\
                            <option v-for="opt in q.options" :key="opt.code" :value="opt.code">{{ opt.display }}</option>\
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
                            <div v-for="opt in q.options" :key="opt.code" class="form-check">\
                                <input class="form-check-input" type="checkbox" :id="q.linkId + \'_\' + opt.code"\
                                       :checked="(answers[q.linkId] || []).indexOf(opt.code) >= 0" @change="onToggleMulti(q, opt.code, $event.target.checked)" />\
                                <label class="form-check-label cq-text-sm" :for="q.linkId + \'_\' + opt.code">{{ opt.display }}</label>\
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
                                <button class="btn btn-sm btn-link" :disabled="viewingAttachment[q.linkId]" @click="viewAttachment(answers[q.linkId].attachmentId, q.linkId)">{{ viewingAttachment[q.linkId] ? \'Abriendo…\' : \'Ver\' }}</button>\
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
            </fieldset>\
\
            <div class="border-top pt-3 mt-3">\
                <div class="d-flex flex-wrap justify-content-end align-items-center gap-2">\
                    <button class="btn btn-outline-secondary" :disabled="submitting || readOnly()" @click="finishPartial"><i class="bi bi-clock-history me-1"></i> Guardar y continuar después</button>\
                    <button class="btn btn-primary" :disabled="submitting || readOnly() || !isComplete()" :title="!isComplete() ? \'Aún quedan preguntas obligatorias que deben ser contestadas antes de finalizar\' : null" @click="submit"><i class="bi bi-check2-circle me-1"></i> {{ submitting ? \'Guardando…\' : \'Finalizar y enviar\' }}</button>\
                </div>\
            </div>\
        </div>\
    </div>\
\
    <div v-if="showRetargetModal && retargetPreview" class="col-12" style="position:fixed; inset:0; background:rgba(0,0,0,.45); z-index:1050; display:flex; align-items:center; justify-content:center; padding:16px;" @click.self="closeRetargetModal">\
        <div class="cq-card" style="max-width:560px; width:100%; max-height:85vh; overflow:auto;">\
            <h2 class="cq-section-title" style="margin-top:0;">Pasar a la versión v{{ retargetPreview.ToVersion }}</h2>\
            <p class="cq-text-sm cq-text-muted">Está completando la v{{ retargetPreview.FromVersion }}. Así queda su ficha si pasa a la v{{ retargetPreview.ToVersion }}:</p>\
            <div class="mb-3">\
                <div class="cq-text-sm fw-semibold"><i class="bi bi-check2 text-success me-1"></i>Se conservan ({{ retargetPreview.Carried.length }})</div>\
                <ul class="cq-text-sm mb-0" v-if="retargetPreview.Carried.length">\
                    <li v-for="c in retargetPreview.Carried" :key="c.LinkId">{{ c.Text }}<span v-if="c.Changed" class="cq-text-xs cq-text-muted"> · cambió el enunciado</span></li>\
                </ul>\
                <div v-else class="cq-text-sm cq-text-muted">Ninguna respuesta previa se reutiliza.</div>\
            </div>\
            <div class="mb-3" v-if="retargetPreview.Added.length">\
                <div class="cq-text-sm fw-semibold"><i class="bi bi-plus-circle text-primary me-1"></i>Preguntas nuevas ({{ retargetPreview.Added.length }})</div>\
                <ul class="cq-text-sm mb-0">\
                    <li v-for="a in retargetPreview.Added" :key="a.LinkId">{{ a.Text }}</li>\
                </ul>\
            </div>\
            <div class="mb-3" v-if="retargetPreview.Dropped.length">\
                <div class="cq-text-sm fw-semibold text-danger"><i class="bi bi-exclamation-triangle me-1"></i>Se descartan ({{ retargetPreview.Dropped.length }})</div>\
                <ul class="cq-text-sm mb-0">\
                    <li v-for="d in retargetPreview.Dropped" :key="d.LinkId">{{ d.Text }}: <strong>{{ d.Answer }}</strong></li>\
                </ul>\
                <p class="cq-text-xs text-danger mb-0 mt-1">Estas respuestas se perderán al actualizar.</p>\
            </div>\
            <div class="d-flex justify-content-end gap-2 border-top pt-3 mt-3">\
                <button class="btn btn-outline-secondary" :disabled="retargeting" @click="closeRetargetModal">Seguir en v{{ retargetPreview.FromVersion }}</button>\
                <button class="btn btn-primary" :disabled="retargeting" @click="confirmRetarget"><i class="bi bi-arrow-up-circle me-1"></i>{{ retargeting ? \'Actualizando…\' : \'Actualizar a v\' + retargetPreview.ToVersion }}</button>\
            </div>\
        </div>\
    </div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('questionnaire-renderer', QuestionnaireRenderer);
})();
