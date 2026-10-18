/*
 * cq-select-enhance — convierte todo <select class="form-select"> en un dropdown
 * Bootstrap custom (.cq-select). Motivo: los <option> nativos los pinta el sistema
 * operativo y no aceptan CSS, así que para tener consistencia visual con el
 * dropdown de "Todas las especialidades" tenemos que reemplazarlos.
 *
 * Cómo funciona:
 *  1. Encuentra cada <select class="form-select">, salvo los que llevan
 *     data-cq-noenhance (por si algún caso puntual necesita el select nativo).
 *  2. Genera un <div class="dropdown cq-select"> hermano con un botón toggle
 *     que muestra el label de la opción actual y un <ul.dropdown-menu> con las
 *     mismas opciones estilizadas.
 *  3. El <select> original queda oculto pero SIGUE en el DOM: las formas normales
 *     lo submiten, y el JS de la app puede leer/escribir .value sin cambios.
 *  4. Al elegir una opción del custom: se actualiza select.value y se dispara
 *     'change' con bubbles → cualquier listener existente (cq-autofilter,
 *     applyQFilter, etc.) sigue funcionando igual.
 *  5. Se observa el <select> por si otro script cambia .value programáticamente:
 *     el toggle se re-renderiza para reflejarlo.
 */
(function () {
    'use strict';

    function slug() { return 'cqsel_' + Math.random().toString(36).slice(2, 9); }

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function currentOption(select) {
        var idx = select.selectedIndex;
        if (idx < 0 || idx >= select.options.length) return null;
        return select.options[idx];
    }

    function renderToggleLabel(dropdown, select) {
        var opt = currentOption(select);
        var label = dropdown.querySelector('.cq-select__label');
        if (!label) return;
        // Usamos textContent (no innerHTML): el enhancer trabaja con opciones
        // nativas que no traen markup, así se evita cualquier riesgo de inyección.
        label.textContent = opt ? opt.text : (select.getAttribute('data-placeholder') || 'Elegir…');
    }

    function buildMenuItems(select) {
        var out = [];
        for (var i = 0; i < select.options.length; i++) {
            var o = select.options[i];
            if (o.disabled) continue;
            out.push(
                '<li><a class="dropdown-item cq-select__opt" href="javascript:" ' +
                'data-cq-index="' + i + '">' +
                    '<span>' + esc(o.text) + '</span>' +
                '</a></li>'
            );
        }
        return out.join('');
    }

    function enhance(select) {
        if (select.__cqEnhanced) return;
        if (select.hasAttribute('data-cq-noenhance')) return;
        // Un <select multiple> o de size>1 no cabe en un dropdown de una opción:
        // lo dejamos como está.
        if (select.multiple || (select.size && select.size > 1)) return;

        select.__cqEnhanced = true;

        var isSmall = select.classList.contains('form-select-sm');
        var wrapper = document.createElement('div');
        wrapper.className = 'dropdown cq-select' + (isSmall ? ' cq-select--sm' : '');
        var id = select.id || slug();

        // El layout pone los dropdowns en posición fija; acá el menú es w-100 y con fixed ese
        // 100% pasa a ser el ancho de la ventana. Vuelve a absolute para medir contra el wrapper.
        wrapper.innerHTML =
            '<button class="btn cq-select__toggle dropdown-toggle w-100" type="button" ' +
                'data-bs-toggle="dropdown" aria-expanded="false" data-cq-select-toggle="' + id + '" ' +
                'data-bs-popper-config=\'{"strategy":"absolute"}\'>' +
                '<span class="cq-select__label"></span>' +
            '</button>' +
            '<ul class="dropdown-menu w-100">' + buildMenuItems(select) + '</ul>';

        // Oculta el select original y lo pone dentro del wrapper como storage.
        select.parentNode.insertBefore(wrapper, select);
        wrapper.appendChild(select);
        select.classList.add('cq-select__native');
        select.setAttribute('aria-hidden', 'true');
        select.tabIndex = -1;

        renderToggleLabel(wrapper, select);

        // Cambios programáticos del <select> (por ejemplo desde Razor tras un
        // submit o desde otro script): reflejarlos en el toggle.
        var mo = new MutationObserver(function () { renderToggleLabel(wrapper, select); });
        mo.observe(select, { attributes: true, attributeFilter: ['value'] });
        select.addEventListener('change', function (e) {
            if (e.__cqInternal) return;
            renderToggleLabel(wrapper, select);
        });

        wrapper.addEventListener('click', function (e) {
            var opt = e.target.closest('.cq-select__opt');
            if (!opt) return;
            e.preventDefault();
            var idx = parseInt(opt.getAttribute('data-cq-index'), 10);
            if (isNaN(idx) || idx < 0 || idx >= select.options.length) return;
            select.selectedIndex = idx;
            renderToggleLabel(wrapper, select);
            var ev = new Event('change', { bubbles: true });
            ev.__cqInternal = true;
            select.dispatchEvent(ev);
        });
    }

    function enhanceAll(root) {
        (root || document).querySelectorAll('select.form-select:not(.cq-select__native)')
            .forEach(enhance);
    }

    // Reconstruye el menú de un select ya enhanced. Necesario cuando el JS de
    // la app agrega/quita <option>s después de que el enhancer corrió: el menú
    // sigue mostrando el snapshot original y el label del toggle no refleja el
    // valor actual. Uso: window.cqRebuildSelect(document.getElementById(id)).
    function rebuild(select) {
        if (!select) return;
        var wrapper = select.closest('.cq-select');
        if (!wrapper) { enhance(select); return; }
        var menu = wrapper.querySelector('.dropdown-menu');
        if (menu) menu.innerHTML = buildMenuItems(select);
        renderToggleLabel(wrapper, select);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { enhanceAll(); });
    } else {
        enhanceAll();
    }
    // Exportamos ambos: enhance para selects nuevos, rebuild para selects
    // enhanced cuyas opciones cambiaron.
    window.cqEnhanceSelects = enhanceAll;
    window.cqRebuildSelect = rebuild;
})();
