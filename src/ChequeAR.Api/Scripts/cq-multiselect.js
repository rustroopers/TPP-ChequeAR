/*
 * <cq-multiselect> — combo buscable de selección múltiple.
 *
 * Misma cara que el combobox de Reportes (.cq-combo): un input en el que se tipea para
 * achicar la lista, con un desplegable donde se tilda. La diferencia es que acá se puede
 * elegir más de uno.
 *
 * Mejora progresiva sobre un <select multiple> real:
 *
 *   <cq-multiselect placeholder="Todos" label-plural="cuestionarios">
 *       <select name="questionnaire" multiple>
 *           <option value="..." selected>Título</option>
 *       </select>
 *   </cq-multiselect>
 *
 * El <select> nativo es el que envía el formulario (name repetido), así que sin JS la
 * pantalla sigue funcionando: se ve el multi-select del navegador.
 *
 * Dos cosas que el componente tiene que hacer bien porque vive dentro de un
 * <form data-cq-autofilter>:
 *
 *  1. El input de búsqueda NO es un filtro del formulario: es UI interna. cq-autofilter
 *     re-envía el form ante cualquier 'input' en un campo de texto, así que sin frenar la
 *     propagación se recargaba la página a media palabra. Se cortan input/keydown/change.
 *  2. El 'change' del <select> se emite al CERRAR, no en cada tilde: elegir tres opciones
 *     no puede costar tres recargas. Mismo criterio que <cq-date-range> con su rango.
 */
(function () {
    'use strict';

    function norm(s) { return (s || '').toLowerCase(); }
    function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;'); }

    class CqMultiSelect extends HTMLElement {
        connectedCallback() {
            if (this._built) return;
            this._select = this.querySelector('select');
            if (!this._select) return;      // sin <select> adentro no hay nada que mejorar
            this._built = true;

            this._placeholder = this.getAttribute('placeholder') || 'Todos';
            this._plural = this.getAttribute('label-plural') || 'seleccionados';
            this._select.multiple = true;
            this._select.classList.add('cq-multi__native');

            this._options = Array.prototype.map.call(this._select.options, function (o) {
                return { value: o.value, label: o.text, selected: o.selected };
            }).filter(function (o) { return o.value !== ''; });

            this.classList.add('cq-combo', 'cq-multi');
            var box = document.createElement('div');
            box.innerHTML =
                '<input type="text" class="form-control cq-combo__input cq-multi__input" autocomplete="off">' +
                '<i class="bi bi-chevron-down cq-combo__caret" aria-hidden="true"></i>' +
                '<div class="cq-combo__pop cq-multi__pop" hidden>' +
                    '<div class="cq-multi__list"></div>' +
                    '<div class="cq-multi__foot">' +
                        '<button type="button" class="cq-daterange__clear" data-act="clear">Limpiar</button>' +
                        '<span class="cq-multi__count"></span>' +
                    '</div>' +
                '</div>';
            while (box.firstChild) this.appendChild(box.firstChild);

            this._input = this.querySelector('.cq-multi__input');
            this._pop = this.querySelector('.cq-multi__pop');
            this._list = this.querySelector('.cq-multi__list');
            this._countEl = this.querySelector('.cq-multi__count');

            var self = this;

            // --- aislamiento del formulario (ver nota 1 del encabezado) ---------
            // Se frena en la fase de captura y también al burbujear, para que el listener
            // del <form> no llegue a ver nada de lo que pasa adentro del combo.
            ['input', 'change', 'keydown', 'keyup'].forEach(function (type) {
                self._input.addEventListener(type, function (e) { e.stopPropagation(); });
            });

            this._input.addEventListener('input', function () { self._open(); self._renderList(); });
            this._input.addEventListener('focus', function () { self._open(); });
            this._input.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { self._close(); return; }
                if (e.key !== 'Enter') return;
                // Enter tiene que tildar, no enviar el formulario a medio escribir.
                e.preventDefault();
                var visible = self._visibleOptions();
                if (visible.length === 1) self._toggleValue(visible[0].value);
            });
            // Tabular fuera cierra y confirma. Clickear una opción NO dispara blur porque el
            // mousedown del popup lo previene, así que el foco no se pierde al tildar.
            this._input.addEventListener('blur', function () { self._close(); });
            this._pop.addEventListener('mousedown', function (e) { e.preventDefault(); }); // no robar el foco
            this._pop.addEventListener('click', function (e) { self._onPopClick(e); });
            this._onDocDown = function (e) { if (!self.contains(e.target)) self._close(); };
            document.addEventListener('mousedown', this._onDocDown);

            this._renderPlaceholder();
        }

        disconnectedCallback() {
            if (this._onDocDown) document.removeEventListener('mousedown', this._onDocDown);
        }

        // ---- apertura / cierre ------------------------------------------------
        _open() {
            if (!this._pop.hasAttribute('hidden')) return;
            this._dirty = false;
            this._renderList();
            this._pop.removeAttribute('hidden');
            this.classList.add('is-open');
        }

        _close() {
            if (this._pop.hasAttribute('hidden')) return;
            this._pop.setAttribute('hidden', '');
            this.classList.remove('is-open');
            this._input.value = '';           // el resumen vuelve a verse en el placeholder
            this._renderPlaceholder();
            // Recién acá se avisa al formulario, y sólo si algo cambió: abrir y cerrar sin
            // tocar nada no tiene por qué recargar el listado.
            if (this._dirty) {
                this._dirty = false;
                this._select.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }

        // ---- selección ----------------------------------------------------------
        _onPopClick(e) {
            if (e.target.closest('[data-act="clear"]')) {
                var changed = this._options.some(function (o) { return o.selected; });
                this._options.forEach(function (o) { o.selected = false; });
                if (changed) this._commit();
                this._renderList();
                return;
            }
            var opt = e.target.closest('[data-value]');
            if (opt) this._toggleValue(opt.getAttribute('data-value'));
        }

        _toggleValue(value) {
            var target = null;
            this._options.forEach(function (o) { if (o.value === value) target = o; });
            if (!target) return;
            target.selected = !target.selected;
            this._commit();
            this._renderList();
        }

        /// Vuelca el estado al <select> nativo, que es lo que se envía.
        _commit() {
            var byValue = {};
            this._options.forEach(function (o) { byValue[o.value] = o.selected; });
            Array.prototype.forEach.call(this._select.options, function (o) {
                if (o.value !== '') o.selected = !!byValue[o.value];
            });
            this._dirty = true;
        }

        // ---- render -------------------------------------------------------------
        _visibleOptions() {
            var term = norm(this._input.value.trim());
            return this._options.filter(function (o) {
                return !term || norm(o.label).indexOf(term) >= 0;
            });
        }

        _renderList() {
            var visible = this._visibleOptions();
            this._list.innerHTML = visible.length
                ? visible.map(function (o) {
                    return '<button type="button" class="cq-combo__opt cq-multi__opt' +
                        (o.selected ? ' is-checked' : '') + '" data-value="' + esc(o.value) + '">' +
                        '<i class="bi ' + (o.selected ? 'bi-check-square-fill' : 'bi-square') + '" aria-hidden="true"></i>' +
                        '<span>' + esc(o.label) + '</span>' +
                        '</button>';
                }).join('')
                : '<div class="cq-combo__empty">Sin resultados</div>';

            var n = this._options.filter(function (o) { return o.selected; }).length;
            this._countEl.textContent = n ? n + ' seleccionado' + (n === 1 ? '' : 's') : '';
        }

        /// El resumen de lo elegido vive en el placeholder: así el input queda libre para
        /// tipear sin tener que borrar antes lo que ya estaba seleccionado.
        _renderPlaceholder() {
            var picked = this._options.filter(function (o) { return o.selected; });
            this._input.placeholder = !picked.length
                ? this._placeholder
                : (picked.length === 1 ? picked[0].label : picked.length + ' ' + this._plural);
            this.classList.toggle('has-selection', picked.length > 0);
        }
    }

    if (!customElements.get('cq-multiselect')) {
        customElements.define('cq-multiselect', CqMultiSelect);
    }
})();
