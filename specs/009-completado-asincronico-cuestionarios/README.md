# Completado asincrónico de cuestionarios

## Contexto

Un cuestionario (ej. seguimiento de vasculitis en cardiología/reumatología) puede
empezar a completarse en el consultorio y seguir completándose semana a semana en
llamadas telefónicas de seguimiento, quedando `InProgress` durante todo ese lapso.
Antes de este cambio, `QuestionnaireResponse` sólo se podía crear y finalizar en un
único paso (`POST` + `$finalize` inmediato) — no existía ningún endpoint de guardado
parcial, y el propio código lo dejaba documentado como deuda pendiente.

Esto agrega:
1. **Guardado parcial incremental**: cada llamada de seguimiento guarda solo lo que
   se respondió en esa sesión, sin pisar lo respondido en llamadas anteriores.
2. **Anti-concurrencia**: dos médicos no pueden completar la misma ficha a la vez.

## Decisiones de diseño

Estas fueron discutidas y acordadas antes de implementar:

- **Concurrencia**: lock pesimista de sesión de edición (con expiración automática)
  **+** concurrencia optimista como red de seguridad al guardar. La optimista ya
  estaba probada en el proyecto para `Questionnaire` (patrón FHIR "Managing Resource
  Contention": `ETag`/`If-Match`, 409/428); se replicó el mismo mecanismo para
  `QuestionnaireResponse`.
- **Quién completa**: solo personal médico/administrativo (no hay self-service del
  paciente), así que no hizo falta ningún flujo de autenticación nuevo.
- **Estructura temporal**: el cuestionario no se divide en secciones por semana — es
  libre, se responde lo que se pueda en cada llamada.
- **TTL del lock**: 15 minutos de inactividad, renovado en cada guardado parcial.
  Aviso en el frontend al faltar 1 minuto para que el médico decida extender.
- **Qué pasa al vencer el TTL** ("grace period"): si nadie más tomó el lock
  mientras tanto, el guardado que llega tarde se acepta igual y renueva en
  silencio — el TTL protege contra que *otro usuario* pise el trabajo, no castiga
  al mismo médico por tardarse. Si otro usuario sí tomó el lock en el medio, el
  guardado tardío se rechaza con 409 explícito. Lo que ya se había guardado en
  llamadas/autosaves anteriores nunca se pierde, sea cual sea el caso.
- **Granularidad de auditoría**: se reutiliza `AuditEvent`/`AuditDispatcher` tal
  cual (ya logueaba creates/updates/finalize) para trackear cada sesión de
  guardado. No se agregó `AnsweredAt` por pregunta — se puede sumar después si
  hace falta saber qué pregunta puntual se respondió en qué llamada.

## Arquitectura

### Backend

| Pieza | Archivo | Qué hace |
|---|---|---|
| Migración | `src/ChequeAR.Infrastructure/Migrations/202608031619169_AddQuestionnaireResponseLocking.cs` | Agrega `ConcurrencyVersion` (nullable→backfill→NOT NULL, patrón Oracle ya usado en `AddOptimisticConcurrency`) y las 4 columnas de lock. Generada con el `MigrationScaffolder` real de EF6 (no a mano), para que el `Designer.cs`/`.resx` con el snapshot del modelo quede correcto. |
| Entidad | `src/ChequeAR.Domain/Entities/QuestionnaireResponse.cs` | `ConcurrencyVersion`, `LockToken`, `LockedByUserId`, `LockedAt`, `LockExpiresAt`. |
| Config EF | `src/ChequeAR.Infrastructure/EF/Configurations/QuestionnaireResponseConfig.cs` | `ConcurrencyVersion` como `IsConcurrencyToken()`; `LockedAt`/`LockExpiresAt` como `timestamp` (no `DATE`, que trunca a segundos en Oracle). |
| Excepción | `src/ChequeAR.Application/Exceptions/LockConflictException.cs` | Análoga a `ConcurrencyConflictException`, para el caso "lo tiene otro usuario". |
| Repositorio | `src/ChequeAR.Infrastructure/Repositories/QuestionnaireResponseRepository.cs` | Traduce `DbUpdateConcurrencyException` → `ConcurrencyConflictException`. `SaveProgressAsync` mergea items por `LinkId` operando contra el `DbSet` directamente (no la colección de navegación, por la FK requerida). |
| Servicio | `src/ChequeAR.Application/Services/QuestionnaireResponseService.cs` | `AcquireLockAsync`, `RenewLockAsync`, `ReleaseLockAsync`, `SaveProgressAsync` (con el grace period), `FinalizeAsync` actualizado para exigir y liberar el lock. |
| Controller | `src/ChequeAR.Api/Controllers/Api/QuestionnaireResponseController.cs` | Endpoints nuevos (ver tabla abajo). El `GET` oculta `LockToken` a quien no es su dueño (es un credential, no un dato de lectura pública). |

### Endpoints nuevos

Todos bajo `fhir/r5/QuestionnaireResponse`, mismos roles que `Create`/`$finalize`
(Administrador, Jefe, Médico):

| Método | Ruta | Uso |
|---|---|---|
| `POST` | `{id}/$lock` | Toma la sesión de edición. 409 si otro usuario la tiene vigente. |
| `POST` | `{id}/$renew-lock` | Heartbeat: extiende el vencimiento. Requiere header `X-Lock-Token`. |
| `POST` | `{id}/$unlock` | Libera el lock (idempotente). Requiere `X-Lock-Token`. |
| `PUT` | `{id}` | Guardado parcial. Requiere `If-Match` (versión) **y** `X-Lock-Token` (sesión). |
| `POST` | `{id}/$finalize` | Igual que antes, pero ahora también exige `X-Lock-Token` y libera el lock al terminar. |

### Frontend (`questionnaire-renderer`)

Al montar, busca una ficha `InProgress` existente para ese paciente+cuestionario
(`GET .../QuestionnaireResponse?subject=...&questionnaire=...&status=InProgress`),
la resume si existe, y toma el lock. Cada cambio dispara un autosave debounced
(~2.5s) vía `PUT`. Si el lock está tomado por otro, el formulario queda de solo
lectura con un banner. A 1 minuto de que venza el lock, aparece un aviso para
renovar. El lock se libera al finalizar o al salir de la página
(`beforeunload` con `fetch keepalive`, no `sendBeacon`, porque hace falta mandar
headers de autenticación).

## Testing hecho

- **21 tests unitarios nuevos** en `QuestionnaireResponseServiceTests.cs`: lock
  libre/tomado/vencido, grace period, merge por `LinkId`, conflicto de
  concurrencia, ciclo completo de `$finalize`. Los 124 tests del proyecto pasan.
- **Prueba manual end-to-end en el navegador**, contra Oracle real (no mocks):
  1. Se resumió un borrador ya existente (`PAC-000043`, "Vasculitis (Kawasaki/PAN)
     - Seguimiento") con sus 5 respuestas precargadas correctamente.
  2. Se editó un campo, se vio "Cambios sin guardar" y el autosave a los ~3s.
  3. **Se encontró y arregló un bug real**: `$lock` incrementa `ConcurrencyVersion`
     del lado del servidor (es una escritura protegida más), pero el frontend no
     se enteraba de ese incremento — el primer autosave después de tomar el lock
     chocaba siempre con un 409 falso ("esperaba la versión 2, en la base está la
     3"). Se agregó `concurrencyVersion` a las respuestas de `$lock`/`$renew-lock`
     y el frontend ahora la toma. Este bug no lo detectaban los tests unitarios
     porque no ejercitan el round-trip HTTP completo — solo apareció al probar en
     el navegador de verdad.
  4. Se recargó la página y el valor nuevo seguía ahí (persistido en Oracle).
  5. Se finalizó la ficha: `$finalize` exigió el lock, lo liberó al terminar, y
     redirigió a la vista de solo lectura con el dato final.
  6. La Auditoría mostró cada sesión de lock/guardado/finalización.
- Nota operativa: la migración no estaba aplicada en el Oracle de desarrollo (la
  app tiraba *"model backing the context has changed"* al arrancar). Se aplicó
  con `DbMigrator.Update()` — el mismo mecanismo que usa `Update-Database` en el
  Package Manager Console de Visual Studio.

## Testing pendiente

En orden de prioridad, ninguno bloqueante pero conviene cubrirlos antes de dar la
feature por completa:

1. **Conflicto real entre dos usuarios** (el banner "lo está completando otro
   usuario" solo está probado a nivel de servicio, nunca se vio en pantalla —
   necesita una segunda sesión logueada con otro usuario).
2. **Vencimiento real del TTL**, los dos casos (grace period vs. rechazo por
   colisión) — se probaron solo con mocks; bajando el TTL temporalmente se
   podría ver en minutos en vez de simularlo.
3. **Aviso de "1 minuto" y renovación** — nunca se disparó en pantalla.
4. **Cierre real de pestaña** liberando el lock (`beforeunload`/`keepalive`) —
   solo se revisó el código, no se confirmó el comportamiento real del navegador.
5. **Preguntas de opción múltiple (`openChoice`) y adjunto (`attachment`)** al
   resumir una ficha — el resume solo se probó con campos simples.
6. **Ficha nueva desde cero** (sin borrador previo, se crea recién en el primer
   autosave) — solo se probó el camino de "resumir una existente".
7. **Reportes** (`vw_FlatResponses` / QueryBuilder) — confirmar que las columnas
   nuevas no rompieron nada ahí.
8. **Carrera real** (dos `PUT` simultáneos, no secuenciales) para confirmar que
   el `ConcurrencyToken` de Oracle corta la escritura perdedora en una colisión
   genuina, no solo simulada con mocks.

## Archivos tocados

```
src/ChequeAR.Domain/Entities/QuestionnaireResponse.cs
src/ChequeAR.Infrastructure/EF/Configurations/QuestionnaireResponseConfig.cs
src/ChequeAR.Infrastructure/Repositories/QuestionnaireResponseRepository.cs
src/ChequeAR.Infrastructure/Migrations/202608031619169_AddQuestionnaireResponseLocking.{cs,Designer.cs,resx}
src/ChequeAR.Application/Interfaces/IQuestionnaireResponseRepository.cs
src/ChequeAR.Application/Services/QuestionnaireResponseService.cs
src/ChequeAR.Application/Exceptions/LockConflictException.cs
src/ChequeAR.Api/Controllers/Api/QuestionnaireResponseController.cs
src/ChequeAR.Api/Scripts/islands/questionnaire-renderer/index.js
src/ChequeAR.Api/Views/Encounters/Load.cshtml
src/ChequeAR.Tests/Application/QuestionnaireResponseServiceTests.cs
```
