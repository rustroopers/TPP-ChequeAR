/* Autocompletado y verificación de conceptos SNOMED CT, compartido entre islas.
 *
 * Vivía dentro de questionnaire-editor, que era su único consumidor. Con el catálogo de
 * variables hay un segundo: el ABM del Administrador es, ahora, el lugar donde se elige el
 * concepto de cada variable. Reimplementarlo allá significaría dos versiones del debounce,
 * del backoff por 429 y de la cancelación de la consulta en curso, divergiendo con el tiempo
 * contra el mismo endpoint (/fhir/r5/ValueSet/$expand).
 *
 * Depende de Vue global (window.Vue) y del <meta name="cq-token">, igual que las islas.
 */
(function (window) {
    'use strict';

    // bootstrap.js se carga antes que este archivo en todas las vistas que lo usan.
    function getToken() { return window.ChequeARIslands.auth.token(); }

    // El tiempo de espera de un 429 viaja en el diagnostics del OperationOutcome: el estándar
    // no tiene un campo para eso.
    function diagnostico(body, porOmision) {
        var issues = body && body.issue;
        if (!issues || !issues.length) return porOmision;
        return issues[0].diagnostics || porOmision;
    }

    function expand(term, count, signal) {
        var token = getToken();
        if (!token) return Promise.reject(new Error('No hay sesión: no se puede consultar SNOMED (token ausente).'));
        return fetch('/fhir/r5/ValueSet/$expand?count=' + count + '&filter=' + encodeURIComponent(term), {
            headers: { 'Authorization': 'Bearer ' + token },
            signal: signal
        }).then(function (res) {
            return res.json().catch(function () { return null; }).then(function (body) {
                if (res.status === 429) {
                    var err = new Error(diagnostico(body, 'Demasiadas consultas a SNOMED. Reintente en unos segundos.'));
                    err.rateLimited = true;
                    throw err;
                }
                if (!res.ok) throw new Error(diagnostico(body, 'HTTP ' + res.status));
                // ValueSet: los conceptos vienen en expansion.contains.
                var contains = (body && body.expansion && body.expansion.contains) || [];
                return contains.map(function (c) { return { code: c.code, display: c.display }; });
            });
        });
    }

    /* Fábrica de buscadores. `getActive()` devuelve el objeto sobre el que se escribe
     * term/code/codeDisplay: una pregunta, la codificación del cuestionario entero, o una
     * variable del catálogo — los tres exponen esas mismas tres propiedades.
     *
     * `onChange` se llama cada vez que el buscador modifica el objeto, para que cada isla
     * marque su propio estado sucio (el editor tiene autosave; el ABM, no).
     */
    function createSearch(getActive, onChange) {
        var Vue = window.Vue;
        var results = Vue.ref([]);
        var searching = Vue.ref(false);
        var error = Vue.ref(null);
        var open = Vue.ref(false);
        var abort = null;
        var debounce;
        // Hasta este instante (ms epoch) no autocompletamos solos, para no seguir golpeando
        // un server que ya nos está limitando.
        var cooldownUntil = 0;

        function touched() { if (typeof onChange === 'function') onChange(); }

        function reset() {
            results.value = []; open.value = false; error.value = null;
            clearTimeout(debounce);
        }

        // Mientras se edita el término clínico buscamos solos, con debounce para no golpear el
        // terminológico en cada tecla. El enunciado NO dispara búsquedas: sólo el término.
        function schedule(term) {
            clearTimeout(debounce);
            if (!term || term.trim().length < 3) { reset(); return; }
            if (Date.now() < cooldownUntil) return;
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

            // Si el usuario busca de nuevo con una consulta en curso, cancelamos la vieja.
            if (abort) abort.abort();
            abort = ('AbortController' in window) ? new AbortController() : null;
            searching.value = true;

            expand(term, 15, abort ? abort.signal : undefined)
                .then(function (data) {
                    results.value = data;
                    open.value = true;
                })
                .catch(function (err) {
                    if (err.name === 'AbortError') return;
                    if (err.rateLimited) {
                        var m = /(\d+)\s*segundos/.exec(err.message);
                        cooldownUntil = Date.now() + ((m ? parseInt(m[1], 10) : 8) * 1000);
                    }
                    error.value = err.message;
                    results.value = []; open.value = true;
                })
                .then(function () { searching.value = false; });
        }

        function pick(r) {
            var target = getActive();
            if (!target) return;
            target.code = r.code;
            target.codeDisplay = r.display;
            open.value = false;
            results.value = [];
            touched();
        }

        // Saca el código pero conserva el término: qué mide no deja de ser cierto porque se
        // descarte el concepto elegido, y así se puede volver a buscar sin reescribirlo.
        function clear() {
            var target = getActive();
            if (!target) return;
            target.code = undefined;
            target.codeDisplay = undefined;
            touched();
        }

        function cancel() { clearTimeout(debounce); if (abort) abort.abort(); }

        // reactive y no un objeto plano: al exponerse anidado en el return de setup, Vue NO
        // desenvuelve los refs sueltos —`.searching` llegaría al template como el objeto Ref,
        // siempre truthy, y el spinner quedaría fijo—.
        return Vue.reactive({
            results: results, searching: searching, error: error, open: open,
            schedule: schedule, search: search, pick: pick, clear: clear, reset: reset, cancel: cancel
        });
    }

    /* Verifica un código tipeado a mano contra el terminológico, exigiendo match exacto.
     * Resuelve a { ok, display, msg }; nunca rechaza, para que cada isla lo muestre igual.
     */
    function verifyCode(code) {
        code = (code || '').trim();
        if (!code) return Promise.resolve({ ok: false, msg: 'Escribí un código para verificar.' });
        if (!/^[0-9]{6,18}$/.test(code)) {
            return Promise.resolve({ ok: false, msg: 'Un código SNOMED CT son solo dígitos (6-18). Revisá lo tipeado.' });
        }
        return expand(code, 5)
            .then(function (data) {
                var results = data;
                for (var i = 0; i < results.length; i++) {
                    // Match exacto: $expand con filtro numérico puede devolver conceptos cuyo
                    // código apenas contiene lo tipeado, y aceptar el primero codificaría algo
                    // distinto de lo que el usuario escribió.
                    if (results[i].code === code) return { ok: true, display: results[i].display };
                }
                return { ok: false, msg: 'El código ' + code + ' no existe en SNOMED CT (o no está en el subset activo).' };
            })
            .catch(function (err) { return { ok: false, msg: err.message }; });
    }

    window.ChequeARSnomed = {
        createSearch: createSearch,
        verifyCode: verifyCode,
        getToken: getToken,
        diagnostico: diagnostico
    };
})(window);
