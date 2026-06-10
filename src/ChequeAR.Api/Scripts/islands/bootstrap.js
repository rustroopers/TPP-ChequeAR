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
