/*
 * cq-autofilter — filtros sin botón "Filtrar".
 *
 * En cualquier <form data-cq-autofilter>, el formulario se re-envía solo a medida que
 * el usuario completa los campos:
 *   - inputs de texto/búsqueda: con debounce (evita recargar en cada tecla).
 *   - selects y fechas (incluye <cq-date-range>): al instante.
 *
 * Como el listado se renderiza en el server (GET + recarga), se preserva el foco y la
 * posición del cursor del campo que se estaba editando, para poder seguir tipeando.
 */
(function () {
    'use strict';

    var DEBOUNCE_MS = 450;
    var STORAGE_KEY = 'cq-autofilter-focus';

    function formKey(form) {
        return (form.getAttribute('action') || location.pathname) + '#' +
            (form.getAttribute('data-cq-autofilter') || 'default');
    }

    function isTextField(el) {
        if (!el || el.tagName !== 'INPUT') return false;
        var t = (el.getAttribute('type') || 'text').toLowerCase();
        return t === 'text' || t === 'search' || t === 'number' || t === 'email' || t === 'tel';
    }

    function saveFocus(form, el) {
        try {
            var data = { key: formKey(form), name: el.name || '', start: null, end: null };
            if (isTextField(el) && el.type !== 'number') {
                data.start = el.selectionStart;
                data.end = el.selectionEnd;
            }
            sessionStorage.setItem(STORAGE_KEY, JSON.stringify(data));
        } catch (e) { /* sessionStorage no disponible: se pierde el foco, sin más */ }
    }

    function restoreFocus() {
        var raw;
        try { raw = sessionStorage.getItem(STORAGE_KEY); } catch (e) { return; }
        if (!raw) return;
        try { sessionStorage.removeItem(STORAGE_KEY); } catch (e) { /* noop */ }

        var data;
        try { data = JSON.parse(raw); } catch (e) { return; }
        if (!data || !data.name) return;

        var forms = document.querySelectorAll('form[data-cq-autofilter]');
        for (var i = 0; i < forms.length; i++) {
            if (formKey(forms[i]) !== data.key) continue;
            var el = forms[i].querySelector('[name="' + data.name + '"]');
            if (!el) return;
            el.focus();
            if (data.start != null && typeof el.setSelectionRange === 'function') {
                try { el.setSelectionRange(data.start, data.end); } catch (e) { /* noop */ }
            }
            return;
        }
    }

    function wire(form) {
        var timer = null;

        function submitNow(el) {
            if (timer) { clearTimeout(timer); timer = null; }
            saveFocus(form, el);
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
        }

        // Texto: debounce.
        form.addEventListener('input', function (e) {
            if (!isTextField(e.target)) return;
            var el = e.target;
            if (timer) clearTimeout(timer);
            timer = setTimeout(function () { submitNow(el); }, DEBOUNCE_MS);
        });

        // Selects y fechas (incluye el 'change' nativo que burbujea desde <cq-date-range>).
        form.addEventListener('change', function (e) {
            if (isTextField(e.target)) return; // el texto ya se maneja por 'input'
            submitNow(e.target);
        });

        // Enter en un campo de texto: filtrar ya, sin esperar el debounce.
        form.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && isTextField(e.target)) {
                e.preventDefault();
                submitNow(e.target);
            }
        });
    }

    function init() {
        var forms = document.querySelectorAll('form[data-cq-autofilter]');
        Array.prototype.forEach.call(forms, wire);
        restoreFocus();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
