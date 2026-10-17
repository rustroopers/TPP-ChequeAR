/*
 * <cq-date-range> — selector de rango de fechas en un único display con calendario propio.
 *
 * Se hace clic en una fecha y luego en otra; el componente resalta el rango y decide solo
 * cuál es "desde" y cuál "hasta" (si la segunda es anterior, las intercambia). Mientras se
 * elige la segunda, muestra un preview al pasar el mouse.
 *
 * Misma API que antes (no hace falta tocar las pantallas que lo usan):
 *   <cq-date-range name-from="from" name-to="to"
 *                  value-from="2026-07-01" value-to="2026-07-31"></cq-date-range>
 *
 * - name-from / name-to: nombres de los <input hidden> internos, para enviar con el <form>.
 * - value-from / value-to: valores iniciales (yyyy-MM-dd). Reactivos desde Vue (:value-from).
 * - Emite 'range-change' (bubbles) con detail { from, to } para consumo desde Vue.
 * - Al confirmarse el rango dispara un 'change' que burbujea al <form> (lo usa cq-autofilter
 *   para refrescar sin botón "Filtrar").
 */
(function () {
    'use strict';

    var MONTHS = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
        'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
    var WEEK = ['lu', 'ma', 'mi', 'ju', 'vi', 'sá', 'do'];

    function pad(n) { return String(n).length < 2 ? '0' + n : String(n); }
    function toISO(d) { return d ? d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) : ''; }
    function parseISO(s) {
        if (!s) return null;
        var p = String(s).split('-');
        if (p.length !== 3) return null;
        var d = new Date(+p[0], +p[1] - 1, +p[2]);
        return isNaN(d.getTime()) ? null : d;
    }
    function fmt(d) { return d ? pad(d.getDate()) + '/' + pad(d.getMonth() + 1) + '/' + d.getFullYear() : ''; }
    function midnight(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }
    function sameDay(a, b) { return !!a && !!b && a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate(); }

    class CqDateRange extends HTMLElement {
        static get observedAttributes() { return ['value-from', 'value-to']; }

        connectedCallback() {
            if (this._built) return;
            this._built = true;
            this._syncing = false;
            this._picking = false;   // true tras el primer clic, esperando la fecha final
            this._hover = null;

            this._start = parseISO(this.getAttribute('value-from'));
            this._end = parseISO(this.getAttribute('value-to'));
            var base = this._start || this._end || new Date();
            this._view = new Date(base.getFullYear(), base.getMonth(), 1);

            var nameFrom = this.getAttribute('name-from') || 'from';
            var nameTo = this.getAttribute('name-to') || 'to';

            this.classList.add('cq-daterange');
            this.innerHTML =
                '<button type="button" class="cq-daterange__trigger">' +
                    '<i class="bi bi-calendar-range cq-daterange__ic" aria-hidden="true"></i>' +
                    '<span class="cq-daterange__label"></span>' +
                    '<i class="bi bi-chevron-down cq-daterange__caret" aria-hidden="true"></i>' +
                '</button>' +
                '<input type="hidden" data-part="from">' +
                '<input type="hidden" data-part="to">' +
                '<div class="cq-daterange__pop" hidden></div>';

            this._trigger = this.querySelector('.cq-daterange__trigger');
            this._label = this.querySelector('.cq-daterange__label');
            this._fromInput = this.querySelector('[data-part="from"]');
            this._toInput = this.querySelector('[data-part="to"]');
            this._pop = this.querySelector('.cq-daterange__pop');
            this._fromInput.name = nameFrom;
            this._toInput.name = nameTo;

            this._syncInputs();
            this._renderLabel();

            var self = this;
            this._trigger.addEventListener('click', function () { self._toggle(); });
            this._pop.addEventListener('click', function (e) { self._onPopClick(e); });
            this._pop.addEventListener('mouseover', function (e) { self._onPopHover(e); });
            this._pop.addEventListener('mouseleave', function () {
                if (self._picking && self._hover) { self._hover = null; self._paintRange(); }
            });
            this._onDocDown = function (e) { if (!self.contains(e.target)) self._close(); };
            this._onKey = function (e) { if (e.key === 'Escape') self._close(); };
        }

        // ---- apertura / cierre ------------------------------------------------
        _toggle() { if (this._pop.hasAttribute('hidden')) this._open(); else this._close(); }

        _open() {
            var base = this._start || this._end || new Date();
            this._view = new Date(base.getFullYear(), base.getMonth(), 1);
            this._picking = false; this._hover = null;
            this._renderGrid();
            this._pop.removeAttribute('hidden');
            this.classList.add('is-open');
            document.addEventListener('mousedown', this._onDocDown);
            document.addEventListener('keydown', this._onKey);
            // Si el popup se sale por la derecha, lo anclamos al borde derecho del trigger.
            this._pop.style.left = '0'; this._pop.style.right = 'auto';
            if (this._pop.getBoundingClientRect().right > window.innerWidth - 8) {
                this._pop.style.left = 'auto'; this._pop.style.right = '0';
            }
        }

        _close() {
            // Si quedó a mitad de selección (solo el "desde"), lo confirmamos como rango abierto.
            if (this._picking && this._start) { this._picking = false; this._commit(); }
            else this._picking = false;
            this._hover = null;
            this._pop.setAttribute('hidden', '');
            this.classList.remove('is-open');
            document.removeEventListener('mousedown', this._onDocDown);
            document.removeEventListener('keydown', this._onKey);
            this._renderLabel();
        }

        // ---- interacción ------------------------------------------------------
        _onPopClick(e) {
            var nav = e.target.closest('[data-nav]');
            if (nav) {
                this._view = new Date(this._view.getFullYear(), this._view.getMonth() + (+nav.getAttribute('data-nav')), 1);
                this._renderGrid();
                return;
            }
            if (e.target.closest('[data-act="clear"]')) {
                this._start = null; this._end = null; this._picking = false; this._hover = null;
                this._commit(); this._paintRange();
                return;
            }
            var dayBtn = e.target.closest('[data-day]');
            if (!dayBtn) return;
            var d = parseISO(dayBtn.getAttribute('data-day'));
            if (!this._picking) {
                // Primer clic: arranca un rango nuevo (aún sin confirmar).
                this._start = d; this._end = null; this._picking = true; this._hover = null;
                this._paintRange(); this._renderLabel();
            } else {
                // Segundo clic: confirma. Si es anterior al "desde", _commit las intercambia.
                this._end = d; this._picking = false; this._hover = null;
                this._commit(); this._close();
            }
        }

        _onPopHover(e) {
            if (!this._picking) return;
            var btn = e.target.closest('[data-day]');
            if (!btn) return;
            var d = parseISO(btn.getAttribute('data-day'));
            if (!sameDay(d, this._hover)) { this._hover = d; this._paintRange(); }
        }

        // ---- render -----------------------------------------------------------
        // Construye la grilla del mes una sola vez. El resaltado del rango se aplica aparte
        // (_paintRange), para no reconstruir el DOM en cada movimiento del mouse.
        _renderGrid() {
            var y = this._view.getFullYear(), m = this._view.getMonth();
            var first = new Date(y, m, 1);
            var offset = (first.getDay() + 6) % 7;            // lunes = 0
            var cell = new Date(y, m, 1 - offset);
            var today = midnight(new Date());

            var html = '<div class="cq-daterange__nav">' +
                '<button type="button" class="cq-daterange__navbtn" data-nav="-1" aria-label="Mes anterior"><i class="bi bi-chevron-left"></i></button>' +
                '<span class="cq-daterange__title">' + MONTHS[m] + ' ' + y + '</span>' +
                '<button type="button" class="cq-daterange__navbtn" data-nav="1" aria-label="Mes siguiente"><i class="bi bi-chevron-right"></i></button>' +
                '</div>';
            html += '<div class="cq-daterange__week">' + WEEK.map(function (w) { return '<span>' + w + '</span>'; }).join('') + '</div>';
            html += '<div class="cq-daterange__days">';
            for (var i = 0; i < 42; i++) {
                var d = new Date(cell.getFullYear(), cell.getMonth(), cell.getDate() + i);
                var cls = 'cq-daterange__day';
                if (d.getMonth() !== m) cls += ' is-adjacent';
                if (sameDay(d, today)) cls += ' is-today';
                html += '<button type="button" class="' + cls + '" data-day="' + toISO(d) + '">' + d.getDate() + '</button>';
            }
            html += '</div>';
            html += '<div class="cq-daterange__foot">' +
                '<button type="button" class="cq-daterange__clear" data-act="clear">Limpiar</button>' +
                '<span class="cq-daterange__hint"></span>' +
                '</div>';
            this._pop.innerHTML = html;

            this._dayEls = Array.prototype.map.call(
                this._pop.querySelectorAll('[data-day]'),
                function (el) { return { el: el, d: parseISO(el.getAttribute('data-day')) }; });
            this._hintEl = this._pop.querySelector('.cq-daterange__hint');
            this._paintRange();
        }

        // Actualiza solo las clases de resaltado según el rango (con preview del hover).
        _paintRange() {
            if (!this._dayEls) return;
            var rs = this._start, re = this._end;
            if (this._picking && this._start && this._hover) {
                if (this._hover < this._start) { rs = this._hover; re = this._start; }
                else { rs = this._start; re = this._hover; }
            }
            this._dayEls.forEach(function (o) {
                var d = o.d, el = o.el;
                el.classList.remove('is-inrange', 'is-start', 'is-end', 'is-selected');
                var isStart = sameDay(d, rs), isEnd = sameDay(d, re);
                if (rs && re && d > rs && d < re) el.classList.add('is-inrange');
                if (isStart) el.classList.add('is-start');
                if (isEnd) el.classList.add('is-end');
                if (isStart || isEnd) el.classList.add('is-selected');
            });
            if (this._hintEl) this._hintEl.textContent = this._picking ? 'Elija la fecha final' : '';
        }

        _renderLabel() {
            var s = this._start, e = this._end;
            if (!s && !e) { this._label.textContent = 'Elegir período'; this._label.classList.add('is-placeholder'); return; }
            this._label.classList.remove('is-placeholder');
            if (s && e) this._label.textContent = fmt(s) + '  →  ' + fmt(e);
            else this._label.textContent = 'Desde ' + fmt(s || e);
        }

        // ---- estado / emisión -------------------------------------------------
        _syncInputs() {
            this._fromInput.value = toISO(this._start);
            this._toInput.value = toISO(this._end);
        }

        _commit() {
            if (this._start && this._end && this._end < this._start) {
                var t = this._start; this._start = this._end; this._end = t;
            }
            this._syncInputs();
            this._renderLabel();
            if (this._syncing) return;
            this.dispatchEvent(new CustomEvent('range-change', {
                detail: { from: toISO(this._start), to: toISO(this._end) },
                bubbles: true
            }));
            this.dispatchEvent(new Event('change', { bubbles: true }));
        }

        attributeChangedCallback(name, oldV, newV) {
            if (!this._built) return;
            this._syncing = true;
            if (name === 'value-from') this._start = parseISO(newV);
            else if (name === 'value-to') this._end = parseISO(newV);
            this._syncInputs();
            this._renderLabel();
            if (!this._pop.hasAttribute('hidden')) this._renderGrid();
            this._syncing = false;
        }
    }

    if (!customElements.get('cq-date-range')) {
        customElements.define('cq-date-range', CqDateRange);
    }
})();
