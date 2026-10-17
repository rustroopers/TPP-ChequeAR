/*
 * Envuelve fetch para las llamadas a /api, /fhir y /auth: pone el token vigente del meta
 * cq-token y, ante un 401, lo renueva con /Account/Token y reintenta una vez. Si la sesión
 * venció del todo, pide la contraseña sin salir de la página.
 */
(function () {
    if (!window.fetch || window.fetch.__cqSession) return;

    var nativeFetch = window.fetch.bind(window);
    var refreshing = null;

    function meta() { return document.querySelector('meta[name="cq-token"]'); }
    function currentToken() { var m = meta(); return m ? m.getAttribute('content') : null; }

    function isApi(url) {
        try {
            var u = new URL(url, window.location.href);
            if (u.origin !== window.location.origin) return false;
            return /^\/(api|fhir|auth)\//i.test(u.pathname);
        } catch (e) { return false; }
    }

    function withToken(init, token) {
        init = init ? Object.assign({}, init) : {};
        var headers = new Headers(init.headers || {});
        if (token) headers.set('Authorization', 'Bearer ' + token);
        init.headers = headers;
        return init;
    }

    function refreshToken() {
        if (!refreshing) {
            refreshing = nativeFetch('/Account/Token', {
                credentials: 'same-origin',
                cache: 'no-store',
                headers: { 'Accept': 'application/json' }
            }).then(function (r) {
                if (!r.ok) return null;
                return r.json().then(function (b) {
                    var t = b && b.token;
                    var m = meta();
                    if (t && m) m.setAttribute('content', t);
                    return t || null;
                });
            }).catch(function () { return null; })
              .then(function (t) { refreshing = null; return t; });
        }
        return refreshing;
    }

    // Si la cookie también venció, /Account/Token no da token: se pide la contraseña acá
    // mismo, sin recargar, para no perder lo cargado. Todos los pedidos que chocaron
    // esperan el mismo reingreso.
    var reauthing = null;
    function reauthenticate() {
        if (!reauthing) reauthing = new Promise(showOverlay).then(function (t) { reauthing = null; return t; });
        return reauthing;
    }

    function showOverlay(resolve) {
        var user = (document.querySelector('meta[name="cq-user"]') || {}).content || '';
        var back = document.createElement('div');
        back.style.cssText = 'position:fixed; inset:0; background:rgba(0,0,0,.45); z-index:2000; display:flex; align-items:center; justify-content:center; padding:16px;';
        back.innerHTML =
            '<form class="cq-card" role="dialog" aria-modal="true" aria-labelledby="cqReauthTitle" style="max-width:380px; width:100%;">' +
            '  <h2 id="cqReauthTitle" class="cq-section-title" style="margin-top:0;">La sesión expiró</h2>' +
            '  <p class="cq-text-sm cq-text-muted">Ingrese su contraseña para seguir. Lo cargado no se pierde.</p>' +
            '  <label class="form-label" for="cqReauthPassword">Contraseña de <strong></strong></label>' +
            '  <input id="cqReauthPassword" type="password" class="form-control" autocomplete="current-password" required />' +
            '  <div class="text-danger cq-text-sm mt-2" role="alert"></div>' +
            '  <div class="d-flex justify-content-end gap-2 border-top pt-3 mt-3">' +
            '    <a class="btn btn-outline-secondary">Cerrar sesión</a>' +
            '    <button type="submit" class="btn btn-primary">Continuar</button>' +
            '  </div>' +
            '</form>';
        var form = back.querySelector('form');
        var input = back.querySelector('input');
        var error = back.querySelector('[role="alert"]');
        var button = back.querySelector('button');
        back.querySelector('strong').textContent = user;
        back.querySelector('a').href = '/Account/Login?returnUrl=' + encodeURIComponent(location.pathname + location.search);

        form.addEventListener('submit', function (ev) {
            ev.preventDefault();
            button.disabled = true;
            error.textContent = '';
            var body = new URLSearchParams();
            body.set('username', user);
            body.set('password', input.value);
            nativeFetch('/Account/Reauth', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'X-CQ-Reauth': '1', 'Content-Type': 'application/x-www-form-urlencoded' },
                body: body.toString()
            }).then(function (r) {
                if (r.ok) return refreshToken();
                return r.json().catch(function () { return null; }).then(function (b) {
                    throw new Error((b && b.error) || 'No se pudo iniciar sesión.');
                });
            }).then(function (t) {
                if (!t) throw new Error('No se pudo iniciar sesión.');
                document.body.removeChild(back);
                resolve(t);
            }).catch(function (e) {
                error.textContent = e.message;
                input.select();
            }).then(function () { button.disabled = false; });
        });

        document.body.appendChild(back);
        input.focus();
    }

    function cqFetch(input, init) {
        var url = typeof input === 'string' ? input : (input && input.url);
        // Un Request ya armado no se puede reenviar: su body se consume.
        if (typeof input !== 'string' || !isApi(url)) return nativeFetch(input, init);

        return nativeFetch(input, withToken(init, currentToken())).then(function (res) {
            if (res.status !== 401) return res;
            return refreshToken().then(function (t) {
                if (t) return nativeFetch(input, withToken(init, t));
                // Saliendo de la página (keepalive) no hay a quién preguntarle.
                if (init && init.keepalive) return res;
                return reauthenticate().then(function (nuevo) { return nativeFetch(input, withToken(init, nuevo)); });
            });
        });
    }
    cqFetch.__cqSession = true;
    window.fetch = cqFetch;
})();
