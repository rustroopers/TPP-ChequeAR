/*
 * Visibilidad (enableWhen) de las preguntas del renderer. Está aparte para que los tests del
 * servidor la ejecuten con los mismos casos que EnableWhenEvaluator.
 */
(function (root) {
    // En es-AR el separador decimal es la coma.
    function parseNumeric(value) {
        if (value === undefined || value === null) return NaN;
        var raw = String(value).trim().replace(',', '.');
        if (raw === '') return NaN;
        return Number(raw);
    }

    function isEmpty(v) {
        return v === undefined || v === null || v === '' || (Array.isArray(v) && v.length === 0);
    }

    // Fechas: se comparan como texto, sin segundos ni zona.
    function normalize(type, v) {
        if (typeof v !== 'string') return v;
        if (type === 'date') return v.slice(0, 10);
        if (type === 'dateTime') return v.slice(0, 16);
        return v;
    }

    function same(actual, expected) {
        if (expected === undefined || expected === null) return false;
        if (typeof expected === 'number') {
            var n = parseNumeric(actual);
            return !isNaN(n) && n === expected;
        }
        return actual === expected;
    }

    function compareNumeric(actual, expected, greater) {
        if (expected === undefined || expected === null || expected === '') return false;
        var n = parseNumeric(actual), e = Number(expected);
        if (isNaN(n) || isNaN(e)) return false;
        return greater ? n > e : n < e;
    }

    // FHIR R5: '=' y los de orden se cumplen si alguna respuesta cumple; '!=' si ninguna es igual.
    // Sin respuesta, '!=' se cumple y el resto no.
    function satisfies(op, values, expected) {
        switch (op) {
            case '=': return values.some(function (v) { return same(v, expected); });
            case '!=': return !values.some(function (v) { return same(v, expected); });
            case '>': return values.some(function (v) { return compareNumeric(v, expected, true); });
            case '<': return values.some(function (v) { return compareNumeric(v, expected, false); });
            default: return true;
        }
    }

    function valuesOf(type, raw) {
        var list = Array.isArray(raw) ? raw : [raw];
        return list.filter(function (v) { return !isEmpty(v); })
                   .map(function (v) { return normalize(type, v); });
    }

    // Una pregunta cuya origen está oculta se evalúa como si la origen no tuviera respuesta,
    // igual que en el servidor: el valor que quedó en memoria no cuenta.
    function isVisible(q, byLinkId, answers, visiting) {
        var cond = q && q.enableWhen;
        if (!cond) return true;
        visiting = visiting || {};
        if (visiting[q.linkId]) return true;
        visiting[q.linkId] = true;

        var source = byLinkId[cond.question];
        var sourceVisible = !source || isVisible(source, byLinkId, answers, visiting);
        delete visiting[q.linkId];

        var type = source ? source.type : null;
        var values = sourceVisible ? valuesOf(type, answers[cond.question]) : [];
        return satisfies(cond.op, values, normalize(type, cond.value));
    }

    root.ChequeAREnableWhen = { isVisible: isVisible, isEmpty: isEmpty, parseNumeric: parseNumeric };
})(typeof window !== 'undefined' ? window : this);
