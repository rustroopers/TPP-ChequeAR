# Tasks: Internacionalización

- [ ] T001 [F0] Crear `App_GlobalResources/Resources.es-AR.resx` y `Resources.en.resx` vacíos.
- [ ] T002 [F0] Crear `Scripts/i18n/es-AR.json` y `en.json` vacíos.
- [ ] T003 [F0] Implementar `Helpers/CqLocalizer.cs` con método `Cq.T(key, params)` + fallback.
- [ ] T004 [F0] Implementar `CultureMiddleware` en `Startup.cs` (cookie → Accept-Language → default).
- [ ] T005 [F0] [P] Setear cookie `chequear_lang` en `LanguageSwitcher` controller action.
- [ ] T006 [F1] Implementar `_LanguageSwitcher.cshtml` en navbar.
- [ ] T007 [F1] Persistencia de preferencia en perfil de usuario (campo `preferred_language` en `User`).
- [ ] T008 [F2] Extraer todas las claves de las 18 pantallas P1 y poblar `es-AR.resx` + `.json`.
- [ ] T009 [F2] [P] Mensajes de validación server-side con códigos i18n.
- [ ] T010 [F3] Traducir 100% de las claves a `en`.
- [ ] T011 [F4] Crear tabla `LocalizedTextEntry` + migración EF.
- [ ] T012 [F4] Implementar `LocalizedTextResolver` con caché por request.
- [ ] T013 [F4] Adaptar `QuestionnaireService` para resolver textos en cada lectura.
- [ ] T014 [F4] UI de edición de traducciones en el editor Q3 (sub-pestañas por idioma).
- [ ] T015 [F5] Script `scripts/check-i18n.ps1` que valida claves iguales entre `.resx` y `.json`.
- [ ] T016 [F5] Test Playwright: cambiar idioma y validar 3 labels en T2 + R3 + Q1.
- [ ] T017 [F5] [P] Auditoría de claves faltantes con grep sobre `.cshtml` y `.js`.
