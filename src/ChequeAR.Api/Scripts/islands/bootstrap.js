/*
 * Vue islands bootstrap.
 * Recorre [data-vue-island] y monta el componente registrado.
 * Cada isla se auto-registra llamando a window.ChequeARIslands.register(name, component).
 */
(function () {
    var registry = {};
    window.ChequeARIslands = {
        register: function (name, component) {
            registry[name] = component;
            mountPending(name);
        },
        // Credenciales para llamar a la API desde cualquier isla. El JWT lo estampa
        // _Layout en <meta name="cq-token"> (ver Auth/SessionToken). Único lugar que lo
        // lee: antes cada isla tenía su propia copia de esto.
        auth: {
            token: function () {
                var m = document.querySelector('meta[name="cq-token"]');
                return m ? m.getAttribute('content') : null;
            },
            /// json=true agrega Content-Type: application/json (no va con FormData).
            headers: function (json) {
                var h = json ? { 'Content-Type': 'application/json' } : {};
                var t = window.ChequeARIslands.auth.token();
                if (t) h['Authorization'] = 'Bearer ' + t;
                return h;
            }
        }
    };

    function mountPending(name) {
        if (!window.Vue) return;
        var component = registry[name];
        if (!component) return;
        var nodes = document.querySelectorAll('[data-vue-island="' + name + '"]:not([data-vue-mounted])');
        Array.prototype.forEach.call(nodes, function (el) {
            var rawProps = el.getAttribute('data-props');
            var props = {};
            if (rawProps) { try { props = JSON.parse(rawProps); } catch (e) { console.error('Bad data-props on', el, e); } }
            el.innerHTML = '';
            var app = Vue.createApp(component, props);
            // <cq-date-range> es un custom element nativo, no un componente Vue.
            app.config.compilerOptions.isCustomElement = function (tag) {
                return tag === 'cq-date-range';
            };
            app.mount(el);
            el.setAttribute('data-vue-mounted', '1');
        });
    }

    function mountAll() {
        Object.keys(registry).forEach(mountPending);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', mountAll);
    } else {
        mountAll();
    }
})();
