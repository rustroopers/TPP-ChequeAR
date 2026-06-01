# Quickstart: Motor de Renderizado Dinámico

## Prerrequisitos

- Setup del módulo 001 completo (API base levantada).
- Node.js 20+ y npm para la librería JS.
- Template UI del Hospital (carpeta `vendor/hospital-template/` o equivalente).

## Backend

Ya cubierto por el quickstart de módulo 001. Verificar que los endpoints nuevos respondan:

```powershell
curl https://localhost:5001/fhir/r5/QuestionnaireResponse `
  -H "Authorization: Bearer dev-medico"
```

## Librería JS de renderizado

```bash
cd frontend/chequear-renderer
npm install
npm run build       # produce dist/chequear-renderer.js
```

Integrar en el template:

```html
<script src="/static/chequear-renderer.js"></script>
<div id="ficha"></div>
<script>
  ChequeAR.render({
    container: document.getElementById('ficha'),
    questionnaireId: 'abc-123',
    patientId: 'PAC-001',
    apiBase: '/fhir/r5',
    token: window.__hospitalAuth.token,
    onSave: (qr) => console.log('saved', qr.id),
  });
</script>
```

## Smoke test E2E

```bash
cd tests/e2e
npx playwright install
npx playwright test renderer.spec.ts
```

El test:
1. Crea un Questionnaire vía API.
2. Lo publica.
3. Abre el template con el renderer integrado.
4. Completa 5 preguntas (incluida una condicional).
5. Finaliza y verifica `status=completed`.

## Troubleshooting

| Síntoma | Solución |
|---|---|
| Pregunta condicional siempre visible | Verificar que el cuestionario tenga `enableWhen` válido y que `EnableWhenEvaluator` se ejecute en el servidor |
| Adjuntos rechazados | Revisar config `Attachments:MaxBytes` y `Attachments:AllowedExtensions` |
| Autosave no dispara | Confirmar `autosaveIntervalMs` > 0 y que el navegador no esté en modo offline |
