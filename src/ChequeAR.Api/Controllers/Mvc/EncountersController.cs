using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Constants;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChequeAR.Api.Controllers.Mvc
{
    [Authorize]
    public class EncountersController : Controller
    {
        private readonly PatientService _patients;
        private readonly QuestionnaireResponseService _responses;
        private readonly QuestionnaireService _questionnaires;
        private readonly AuditQueryService _audit;
        private readonly SpecialtyCatalogService _specialties;
        private readonly AccessAuditService _accessAudit;

        public EncountersController(PatientService patients, QuestionnaireResponseService responses, QuestionnaireService questionnaires, AuditQueryService audit, SpecialtyCatalogService specialties, AccessAuditService accessAudit)
        {
            _accessAudit = accessAudit;
            _patients = patients;
            _responses = responses;
            _questionnaires = questionnaires;
            _audit = audit;
            _specialties = specialties;
        }

        // R1 — pantalla de pacientes (buscador). El action se llama Index para que
        // la URL canónica sea /Pacientes (via ruta nombrada) o /Encounters como
        // fallback, en vez del ex "/Encounters/Search" que exponía el verbo interno.
        public async Task<ActionResult> Index(string q = null)
        {
            ViewBag.Title = "Pacientes";
            ViewBag.PageId = "encounters";

            var model = new PatientSearchViewModel { Query = q, HasSearched = !string.IsNullOrWhiteSpace(q) };

            // Sólo se pueden cargar fichas de cuestionarios PUBLICADOS: un borrador no está
            // habilitado para uso clínico y uno archivado ya no se usa.
            try
            {
                var cat = await _specialties.GetAsync();
                var published = await _questionnaires.SearchAsync(QuestionnaireStatus.Active, null, null, 0, 200);
                foreach (var pq in published.OrderBy(x => x.Title))
                {
                    model.Questionnaires.Add(new SelectableQuestionnaire
                    {
                        Id = pq.Id,
                        Title = pq.Title,
                        Version = pq.Versions?.Count ?? 0,
                        SpecialtyCode = cat.CodeOrDefault(pq.SpecialtyCode),
                        SpecialtyLabel = cat.Label(pq.SpecialtyCode, pq.SpecialtyDisplay),
                        SpecialtyColor = cat.Color(pq.SpecialtyCode)
                    });
                }
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }

            if (!model.HasSearched) return View(model);

            try
            {
                var cat = await _specialties.GetAsync();
                var hits = await _patients.SearchAsync(q);
                foreach (var h in hits)
                {
                    model.Results.Add(new PatientSearchItem
                    {
                        HC = h.HospitalId, FullName = h.FullName,
                        Document = h.Document,
                        AgeLabel = h.AgeLabel,
                        BirthDateLabel = h.BirthDateLabel,
                        SpecialtyCode = cat.CodeOrDefault(h.SpecialtyCode),
                        SpecialtyLabel = cat.Label(h.SpecialtyCode, h.SpecialtyLabel)
                    });
                }
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                model.Results.Clear();
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View(model);
        }

        // R3 — Render (host Vue)
        public async Task<ActionResult> Load(string patientId, Guid questionnaireId)
        {
            // Sin esto, el navegador restaura esta página desde el bfcache al tocar
            // "atrás" después de finalizar: el formulario queda editable con las
            // respuestas viejas todavía en memoria, y un segundo "Finalizar" crea una
            // QuestionnaireResponse nueva en vez de avisar que ya no hay nada que cargar.
            // no-store fuerza una recarga real contra el server en su lugar.
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();

            ViewBag.Title = "Carga de ficha";
            ViewBag.PageId = "encounters";
            ViewBag.PatientId = patientId;
            ViewBag.QuestionnaireId = questionnaireId;

            try
            {
                var p = await _patients.GetAsync(patientId);
                ViewBag.PatientName = p != null ? (p.FullName + " (HC " + p.HospitalId + ")") : ("HC " + patientId);

                ChequeAR.Domain.Entities.Questionnaire q;
                try
                {
                    q = await _questionnaires.GetAsync(questionnaireId);
                }
                catch (SpecialtyForbiddenException)
                {
                    return HttpNotFound();
                }
                if (q == null) return HttpNotFound();
                ViewBag.QuestionnaireTitle = q.Title;

                var current = q.CurrentVersion();
                if (current == null)
                {
                    ViewBag.Version = 0;
                    ViewBag.LoadError = "El cuestionario no tiene una versión publicada. No se puede cargar una ficha.";
                    return View();
                }

                // Si ya hay una ficha InProgress para este paciente+cuestionario, se retoma en
                // LA VERSIÓN CON LA QUE SE INICIÓ (la congelada), no en la vigente: cambiar las
                // preguntas a mitad de completado reinterpretaría respuestas ya dadas, y como
                // el guardado y la finalización siguen ancladas a esa versión, el formulario
                // quedaría fuera de sincronía con lo que efectivamente se persiste/valida. La
                // versión nueva rige para fichas nuevas. Sin borrador previo, se usa la vigente.
                var draft = await GetResumeDraftAsync(patientId, questionnaireId);
                // Archivado: no admite fichas nuevas, pero un borrador ya empezado se termina.
                if (draft == null && q.Status == QuestionnaireStatus.Retired)
                {
                    ViewBag.Version = current.VersionNumber;
                    ViewBag.LoadError = "El cuestionario está archivado: no admite fichas nuevas.";
                    return View();
                }
                var version = (draft == null ? null : q.FindVersion(draft.QuestionnaireVersionId)) ?? current;
                // El botón "Detalle" (historial de la ficha) sólo tiene sentido si ya hay una
                // ficha con eventos: una carga nueva todavía no generó ninguno.
                ViewBag.ExistingResponseId = draft?.Id;

                ViewBag.Version = version.VersionNumber;
                var renderable = FhirQuestionnaireReader.Read(version.FhirJson);
                if (renderable == null)
                {
                    ViewBag.LoadError = "La versión publicada no se pudo interpretar.";
                    return View();
                }

                ViewBag.RendererProps = JsonConvert.SerializeObject(
                    new
                    {
                        patientId,
                        questionnaireId,
                        questionnaireVersionId = version.Id,
                        version = version.VersionNumber,
                        // Si esta ficha se está completando en una versión anterior a la
                        // vigente, el número de la vigente; 0 si ya está en la última. Habilita
                        // en el renderer la opción de pasar a la versión nueva.
                        newerVersion = version.VersionNumber < current.VersionNumber ? current.VersionNumber : 0,
                        title = q.Title,
                        groups = renderable.Groups
                    },
                    new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                        NullValueHandling = NullValueHandling.Ignore
                    });
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.PatientName = "HC " + patientId;
                ViewBag.QuestionnaireTitle = null;
                ViewBag.Version = 0;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View();
        }


        // Corrección de una ficha finalizada: mismo formulario que la carga, con la versión
        // con la que se completó.
        public async Task<ActionResult> Amend(Guid id)
        {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();

            ViewBag.Title = "Corrección de ficha";
            ViewBag.PageId = "encounters";
            ViewBag.ExistingResponseId = id;
            try
            {
                var qr = await _responses.GetAsync(id);
                if (qr == null) return HttpNotFound();
                if (qr.Status != ResponseStatus.Completed && qr.Status != ResponseStatus.Amended)
                    return RedirectToAction("View", new { id });

                var q = await _questionnaires.GetAsync(qr.QuestionnaireId);
                var p = await _patients.GetAsync(qr.PatientHospitalId);
                ViewBag.PatientName = p != null ? (p.FullName + " (HC " + p.HospitalId + ")") : ("HC " + qr.PatientHospitalId);
                ViewBag.QuestionnaireTitle = q?.Title ?? "Cuestionario";

                var version = q?.FindVersion(qr.QuestionnaireVersionId);
                var renderable = FhirQuestionnaireReader.Read(version?.FhirJson);
                ViewBag.Version = version?.VersionNumber ?? 0;
                if (renderable == null)
                {
                    ViewBag.LoadError = "No se encontró la versión con la que se completó la ficha.";
                    return View("Load");
                }

                ViewBag.RendererProps = JsonConvert.SerializeObject(
                    new
                    {
                        patientId = qr.PatientHospitalId,
                        questionnaireId = qr.QuestionnaireId,
                        questionnaireVersionId = version.Id,
                        version = version.VersionNumber,
                        amendResponseId = id,
                        title = q.Title,
                        groups = renderable.Groups
                    },
                    new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                        NullValueHandling = NullValueHandling.Ignore
                    });
            }
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.Version = 0;
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
            }
            return View("Load");
        }

        /// <summary>
        /// La ficha InProgress más reciente para este paciente+cuestionario, o null si no hay
        /// (ficha nueva) o la base no responde. Mismo criterio que usa el renderer, así ambos
        /// resuelven la misma ficha. Se busca una sola vez y se reutiliza para elegir la versión
        /// de reanudación y para linkear el historial de esa ficha.
        /// </summary>
        private async Task<ChequeAR.Domain.Entities.QuestionnaireResponse> GetResumeDraftAsync(string patientId, Guid questionnaireId)
        {
            try
            {
                var drafts = await _responses.SearchAsync(patientId, questionnaireId, ResponseStatus.InProgress, 0, 1);
                return drafts.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                return null;   // sin base no se puede resumir: el llamador cae a la versión vigente
            }
        }



        // R6 — Read-only
        public async Task<ActionResult> View(Guid id)
        {
            ViewBag.Title = "Ficha";
            ViewBag.PageId = "encounters";
            try
            {
                var qr = await _responses.GetAsync(id);
                if (qr == null) return HttpNotFound();
                await _accessAudit.ResponseRead(qr);
                var q = await _questionnaires.GetAsync(qr.QuestionnaireId);
                var p = await _patients.GetAsync(qr.PatientHospitalId);
                var model = new EncounterReadonlyViewModel
                {
                    EncounterId = qr.Id,
                    Patient = new PatientSearchItem { HC = qr.PatientHospitalId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? SpecialtyDefaults.Code, SpecialtyLabel = p?.SpecialtyLabel ?? SpecialtyDefaults.Name },
                    QuestionnaireTitle = q?.Title,
                    // La versión con la que se completó ESTA ficha, no la que esté vigente
                    // ahora: si se publicó otra después, la ficha sigue siendo de la suya.
                    QuestionnaireVersion = q?.FindVersion(qr.QuestionnaireVersionId)?.VersionNumber ?? 0,
                    Author = qr.AuthorUserId,
                    FinalizedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(qr.CompletedAt ?? qr.LastSavedAt),
                    IsDiscarded = qr.Status == ResponseStatus.EnteredInError,
                    IsAmended = qr.Status == ResponseStatus.Amended
                };

                // Un linkId puede repetirse (opción múltiple: un item por opción marcada),
                // así que se agrupan y se muestran todas, no sólo la primera.
                var itemsByLink = (qr.Items ?? new List<ChequeAR.Domain.Entities.QuestionnaireResponseItem>())
                    .GroupBy(i => i.LinkId).ToDictionary(gr => gr.Key, gr => gr.ToList());

                // Las PREGUNTAS salen de la versión anclada, no de la entidad viva: una
                // ficha respondida con la v1 tiene que seguir mostrando las preguntas y los
                // textos de la v1 aunque después se hayan publicado otras versiones.
                var pinned = qr.QuestionnaireVersionId == Guid.Empty
                    ? null
                    : q?.Versions?.FirstOrDefault(v => v.Id == qr.QuestionnaireVersionId);
                var structure = FhirQuestionnaireReader.Read(pinned?.FhirJson);

                if (structure != null)
                {
                    foreach (var g in structure.Groups)
                    {
                        var group = new QuestionGroupAnswered { Title = g.Title };
                        foreach (var question in g.Questions)
                        {
                            itemsByLink.TryGetValue(question.LinkId ?? "", out var items);
                            group.Questions.Add(new AnsweredQuestion
                            {
                                Text = question.Text,
                                Answer = ResponseAnswerFormatter.Format(items, question),
                                Prefilled = items != null && items.Any(i => i.WasPrefilled),
                                AttachmentId = items?.FirstOrDefault(i => i.AttachmentId.HasValue)?.AttachmentId
                            });
                        }
                        if (group.Questions.Count > 0) model.Groups.Add(group);
                    }
                }
                else
                {
                    // Ficha anterior al anclaje: no sabemos con qué versión se cargó. La
                    // fuente más fiel que queda son los textos guardados en la propia
                    // respuesta; usar el cuestionario vivo mostraría preguntas que esta
                    // ficha nunca tuvo.
                    // Las etiquetas de las opciones sí salen de la vigente, que es contra la que
                    // se pasaron a código estas respuestas.
                    var vigente = FhirQuestionnaireReader.Read(q?.CurrentVersion()?.FhirJson);
                    var answered = new QuestionGroupAnswered { Title = "Respuestas" };
                    foreach (var entry in TextsFromResponse(qr.FhirJson))
                    {
                        itemsByLink.TryGetValue(entry.Key, out var items);
                        var question = vigente?.Groups.SelectMany(g => g.Questions).FirstOrDefault(x => x.LinkId == entry.Key);
                        answered.Questions.Add(new AnsweredQuestion
                        {
                            Text = entry.Value,
                            Answer = ResponseAnswerFormatter.Format(items, question),
                            Prefilled = items != null && items.Any(i => i.WasPrefilled),
                            AttachmentId = items?.FirstOrDefault(i => i.AttachmentId.HasValue)?.AttachmentId
                        });
                    }
                    if (answered.Questions.Count > 0) model.Groups.Add(answered);
                }
                return base.View(model);
            }
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                return base.View(new EncounterReadonlyViewModel { EncounterId = id });
            }
        }

        /// <summary>
        /// linkId -> texto, leídos del recurso guardado con la propia respuesta. Es el
        /// registro de lo que el médico vio en pantalla, y el único disponible cuando la
        /// ficha no tiene versión anclada.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> TextsFromResponse(string fhirJson)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(fhirJson)) return result;
            try
            {
                var root = Newtonsoft.Json.Linq.JObject.Parse(fhirJson);
                // Las preguntas pueden venir dentro de una sección (item group).
                foreach (var item in root.SelectTokens("$..item[*]"))
                {
                    var linkId = (string)item["linkId"];
                    if (string.IsNullOrWhiteSpace(linkId) || item["item"] != null) continue;
                    result.Add(new KeyValuePair<string, string>(linkId, (string)item["text"] ?? linkId));
                }
            }
            catch (Newtonsoft.Json.JsonException) { /* recurso ilegible: no se inventa nada */ }
            return result;
        }


        // Historial de UNA ficha ("Detalle"): quién y cuándo tocó qué, reconstruido de los
        // eventos de auditoría de esa respuesta. Es la vista por-ficha de Auditoría.
        public async Task<ActionResult> History(Guid id)
        {
            ViewBag.Title = "Detalle de la ficha";
            ViewBag.PageId = "encounters";
            try
            {
                var qr = await _responses.GetAsync(id);
                if (qr == null) return HttpNotFound();
                await _accessAudit.ResponseRead(qr);
                var q = await _questionnaires.GetAsync(qr.QuestionnaireId);
                var p = await _patients.GetAsync(qr.PatientHospitalId);

                var model = new EncounterHistoryViewModel
                {
                    EncounterId = qr.Id,
                    Patient = new PatientSearchItem { HC = qr.PatientHospitalId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? SpecialtyDefaults.Code, SpecialtyLabel = p?.SpecialtyLabel ?? SpecialtyDefaults.Name },
                    QuestionnaireId = qr.QuestionnaireId,
                    QuestionnaireTitle = q?.Title,
                    QuestionnaireVersion = q?.FindVersion(qr.QuestionnaireVersionId)?.VersionNumber ?? 0,
                    Status = qr.Status.ToString().ToLowerInvariant()
                };

                foreach (var e in await _audit.ForResponseAsync(id))
                {
                    ChequeAR.Api.Helpers.AuditPresentation.ParseRef(e.EntityRef, out _, out _, out var suffix);
                    var isSave = suffix != null && suffix.StartsWith("save", StringComparison.OrdinalIgnoreCase);
                    var isFinalize = suffix != null && suffix.StartsWith("finalize", StringComparison.OrdinalIgnoreCase);
                    var isAbandon = suffix != null && suffix.StartsWith("abandon", StringComparison.OrdinalIgnoreCase);
                    var isAmend = string.Equals(suffix, "amend", StringComparison.OrdinalIgnoreCase);

                    // Sólo interesa lo que se COMPLETÓ: un avance guardado (con su diff), la
                    // finalización, o el descarte de la ficha. Se descartan "edición iniciada"
                    // ($lock) — alguien pudo abrir la ficha y cerrarla sin tocar nada —, "ficha
                    // iniciada" (create) y los cambios de versión: ninguno registra trabajo
                    // hecho sobre las respuestas.
                    if (!isSave && !isFinalize && !isAbandon && !isAmend) continue;

                    var av = ChequeAR.Api.Helpers.AuditPresentation.Describe(e.EntityRef, e.Action);
                    var entry = new EncounterHistoryEntry
                    {
                        Timestamp = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(e.Recorded),
                        Username = e.AgentUserId,
                        Label = av.Label,
                        BadgeClass = av.BadgeClass,
                        Icon = av.Icon
                    };

                    // El "Avance guardado" trae el diff de la sesión; la finalización es sólo
                    // el "quién/cuándo" (su foto completa vive en el detalle de auditoría).
                    if (isSave && !string.IsNullOrWhiteSpace(e.FhirJson))
                    {
                        try
                        {
                            var changes = JsonConvert.DeserializeObject<List<ResponseFieldChange>>(e.FhirJson);
                            if (changes != null) entry.Changes = changes;
                        }
                        catch (JsonException) { /* payload viejo o ilegible: se muestra sin diff */ }
                    }
                    if (isAmend && !string.IsNullOrWhiteSpace(e.FhirJson))
                    {
                        try
                        {
                            var amend = JsonConvert.DeserializeObject<AmendRecord>(e.FhirJson);
                            entry.Note = amend?.Reason;
                            if (amend?.Changes != null) entry.Changes = amend.Changes;
                        }
                        catch (JsonException) { }
                    }

                    model.Entries.Add(entry);
                }

                return View(model);
            }
            catch (SpecialtyForbiddenException)
            {
                return HttpNotFound();
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                return View(new EncounterHistoryViewModel { EncounterId = id });
            }
        }

        // R7 — Timeline
        public async Task<ActionResult> Timeline(string patientId, Guid? cuestionario = null, string especialidad = null, string autor = null, string estado = null, string from = null, string to = null)
        {
            ChequeAR.Api.Helpers.ArgentinaTime.NormalizeFilterRange(ref from, ref to);
            ViewBag.Title = "Historial del paciente";
            ViewBag.PageId = "encounters";
            var model = new PatientTimelineViewModel
            {
                QuestionnaireFilter = cuestionario, SpecialtyFilter = especialidad, AuthorFilter = autor,
                StatusFilter = NormalizeStatus(estado), From = from, To = to
            };
            try
            {
                var cat = await _specialties.GetAsync();
                var p = await _patients.GetAsync(patientId);
                model.Patient = new PatientSearchItem { HC = patientId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = cat.CodeOrDefault(p?.SpecialtyCode), SpecialtyLabel = p?.SpecialtyLabel ?? cat.DefaultLabel };

                // El historial de UN paciente está intrínsecamente acotado (no crece sin
                // límite como el log global de auditoría), así que se trae completo y se
                // filtra en memoria en vez de empujar el filtro al repositorio.
                // PatientService filtra por las especialidades del usuario.
                var encs = await _patients.GetEncountersAsync(patientId, 500);
                var titleCache = new Dictionary<Guid, ChequeAR.Domain.Entities.Questionnaire>();
                var all = new List<EncounterTimelineItem>();
                foreach (var e in encs)
                {
                    if (!titleCache.ContainsKey(e.QuestionnaireId))
                        titleCache[e.QuestionnaireId] = await _questionnaires.GetForPatientHistoryAsync(e.QuestionnaireId);
                    var q = titleCache[e.QuestionnaireId];
                    all.Add(new EncounterTimelineItem
                    {
                        RawDate = e.AuthoredAt,
                        Date = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(e.AuthoredAt),
                        QuestionnaireId = e.QuestionnaireId,
                        QuestionnaireTitle = q?.Title ?? e.QuestionnaireId.ToString(),
                        QuestionnaireVersion = q?.FindVersion(e.QuestionnaireVersionId)?.VersionNumber ?? 0,
                        SpecialtyCode = cat.CodeOrDefault(q?.SpecialtyCode),
                        SpecialtyLabel = cat.Label(q?.SpecialtyCode, q?.SpecialtyDisplay),
                        SpecialtyColor = cat.Color(q?.SpecialtyCode),
                        Author = e.AuthorUserId,
                        EncounterId = e.Id,
                        Status = e.Status.ToString().ToLowerInvariant()
                    });
                }

                model.AvailableQuestionnaires = all
                    .GroupBy(x => x.QuestionnaireId)
                    .Select(g => new TimelineFilterOption { Value = g.Key.ToString(), Label = g.First().QuestionnaireTitle })
                    .OrderBy(x => x.Label).ToList();
                model.AvailableSpecialties = all
                    .GroupBy(x => x.SpecialtyCode)
                    .Select(g => new TimelineFilterOption { Value = g.Key, Label = g.First().SpecialtyLabel })
                    .OrderBy(x => x.Label).ToList();
                model.AvailableAuthors = all
                    .Select(x => x.Author).Where(a => !string.IsNullOrWhiteSpace(a))
                    .Distinct().OrderBy(a => a).ToList();
                model.HasAnyEncounters = all.Count > 0;

                var fromUtc = ChequeAR.Api.Helpers.ArgentinaTime.ParseFilterDateToUtc(from, endOfDay: false);
                var toUtc = ChequeAR.Api.Helpers.ArgentinaTime.ParseFilterDateToUtc(to, endOfDay: true);

                var status = model.StatusFilter;
                model.Entries.AddRange(all.Where(x =>
                    (cuestionario == null || x.QuestionnaireId == cuestionario.Value) &&
                    (string.IsNullOrEmpty(especialidad) || x.SpecialtyCode == especialidad) &&
                    (string.IsNullOrEmpty(autor) || x.Author == autor) &&
                    (status == null || StatusMatches(x.Status, status)) &&
                    (fromUtc == null || x.RawDate >= fromUtc.Value) &&
                    (toUtc == null || x.RawDate < toUtc.Value)));

                return View(model);
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                model.Patient = new PatientSearchItem { HC = patientId };
                return View(model);
            }
        }

        // Evolución de parámetros (spec 010) — pantalla nueva, accesible desde el Historial.
        public async Task<ActionResult> Trend(string patientId, Guid? questionnaireId = null)
        {
            ViewBag.Title = "Evolución de parámetros";
            ViewBag.PageId = "encounters";

            var model = new PatientTrendViewModel { SelectedQuestionnaireId = questionnaireId };
            try
            {
                var p = await _patients.GetAsync(patientId);
                model.Patient = new PatientSearchItem { HC = patientId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? SpecialtyDefaults.Code, SpecialtyLabel = p?.SpecialtyLabel ?? SpecialtyDefaults.Name };

                // El selector ofrece SOLO los cuestionarios que el paciente completó al menos una
                // vez: son los únicos que pueden producir una serie. Un cuestionario con fichas
                // en progreso, pero ninguna finalizada, no aporta datos y no tiene sentido ofrecerlo.
                // Una ficha enmendada es una finalizada y corregida: cuenta, igual que en la vista.
                var encs = await _patients.GetEncountersAsync(patientId, 500);
                var completed = encs
                    .Where(e => e.Status == ResponseStatus.Completed || e.Status == ResponseStatus.Amended)
                    .ToList();
                model.HasAnyCompleted = completed.Count > 0;

                // Sólo los títulos, en una consulta: cargar cada cuestionario entero para
                // rotular un selector era una ida a la base (con todas sus preguntas) por opción.
                var titles = await _questionnaires.GetTitlesAsync(completed.Select(e => e.QuestionnaireId));
                model.AvailableQuestionnaires = completed
                    .GroupBy(e => e.QuestionnaireId)
                    .Select(g => new TimelineFilterOption
                    {
                        Value = g.Key.ToString(),
                        Label = titles.TryGetValue(g.Key, out var title) ? title : g.Key.ToString()
                    })
                    .OrderBy(x => x.Label).ToList();

                // Si el cuestionario elegido no está entre los que el paciente completó, se ignora
                // (no habría datos): la vista queda como si no se hubiera elegido nada.
                if (questionnaireId.HasValue &&
                    !model.AvailableQuestionnaires.Any(o => o.Value == questionnaireId.Value.ToString()))
                    model.SelectedQuestionnaireId = null;
            }
            catch (Exception ex)
            {
                Helpers.HandledErrors.Log(this, ex);
                ViewBag.DataError = "Ocurrió un error al procesar la solicitud.";
                model.Patient = model.Patient ?? new PatientSearchItem { HC = patientId };
            }
            return View(model);
        }


        /// <summary>Normaliza el filtro de estado a "pendiente"/"finalizada"; cualquier otra cosa = sin filtro.</summary>
        private static string NormalizeStatus(string estado)
        {
            var v = (estado ?? string.Empty).Trim().ToLowerInvariant();
            return v == "pendiente" || v == "finalizada" ? v : null;
        }

        /// <summary>El estado crudo de la ficha ("inprogress"/"completed"…) contra el filtro legible.</summary>
        private static bool StatusMatches(string rawStatus, string filter)
        {
            if (filter == "pendiente") return rawStatus == "inprogress";
            if (filter == "finalizada") return rawStatus == "completed" || rawStatus == "amended";
            return true;
        }

    }
}
