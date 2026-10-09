/*
 * Isla Vue: patient-trend (pantalla Evolución de parámetros, spec 010)
 *
 * Elegido un cuestionario (en el form GET del servidor), esta isla:
 *   1. pide los parámetros graficables del cuestionario y los muestra como checkboxes,
 *   2. al marcar uno o más, pide sus series y dibuja un gráfico por parámetro,
 *   3. apila los gráficos compartiendo el mismo dominio de fechas en el eje X, y
 *   4. sombrea la franja de rango normal cuando la pregunta la tiene.
 *
 * Los gráficos son SVG dibujado a mano, sin librería: la app carga Vue/Bootstrap desde un
 * CDN y eso ya es un problema abierto para una intranet sin salida a internet; sumar una
 * librería de charts agregaría otra dependencia externa para una línea, unos puntos y una
 * banda. Mismo criterio que report-stats/index.js.
 *
 * Las respuestas de la Web API llegan en PascalCase (LinkId, Points, AuthoredAt…), igual que
 * /api/reports/stats: por eso se leen así y no en camelCase.
 */
(function () {
    // Geometría del lienzo (viewBox); el CSS lo escala al ancho disponible.
    var W = 640, H = 200, PADL = 52, PADR = 16, PADT = 16, PADB = 30;
    var INNERW = W - PADL - PADR;
    var INNERH = H - PADT - PADB;

    var PatientTrend = {
        props: {
            patientId: { type: String, default: null },
            questionnaireId: { type: String, default: null }
        },
        setup: function (props) {
            var params = Vue.ref([]);
            var selected = Vue.ref([]);        // linkIds marcados, en orden de selección
            var series = Vue.ref([]);
            var loadingParams = Vue.ref(false);
            var loadingSeries = Vue.ref(false);
            var error = Vue.ref(null);

            function authHeaders() { return window.ChequeARIslands.auth.headers(true); }

            function readJson(r) {
                return window.ChequeARIslands.http.ensureOk(r).then(function (ok) { return ok.json(); });
            }

            function base() {
                return '/api/patients/' + encodeURIComponent(props.patientId);
            }

            function loadParams() {
                if (!props.questionnaireId) return;
                loadingParams.value = true; error.value = null;
                fetch(base() + '/questionnaires/' + props.questionnaireId + '/trend-parameters', {
                    headers: authHeaders()
                }).then(readJson).then(function (d) {
                    params.value = (d && d.Parameters) || [];
                }).catch(function (e) {
                    error.value = e.message; params.value = [];
                }).then(function () { loadingParams.value = false; });
            }

            function loadSeries() {
                var ids = selected.value;
                if (!ids.length) { series.value = []; return; }
                loadingSeries.value = true; error.value = null;
                var url = base() + '/trend?questionnaireId=' + props.questionnaireId +
                    ids.map(function (l) { return '&linkId=' + encodeURIComponent(l); }).join('');
                fetch(url, { headers: authHeaders() }).then(readJson).then(function (d) {
                    series.value = (d && d.Series) || [];
                }).catch(function (e) {
                    error.value = e.message; series.value = [];
                }).then(function () { loadingSeries.value = false; });
            }

            function isSelected(linkId) { return selected.value.indexOf(linkId) !== -1; }

            function toggle(linkId) {
                var i = selected.value.indexOf(linkId);
                if (i === -1) selected.value = selected.value.concat([linkId]);
                else selected.value = selected.value.slice(0, i).concat(selected.value.slice(i + 1));
                loadSeries();
            }

            Vue.onMounted(loadParams);

            // --- formato -------------------------------------------------------------
            function fmtDate(ms) {
                var d = new Date(ms);
                if (isNaN(d.getTime())) return '';
                function pad(n) { return (n < 10 ? '0' : '') + n; }
                return pad(d.getDate()) + '/' + pad(d.getMonth() + 1) + '/' + String(d.getFullYear()).slice(2);
            }
            function fmtNum(v) {
                var n = Math.round(v * 100) / 100;
                return String(n);
            }
            // Etiqueta del eje Y según el tipo del parámetro: las de tipo date guardan su valor
            // como epoch en milisegundos, así que se muestran como fecha; el resto, como número.
            function fmtValueForType(v, type) {
                return type === 'date' ? fmtDate(v) : fmtNum(v);
            }

            // Dominio común de fechas entre TODAS las series seleccionadas con datos, para que
            // los ejes X de los gráficos apilados queden alineados (FR-005/FR-006).
            function commonXDomain() {
                var min = null, max = null;
                series.value.forEach(function (s) {
                    (s.Points || []).forEach(function (p) {
                        var t = new Date(p.AuthoredAt).getTime();
                        if (isNaN(t)) return;
                        if (min === null || t < min) min = t;
                        if (max === null || t > max) max = t;
                    });
                });
                return { min: min, max: max };
            }

            function buildChart(s, xDom) {
                var pts = (s.Points || []).slice().sort(function (a, b) {
                    return new Date(a.AuthoredAt).getTime() - new Date(b.AuthoredAt).getTime();
                });
                var base = { linkId: s.LinkId, text: s.Text, unit: s.Unit, type: s.Type };
                if (!pts.length) { base.empty = true; return base; }

                // Dominio Y: valores de la serie, más los extremos del rango normal si existen,
                // con un 10% de aire arriba y abajo.
                var vals = pts.map(function (p) { return p.Value; });
                var yMin = Math.min.apply(null, vals), yMax = Math.max.apply(null, vals);
                var nr = s.NormalRange;
                if (nr) {
                    if (nr.Min !== null && nr.Min !== undefined) yMin = Math.min(yMin, nr.Min);
                    if (nr.Max !== null && nr.Max !== undefined) yMax = Math.max(yMax, nr.Max);
                }
                if (yMin === yMax) { yMin -= 1; yMax += 1; }
                else { var pad = (yMax - yMin) * 0.1; yMin -= pad; yMax += pad; }

                var xMin = xDom.min, xMax = xDom.max;
                function xScale(t) {
                    if (xMin === null || xMax === null || xMin === xMax) return PADL + INNERW / 2;
                    return PADL + (t - xMin) / (xMax - xMin) * INNERW;
                }
                function yScale(v) {
                    if (yMin === yMax) return PADT + INNERH / 2;
                    return PADT + INNERH - (v - yMin) / (yMax - yMin) * INNERH;
                }

                // Franja de rango normal. Si falta un extremo, se extiende hasta el borde del
                // gráfico de ese lado (rango abierto), sin romper el dibujo (FR-010).
                var band = null;
                if (nr && ((nr.Min !== null && nr.Min !== undefined) || (nr.Max !== null && nr.Max !== undefined))) {
                    var topV = (nr.Max !== null && nr.Max !== undefined) ? nr.Max : yMax;
                    var botV = (nr.Min !== null && nr.Min !== undefined) ? nr.Min : yMin;
                    var y1 = yScale(topV), y2 = yScale(botV);
                    band = { x: PADL, y: Math.min(y1, y2), w: INNERW, h: Math.max(1, Math.abs(y2 - y1)) };
                }

                var points = pts.map(function (p) {
                    var t = new Date(p.AuthoredAt).getTime();
                    return {
                        cx: xScale(t), cy: yScale(p.Value),
                        title: fmtDate(t) + ': ' + fmtValueForType(p.Value, s.Type) + (s.Unit ? ' ' + s.Unit : '')
                    };
                });
                var line = points.length > 1
                    ? points.map(function (pt, i) { return (i === 0 ? 'M' : 'L') + pt.cx.toFixed(1) + ' ' + pt.cy.toFixed(1); }).join(' ')
                    : null;

                var xTicks = [];
                if (xMin !== null) {
                    xTicks.push({ x: xScale(xMin), label: fmtDate(xMin), anchor: 'start' });
                    if (xMax !== xMin) xTicks.push({ x: xScale(xMax), label: fmtDate(xMax), anchor: 'end' });
                }
                var yMid = (yMin + yMax) / 2;
                var yTicks = [
                    { y: yScale(yMax), label: fmtValueForType(yMax, s.Type) },
                    { y: yScale(yMid), label: fmtValueForType(yMid, s.Type) },
                    { y: yScale(yMin), label: fmtValueForType(yMin, s.Type) }
                ];

                base.empty = false;
                base.band = band;
                base.line = line;
                base.points = points;
                base.xTicks = xTicks;
                base.yTicks = yTicks;
                base.axisX = PADT + INNERH;
                base.axisLeft = PADL;
                return base;
            }

            var charts = Vue.computed(function () {
                var xDom = commonXDomain();
                return series.value.map(function (s) { return buildChart(s, xDom); });
            });

            return {
                params: params, selected: selected, loadingParams: loadingParams,
                loadingSeries: loadingSeries, error: error, charts: charts,
                isSelected: isSelected, toggle: toggle,
                W: W, H: H
            };
        },
        template: '\
<div>\
<div v-if="error" class="alert alert-danger">{{ error }}</div>\
<div v-if="loadingParams" class="cq-card">\
    <div class="cq-loading"><div class="cq-spinner cq-spinner--lg"></div>\
        <div class="cq-loading__label">Cargando parámetros…</div>\
    </div>\
</div>\
<template v-else>\
    <div v-if="!params.length" class="cq-card cq-text-sm cq-text-muted">\
        Este cuestionario no tiene parámetros numéricos ni de fecha para graficar.\
    </div>\
    <template v-else>\
        <div class="cq-card mb-3">\
            <div class="cq-text-sm cq-text-muted mb-2">Seleccione uno o más parámetros para graficar su evolución.</div>\
            <div class="d-flex flex-wrap gap-3">\
                <label v-for="p in params" :key="p.LinkId" class="d-flex align-items-center gap-1" style="cursor:pointer;">\
                    <input type="checkbox" :checked="isSelected(p.LinkId)" @change="toggle(p.LinkId)" :aria-label="p.Text" />\
                    <span class="cq-text-sm">{{ p.Text }}<span v-if="p.Unit" class="cq-text-muted"> ({{ p.Unit }})</span></span>\
                </label>\
            </div>\
        </div>\
\
        <div v-if="loadingSeries" class="cq-card">\
            <div class="cq-loading"><div class="cq-spinner cq-spinner--lg"></div>\
                <div class="cq-loading__label">Cargando series…</div>\
            </div>\
        </div>\
        <div v-else-if="!charts.length" class="cq-card cq-text-sm cq-text-muted">\
            Marque un parámetro para ver su gráfico.\
        </div>\
        <div v-else class="d-flex flex-column gap-3">\
            <div v-for="c in charts" :key="c.linkId" class="cq-card">\
                <div class="d-flex justify-content-between align-items-baseline mb-1">\
                    <h3 class="cq-section-title" style="margin:0;">{{ c.text }}<span v-if="c.unit" class="cq-text-muted cq-text-sm"> ({{ c.unit }})</span></h3>\
                </div>\
                <div v-if="c.empty" class="cq-text-sm cq-text-muted py-3">\
                    Sin datos suficientes: el paciente no tiene valores cargados para este parámetro en fichas finalizadas.\
                </div>\
                <svg v-else :viewBox="\'0 0 \' + W + \' \' + H" style="width:100%; height:auto; overflow:visible;" role="img" :aria-label="\'Evolución de \' + c.text">\
                    <rect v-if="c.band" :x="c.band.x" :y="c.band.y" :width="c.band.w" :height="c.band.h"\
                          fill="var(--cq-success, #15803d)" opacity="0.12"></rect>\
                    <line v-for="(t, ti) in c.yTicks" :key="\'yg\'+ti" :x1="c.axisLeft" :x2="c.axisLeft + ' + INNERW + '"\
                          :y1="t.y" :y2="t.y" stroke="var(--cq-neutral-200, #e5e7eb)" stroke-width="1"></line>\
                    <text v-for="(t, ti) in c.yTicks" :key="\'yt\'+ti" :x="c.axisLeft - 6" :y="t.y + 3"\
                          text-anchor="end" font-size="10" fill="var(--cq-text-muted, #6b7280)">{{ t.label }}</text>\
                    <path v-if="c.line" :d="c.line" fill="none" stroke="var(--cq-primary, #2563eb)" stroke-width="2"></path>\
                    <circle v-for="(pt, pi) in c.points" :key="\'pt\'+pi" :cx="pt.cx" :cy="pt.cy" r="3.5"\
                            fill="var(--cq-primary, #2563eb)"><title>{{ pt.title }}</title></circle>\
                    <text v-for="(t, ti) in c.xTicks" :key="\'xt\'+ti" :x="t.x" :y="c.axisX + 16"\
                          :text-anchor="t.anchor" font-size="10" fill="var(--cq-text-muted, #6b7280)">{{ t.label }}</text>\
                </svg>\
            </div>\
        </div>\
    </template>\
</template>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('patient-trend', PatientTrend);
})();
