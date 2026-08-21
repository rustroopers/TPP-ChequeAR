/*
 * Isla Vue: report-stats (pantalla E5)
 * Estadisticas descriptivas de la cohorte filtrada (FR-008): completitud por pregunta,
 * resumen numerico con histograma y distribucion de las categoricas. Tambien permite
 * exportar ese resumen a Excel o Word, con opcion de elegir que preguntas incluir.
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
            autoQuery: { type: Object, default: null }
        },
        setup: function (props) {
            var stats = Vue.ref(null);
            var loading = Vue.ref(false);
            var error = Vue.ref(null);
            var lastQuery = Vue.ref(props.autoQuery);

            // Preguntas elegidas para exportar. Arranca con todas marcadas cada vez que
            // llegan estadisticas nuevas, para no dejar afuera algo sin que se note.
            var selected = Vue.reactive({});
            var exporting = Vue.ref(false);
            var exportError = Vue.ref(null);
            var exportFormat = Vue.ref('xlsx');

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
                    (d && d.Questions || []).forEach(function (q) { selected[q.LinkId] = true; });
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

            function exportStats(format) {
                if (!stats.value || !lastQuery.value) return;
                var linkIds = selectedLinkIds();
                if (!linkIds.length) { exportError.value = 'Seleccioná al menos una pregunta.'; return; }

                exporting.value = true; exportError.value = null;
                fetch('/api/reports/stats/export', {
                    method: 'POST', headers: authHeaders(),
                    body: JSON.stringify({ Query: lastQuery.value, LinkIds: linkIds, Format: format })
                }).then(function (r) {
                    if (r.ok) return r.blob();
                    return r.json().catch(function () { return null; }).then(function (b) {
                        throw new Error((b && b.Message) || ('HTTP ' + r.status));
                    });
                }).then(function (blob) {
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url;
                    a.download = 'estadisticas.' + format;
                    document.body.appendChild(a);
                    a.click();
                    document.body.removeChild(a);
                    URL.revokeObjectURL(url);
                }).catch(function (e) {
                    exportError.value = e.message;
                }).then(function () { exporting.value = false; });
            }

            return {
                stats: stats, loading: loading, error: error, lastQuery: lastQuery,
                load: load, num: num, binPct: binPct, rango: rango,
                completenessColor: completenessColor,
                selected: selected, selectedCount: selectedCount, selectAll: selectAll,
                exporting: exporting, exportError: exportError, exportStats: exportStats,
                exportFormat: exportFormat
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
            <div v-for="q in stats.Questions" :key="q.LinkId" class="col-lg-6">\
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
                        <div v-if="!q.N" class="cq-text-sm cq-text-muted">Sin valores numéricos cargados.</div>\
                        <template v-else>\
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
                    </template>\
\
                    <template v-else-if="q.Kind === \'categorical\'">\
                        <div v-if="!q.Categories || !q.Categories.length" class="cq-text-sm cq-text-muted">Sin respuestas cargadas.</div>\
                        <div v-else>\
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
                    </template>\
\
                    <div v-else class="cq-text-sm cq-text-muted">\
                        Las respuestas de texto y fecha no se resumen: sólo se informa cuántas fichas la tienen cargada.\
                    </div>\
                </div>\
            </div>\
        </div>\
        <div class="d-flex justify-content-end mt-3">\
            <div class="dropdown">\
                <button class="btn btn-primary dropdown-toggle" type="button" data-bs-toggle="dropdown" data-bs-auto-close="outside" aria-expanded="false" :disabled="exporting">\
                    <i class="bi bi-download me-1"></i> {{ exporting ? \'Exportando…\' : \'Exportar\' }}\
                </button>\
                <div class="dropdown-menu dropdown-menu-end p-3" style="min-width:320px;">\
                    <label class="form-label">Formato</label>\
                    <div class="mb-3">\
                        <div class="form-check form-check-inline">\
                            <input class="form-check-input" type="radio" id="statsFmtXlsx" value="xlsx" v-model="exportFormat" />\
                            <label class="form-check-label" for="statsFmtXlsx">Excel (.xlsx)</label>\
                        </div>\
                        <div class="form-check form-check-inline">\
                            <input class="form-check-input" type="radio" id="statsFmtDocx" value="docx" v-model="exportFormat" />\
                            <label class="form-check-label" for="statsFmtDocx">Word (.docx)</label>\
                        </div>\
                    </div>\
                    <div class="mb-2 d-flex justify-content-between align-items-center">\
                        <label class="form-label mb-0">Preguntas</label>\
                        <div class="d-flex gap-2">\
                            <button type="button" class="btn btn-sm btn-link p-0" @click="selectAll(true)">Todas</button>\
                            <button type="button" class="btn btn-sm btn-link p-0" @click="selectAll(false)">Ninguna</button>\
                        </div>\
                    </div>\
                    <div class="border rounded p-2 mb-2" style="max-height:200px; overflow-y:auto;">\
                        <div v-for="q in stats.Questions" :key="q.LinkId" class="form-check">\
                            <input class="form-check-input" type="checkbox" :id="\'statsQ_\' + q.LinkId" v-model="selected[q.LinkId]" />\
                            <label class="form-check-label cq-text-sm" :for="\'statsQ_\' + q.LinkId">{{ q.Text }}</label>\
                        </div>\
                    </div>\
                    <p v-if="exportError" class="cq-text-sm text-danger mb-2">{{ exportError }}</p>\
                    <button type="button" class="btn btn-primary w-100" :disabled="exporting || !selectedCount()" @click="exportStats(exportFormat)">\
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
