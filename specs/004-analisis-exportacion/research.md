# Research: Módulo de Análisis y Exportación

## Decisiones

### D1. Librería Excel

**Decisión**: ClosedXML (LGPL, gratuita) en lugar de EPPlus 5+ (que requiere licencia comercial).

**Justificación**: El proyecto se entrega al hospital y debe poder mantenerse sin licencias adicionales. ClosedXML cubre `.xlsx` con estilos básicos suficientes para el MVP.

**Alternativas descartadas**:
- *EPPlus 5+*: requiere licencia comercial.
- *OpenXML SDK puro*: API verbosa, mayor superficie de bugs.

### D2. Construcción de queries dinámicas

**Decisión**: Builder propio que genera SQL parametrizado contra la vista `vw_FlatResponses`, usando pivoteo por `LinkId` cuando se solicitan múltiples columnas.

**Justificación**: LINQ con queries pivot dinámicas se vuelve frágil y lento; SQL parametrizado da control sobre el plan de ejecución y previene inyección.

**Mitigación de SQL injection**: nunca se concatenan valores; sólo nombres de columnas, validados contra whitelist de `LinkId` por cuestionario.

### D3. Background jobs

**Decisión**: Hangfire self-hosted dentro del AppPool de IIS.

**Justificación**: Compatible con .NET Framework 4.7.1; dashboard útil para operación; persistencia en SQL Server (mismo motor). Alternativa "servicio Windows separado" agrega complejidad de deploy en este alcance.

### D4. Formato CSV para Excel en es-AR

**Decisión**: UTF-8 con BOM, separador `;`, decimales con `,`.

**Justificación**: Excel en locale es-AR usa `;` como separador y `,` como decimal por defecto. Sin BOM no detecta UTF-8 y muestra caracteres rotos.

### D5. Estadísticas

**Decisión**: Calcular en SQL (`AVG`, `STDEV`, `PERCENTILE_CONT(0.5)`) y devolver en el response de `/query`.

**Justificación**: Mover el cálculo al motor SQL es más rápido y consistente. Mediana via `PERCENTILE_CONT` está disponible desde SQL Server 2012.

### D6. Determinismo y reproducibilidad de exports

**Decisión**: Las filas se ordenan por `(QuestionnaireId, AuthoredAt, ResponseId)` y se incluyen metadata en una hoja "Acerca de" del XLSX (consulta, fecha, ejecutor, hash de la consulta).

**Justificación**: Garantiza que el resultado sea citable en publicaciones académicas. Tests de snapshot validan que misma consulta → mismo archivo.

## Referencias

- ClosedXML — https://github.com/ClosedXML/ClosedXML
- Hangfire — https://www.hangfire.io/
- Microsoft SQL Server `PERCENTILE_CONT` — docs.microsoft.com
- CSV RFC 4180 + extensiones de Excel

## Riesgos abiertos

| Riesgo | Mitigación |
|---|---|
| Consultas que escanean tabla completa | Índices sobre `(LinkId, ValueDecimal)` y `(LinkId, ValueCode)`; vista materializada |
| Resultado gigante satura disco | Cuota de espacio por artifact + retención 7 días |
| Cambios de versión de cuestionario fragmentan datos | Mostrar warning en UI; permitir exportar combinando versiones con columna `qVersion` |
