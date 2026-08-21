/*
 * Isla Vue: report-stats (pantalla E5)
 * Estadisticas descriptivas de la cohorte filtrada (FR-008): completitud por pregunta,
 * resumen numerico con histograma y distribucion de las categoricas. Tambien permite
 * exportar ese resumen a Excel, con opcion de elegir que preguntas incluir.
 *
 * Los graficos son divs y SVG a mano, sin libreria. El resto de la app ya carga Vue y
 * Bootstrap desde un CDN y eso es un problema abierto para una intranet sin salida a
 * internet; sumar Chart.js agregaria una dependencia externa mas al mismo problema.
 * Un histograma y unas barras horizontales no justifican 200 KB de terceros.
 */
(function () {
    var ReportStats = {
        props: {
            // Query inicial (misma forma que /api/reports/query). La pantalla E5 la recibe
            // del reporte guardado; dentro del constructor llega por evento.
            autoQuery: { type: Object, default: null },
            // Nombre del reporte guardado, si vino de uno. Si no vino (reporte ad-hoc, nunca
            // guardado), se arma el nombre del archivo con el título del cuestionario.
            reportName: { type: String, default: '' }
        },
        setup: function (props) {
            var stats = Vue.ref(null);
            // Sólo entran a la grilla de Métricas las preguntas que de verdad tienen algo
            // para mostrar: texto/fecha nunca se resumen (Kind queda "other"), y una
            // numérica/categórica sin ninguna respuesta cargada tampoco tiene nada que
            // graficar. Antes esos dos casos mostraban una tarjeta con un mensaje de "sin
            // datos"; ahora directamente no se ofrecen. Siguen disponibles para el export a
            // Excel (ahí sí interesa saber cuántas fichas la tienen cargada).
            var chartableQuestions = Vue.computed(function () {
                return (stats.value && stats.value.Questions || []).filter(function (q) {
                    if (q.Kind === 'numeric') return !!q.N;
                    if (q.Kind === 'categorical') return !!(q.Categories && q.Categories.length);
                    return false;
                });
            });
            var loading = Vue.ref(false);
            var error = Vue.ref(null);
            var lastQuery = Vue.ref(props.autoQuery);

            // Preguntas elegidas para exportar. Arranca con todas marcadas cada vez que
            // llegan estadisticas nuevas, para no dejar afuera algo sin que se note.
            var selected = Vue.reactive({});
            var exporting = Vue.ref(false);
            var exportError = Vue.ref(null);

            function authHeaders() {
                var h = { 'Content-Type': 'application/json' };
                var m = document.querySelector('meta[name="cq-token"]');
                if (m) h['Authorization'] = 'Bearer ' + m.getAttribute('content');
                return h;
            }

            function load(query) {
                if (!query || !query.questionnaireId) return;
                lastQuery.value = query;
                loading.value = true; error.value = null;
                fetch('/api/reports/stats', {
                    method: 'POST', headers: authHeaders(), body: JSON.stringify(query)
                }).then(function (r) {
                    if (r.ok) return r.json();
                    return r.json().catch(function () { return null; }).then(function (b) {
                        throw new Error((b && b.Message) || ('HTTP ' + r.status));
                    });
                }).then(function (d) {
                    stats.value = d;
                    Object.keys(selected).forEach(function (k) { delete selected[k]; });
                    chartableQuestions.value.forEach(function (q) { selected[q.LinkId] = true; });
                }).catch(function (e) {
                    error.value = e.message;
                    stats.value = null;
                }).then(function () { loading.value = false; });
            }

            // El constructor de consultas (otra isla) avisa que hay una cohorte nueva. Es el
            // unico acoplamiento entre las dos, y va en un sentido solo.
            function onQuery(e) { load(e.detail); }
            Vue.onMounted(function () {
                window.addEventListener('cq:report-query', onQuery);
                if (props.autoQuery) load(props.autoQuery);
            });
            Vue.onUnmounted(function () { window.removeEventListener('cq:report-query', onQuery); });

            // --- helpers de presentacion ---------------------------------------------
            function num(v) {
                if (v === null || v === undefined) return '—';
                return String(v);
            }
            // Altura relativa de cada barra del histograma.
            function binPct(bin, question) {
                var max = 0;
                (question.Histogram || []).forEach(function (b) { if (b.Count > max) max = b.Count; });
                return max > 0 ? Math.round(bin.Count * 100 / max) : 0;
            }
            function rango(bin) {
                return bin.From === bin.To ? num(bin.From) : num(bin.From) + '–' + num(bin.To);
            }
            // Verde / ambar / rojo segun que tan cargada esta la variable.
            function completenessColor(pct) {
                if (pct >= 90) return 'var(--cq-success, #15803d)';
                if (pct >= 60) return 'var(--cq-warning)';
                return 'var(--cq-danger)';
            }

            // Tipo de grafico elegido por pregunta categorica: 'bar' (barras, el original)
            // o 'pie' (torta). Por pregunta y no global, porque una pantalla con varias
            // categoricas puede tener una con pocas categorias (torta prolija) y otra con
            // muchas (donde la barra lee mejor).
            var chartType = Vue.reactive({});
            function setChartType(linkId, type) { chartType[linkId] = type; }

            var PIE_COLORS = ['#2563eb', '#dc2626', '#16a34a', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#65a30d'];

            // Slices de la torta en SVG: arcos como sectores circulares (M centro, L borde,
            // A arco, Z). Angulo acumulado a partir de las mismas Categories que ya trae la
            // pregunta -no se recalcula nada, solo se convierte % a grados-.
            function pieSlices(q) {
                var cats = q.Categories || [];
                var total = cats.reduce(function (s, c) { return s + c.Pct; }, 0) || 100;
                var acc = 0;
                return cats.map(function (c, i) {
                    var start = acc / total * 360;
                    acc += c.Pct;
                    var end = acc / total * 360;
                    var color = PIE_COLORS[i % PIE_COLORS.length];
                    return { path: arcPath(start, end), color: color, label: c.Label, count: c.Count, pct: c.Pct };
                });
            }
            function arcPath(startDeg, endDeg) {
                var full = (endDeg - startDeg) >= 359.999;
                if (full) endDeg = startDeg + 359.999; // un solo valor con 100%: arco casi completo, no un punto.
                var cx = 50, cy = 50, r = 48;
                var p1 = polar(cx, cy, r, startDeg), p2 = polar(cx, cy, r, endDeg);
                var largeArc = (endDeg - startDeg) > 180 ? 1 : 0;
                return 'M ' + cx + ' ' + cy + ' L ' + p1.x + ' ' + p1.y +
                    ' A ' + r + ' ' + r + ' 0 ' + largeArc + ' 1 ' + p2.x + ' ' + p2.y + ' Z';
            }
            function polar(cx, cy, r, deg) {
                var rad = (deg - 90) * Math.PI / 180;
                return { x: cx + r * Math.cos(rad), y: cy + r * Math.sin(rad) };
            }


            // --- seleccion y exportacion -----------------------------------------------
            function selectedCount() {
                return Object.keys(selected).filter(function (k) { return selected[k]; }).length;
            }
            function selectAll(value) {
                Object.keys(selected).forEach(function (k) { selected[k] = value; });
            }
            function selectedLinkIds() {
                return Object.keys(selected).filter(function (k) { return selected[k]; });
            }

            // Nombre + " - metricas.xlsx", sin caracteres invalidos para nombre de archivo.
            function statsFilename() {
                var base = (props.reportName || (stats.value && stats.value.QuestionnaireTitle) || 'estadisticas').trim();
                var safe = base.replace(/[\\/:*?"<>|]/g, '').trim() || 'estadisticas';
                return safe + ' - metricas.xlsx';
            }

            function exportStats() {
                if (!stats.value || !lastQuery.value) return;
                var linkIds = selectedLinkIds();
                if (!linkIds.length) { exportError.value = 'Seleccioná al menos una pregunta.'; return; }

                // El Excel tiene que coincidir con lo que la persona ya eligio en pantalla
                // (barra/torta por pregunta), no con un tipo fijo elegido de antemano.
                var chartTypes = {};
                Object.keys(chartType).forEach(function (linkId) { chartTypes[linkId] = chartType[linkId]; });

                exporting.value = true; exportError.value = null;
                fetch('/api/reports/stats/export', {
                    method: 'POST', headers: authHeaders(),
                    body: JSON.stringify({ Query: lastQuery.value, LinkIds: linkIds, ChartTypes: chartTypes })
                }).then(function (r) {
                    if (r.ok) return r.blob();
                    return r.json().catch(function () { return null; }).then(function (b) {
                        throw new Error((b && b.Message) || ('HTTP ' + r.status));
                    });
                }).then(function (blob) {
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = statsFilename();
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                }).catch(function (e) {
                    exportError.value = e.message;
                }).then(function () { exporting.value = false; });
            }

            return {
                stats: stats, chartableQuestions: chartableQuestions, loading: loading, error: error, lastQuery: lastQuery,
                load: load, num: num, binPct: binPct, rango: rango,
                completenessColor: completenessColor,
                selected: selected, selectedCount: selectedCount, selectAll: selectAll,
                exporting: exporting, exportError: exportError, exportStats: exportStats,
                chartType: chartType, setChartType: setChartType, pieSlices: pieSlices
            };
        },
        template: '\
<div>\
<div v-if="loading" class="cq-card">\
    <div class="cq-loading"><div class="cq-spinner cq-spinner--lg"></div>\
        <div class="cq-loading__label">Calculando estadísticas…</div>\
    </div>\
</div>\
<div v-else-if="error" class="alert alert-danger">{{ error }}</div>\
<div v-else-if="!stats" class="cq-card cq-text-sm cq-text-muted">\
    Arme el reporte y pida las estadísticas para verlas aquí.\
</div>\
<div v-else>\
    <template v-if="stats.Responses">\
        <h2 class="cq-section-title border-top pt-3">Métricas</h2>\
        <div class="row g-3">\
            <div v-for="q in chartableQuestions" :key="q.LinkId" class="col-lg-6">\
                <div class="cq-card h-100">\
                    <div class="d-flex justify-content-between align-items-start gap-2">\
                        <h3 class="cq-section-title" style="margin-top:0;">{{ q.Text }}</h3>\
                        <span class="cq-badge cq-badge--info cq-text-xs">{{ q.Type }}</span>\
                    </div>\
\
                    <div class="mb-3">\
                        <div class="d-flex justify-content-between cq-text-xs cq-text-muted">\
                            <span>Completitud</span>\
                            <span>{{ q.Answered }} / {{ stats.Responses }} · {{ q.CompletenessPct }}%</span>\
                        </div>\
                        <div class="progress mt-1" style="height:6px;">\
                            <div class="progress-bar" role="progressbar"\
                                 :style="{ width: q.CompletenessPct + \'%\', background: completenessColor(q.CompletenessPct) }"\
                                 :aria-valuenow="q.CompletenessPct" aria-valuemin="0" aria-valuemax="100"></div>\
                        </div>\
                        <div v-if="q.Missing > 0" class="cq-text-xs cq-text-muted mt-1">\
                            {{ q.Missing }} sin responder\
                        </div>\
                    </div>\
\
                    <template v-if="q.Kind === \'numeric\'">\
                        <table class="table table-sm cq-text-sm mb-2">\
                            <tbody>\
                                <tr><th class="cq-text-muted fw-normal">n</th><td>{{ q.N }}</td>\
                                    <th class="cq-text-muted fw-normal">Media</th><td>{{ num(q.Mean) }}</td></tr>\
                                <tr><th class="cq-text-muted fw-normal">Mediana</th><td>{{ num(q.Median) }}</td>\
                                    <th class="cq-text-muted fw-normal">Desvío</th><td>{{ num(q.StdDev) }}</td></tr>\
                                <tr><th class="cq-text-muted fw-normal">Mín–Máx</th><td>{{ num(q.Min) }}–{{ num(q.Max) }}</td>\
                                    <th class="cq-text-muted fw-normal">P25–P75</th><td>{{ num(q.P25) }}–{{ num(q.P75) }}</td></tr>\
                            </tbody>\
                        </table>\
                        <div v-if="q.Histogram && q.Histogram.length" class="d-flex align-items-end gap-1" style="height:110px;">\
                            <div v-for="(b, bi) in q.Histogram" :key="bi" class="flex-fill d-flex flex-column align-items-center justify-content-end h-100"\
                                 :title="rango(b) + \': \' + b.Count">\
                                <span class="cq-text-xs cq-text-muted">{{ b.Count }}</span>\
                                <div style="width:100%; border-radius:3px 3px 0 0; background:var(--cq-primary); min-height:2px;"\
                                     :style="{ height: binPct(b, q) + \'%\' }"></div>\
                                <span class="cq-text-xs cq-text-muted text-center" style="font-size:.65rem; word-break:break-all;">{{ rango(b) }}</span>\
                            </div>\
                        </div>\
                    </template>\
\
                    <template v-else-if="q.Kind === \'categorical\'">\
                        <div class="btn-group btn-group-sm mb-2" role="group" aria-label="Tipo de gráfico">\
                            <button type="button" class="btn btn-outline-secondary" :class="{ active: (chartType[q.LinkId] || \'bar\') === \'bar\' }" @click="setChartType(q.LinkId, \'bar\')">\
                                <i class="bi bi-bar-chart-line"></i> Barras\
                            </button>\
                            <button type="button" class="btn btn-outline-secondary" :class="{ active: chartType[q.LinkId] === \'pie\' }" @click="setChartType(q.LinkId, \'pie\')">\
                                <i class="bi bi-pie-chart"></i> Torta\
                            </button>\
                        </div>\
                        <div v-if="(chartType[q.LinkId] || \'bar\') === \'bar\'">\
                            <div v-for="c in q.Categories" :key="c.Label" class="mb-2">\
                                <div class="d-flex justify-content-between cq-text-sm">\
                                    <span>{{ c.Label }}</span>\
                                    <span class="cq-text-muted">{{ c.Count }} · {{ c.Pct }}%</span>\
                                </div>\
                                <div class="progress mt-1" style="height:8px;">\
                                    <div class="progress-bar" role="progressbar" :style="{ width: c.Pct + \'%\', background: \'var(--cq-primary)\' }"\
                                         :aria-valuenow="c.Pct" aria-valuemin="0" aria-valuemax="100"></div>\
                                </div>\
                            </div>\
                        </div>\
                        <div v-else class="d-flex align-items-center gap-3 flex-wrap">\
                            <svg viewBox="0 0 100 100" width="130" height="130" style="flex-shrink:0;">\
                                <path v-for="s in pieSlices(q)" :key="s.label" :d="s.path" :fill="s.color" :title="s.label + \': \' + s.count"></path>\
                            </svg>\
                            <div class="cq-text-sm">\
                                <div v-for="s in pieSlices(q)" :key="s.label" class="d-flex align-items-center gap-2 mb-1">\
                                    <span style="width:10px; height:10px; border-radius:2px; display:inline-block;" :style="{ background: s.color }"></span>\
                                    <span>{{ s.label }}</span>\
                                    <span class="cq-text-muted">{{ s.count }} · {{ s.pct }}%</span>\
                                </div>\
                            </div>\
                        </div>\
                    </template>\
                </div>\
            </div>\
        </div>\
        <div class="d-flex justify-content-end mt-3">\
            <div class="dropdown">\
                <button class="btn btn-primary dropdown-toggle" type="button" data-bs-toggle="dropdown" data-bs-auto-close="outside" aria-expanded="false" :disabled="exporting">\
                    <i class="bi bi-download me-1"></i> {{ exporting ? \'Exportando…\' : \'Exportar (Excel)\' }}\
                </button>\
                <div class="dropdown-menu dropdown-menu-end p-3" style="min-width:320px;">\
                    <div class="mb-2 d-flex justify-content-between align-items-center">\
                        <label class="form-label mb-0">Preguntas a exportar</label>\
                        <div class="d-flex gap-2">\
                            <button type="button" class="btn btn-sm btn-link p-0" @click="selectAll(true)">Todas</button>\
                            <button type="button" class="btn btn-sm btn-link p-0" @click="selectAll(false)">Ninguna</button>\
                        </div>\
                    </div>\
                    <div class="border rounded p-2 mb-2" style="max-height:200px; overflow-y:auto;">\
                        <div v-for="q in chartableQuestions" :key="q.LinkId" class="form-check">\
                            <input class="form-check-input" type="checkbox" :id="\'statsQ_\' + q.LinkId" v-model="selected[q.LinkId]" />\
                            <label class="form-check-label cq-text-sm" :for="\'statsQ_\' + q.LinkId">{{ q.Text }}</label>\
                        </div>\
                    </div>\
                    <p v-if="exportError" class="cq-text-sm text-danger mb-2">{{ exportError }}</p>\
                    <button type="button" class="btn btn-primary w-100" :disabled="exporting || !selectedCount()" @click="exportStats()">\
                        {{ exporting ? \'Exportando…\' : \'Descargar (\' + selectedCount() + \')\' }}\
                    </button>\
                </div>\
            </div>\
        </div>\
    </template>\
</div>\
</div>'
    };

    if (window.ChequeARIslands) window.ChequeARIslands.register('report-stats', ReportStats);
})();
