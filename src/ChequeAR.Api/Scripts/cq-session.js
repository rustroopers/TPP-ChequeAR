/*
 * Envuelve fetch para las llamadas a /api, /fhir y /auth: pone el token vigente del meta
 * cq-token y, ante un 401, lo renueva con /Account/Token y reintenta una vez.
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

    function cqFetch(input, init) {
        var url = typeof input === 'string' ? input : (input && input.url);
        // Un Request ya armado no se puede reenviar: su body se consume.
        if (typeof input !== 'string' || !isApi(url)) return nativeFetch(input, init);

        return nativeFetch(input, withToken(init, currentToken())).then(function (res) {
            if (res.status !== 401) return res;
            return refreshToken().then(function (t) {
                return t ? nativeFetch(input, withToken(init, t)) : res;
            });
        });
    }
    cqFetch.__cqSession = true;
    window.fetch = cqFetch;
})();
