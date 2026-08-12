using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using ChequeAR.Api.Models;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChequeAR.Api.Controllers.Mvc
{
    // TODO: re-habilitar [Authorize] cuando FormsAuth esté wired contra el hospital.
    public class EncountersController : Controller
    {
        private readonly PatientService _patients;
        private readonly QuestionnaireResponseService _responses;
        private readonly QuestionnaireService _questionnaires;
        private readonly AuditQueryService _audit;

        public EncountersController(PatientService patients, QuestionnaireResponseService responses, QuestionnaireService questionnaires, AuditQueryService audit)
        {
            _patients = patients;
            _responses = responses;
            _questionnaires = questionnaires;
            _audit = audit;
        }

        // R1
        public async Task<ActionResult> Search(string q = null)
        {
            ViewBag.Title = "Pacientes";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", (string)null)
            };

            var model = new PatientSearchViewModel { Query = q, HasSearched = !string.IsNullOrWhiteSpace(q) };

            // Sólo se pueden cargar fichas de cuestionarios PUBLICADOS: un borrador no está
            // habilitado para uso clínico y uno archivado ya no se usa.
            try
            {
                var published = await _questionnaires.SearchAsync(QuestionnaireStatus.Active, null, null, 0, 200);
                foreach (var pq in published.OrderBy(x => x.Title))
                {
                    model.Questionnaires.Add(new SelectableQuestionnaire
                    {
                        Id = pq.Id,
                        Title = pq.Title,
                        Version = pq.Versions?.Count ?? 0,
                        SpecialtyCode = pq.SpecialtyCode ?? "general",
                        SpecialtyLabel = SpecialtyMap.Label(pq.SpecialtyCode, pq.SpecialtyDisplay)
                    });
                }
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
            }

            if (!model.HasSearched) return View(model);

            try
            {
                var hits = await _patients.SearchAsync(q);
                foreach (var h in hits)
                {
                    model.Results.Add(new PatientSearchItem
                    {
                        HC = h.HospitalId, FullName = h.FullName,
                        AgeLabel = h.AgeLabel,
                        SpecialtyCode = h.SpecialtyCode ?? "general",
                        SpecialtyLabel = SpecialtyMap.Label(h.SpecialtyCode, h.SpecialtyLabel)
                    });
                }
            }
            catch (Exception ex)
            {
                model.Results.Clear();
                ViewBag.DataError = ex.Message;
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
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Carga de ficha", (string)null)
            };

            try
            {
                var p = await _patients.GetAsync(patientId);
                ViewBag.PatientName = p != null ? (p.FullName + " (HC " + p.HospitalId + ")") : ("HC " + patientId);

                var q = await _questionnaires.GetAsync(questionnaireId);
                ViewBag.QuestionnaireTitle = q?.Title ?? "Cuestionario";

                var current = CurrentVersion(q);
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
                var version = ResolveResumeVersion(q, draft) ?? current;
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
                ViewBag.PatientName = "HC " + patientId;
                ViewBag.QuestionnaireTitle = null;
                ViewBag.Version = 0;
                ViewBag.DataError = ex.Message;
            }
            return View();
        }

        /// <summary>
        /// Número de la versión a la que quedó anclada una respuesta. 0 si no se puede
        /// resolver (fichas viejas, previas al anclaje, que guardaron Guid.Empty).
        /// </summary>
        private static int PinnedVersionNumber(ChequeAR.Domain.Entities.Questionnaire q, Guid versionId)
        {
            if (q?.Versions == null || versionId == Guid.Empty) return 0;
            return q.Versions.FirstOrDefault(v => v.Id == versionId)?.VersionNumber ?? 0;
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
            catch
            {
                return null;   // sin base no se puede resumir: el llamador cae a la versión vigente
            }
        }

        /// <summary>
        /// Versión a la que hay que anclar el editor al reabrir un borrador: la que se congeló
        /// cuando se inició. Devuelve null cuando no hay borrador o es anterior al anclaje de
        /// versión (VersionId vacío); en ambos casos el llamador cae a la versión vigente.
        /// </summary>
        private static ChequeAR.Domain.Entities.QuestionnaireVersion ResolveResumeVersion(
            ChequeAR.Domain.Entities.Questionnaire q, ChequeAR.Domain.Entities.QuestionnaireResponse draft)
        {
            if (q?.Versions == null || q.Versions.Count == 0) return null;
            if (draft == null || draft.QuestionnaireVersionId == Guid.Empty) return null;
            return q.Versions.FirstOrDefault(v => v.Id == draft.QuestionnaireVersionId);
        }

        /// <summary>Versión viva del cuestionario, o null si nunca se publicó.</summary>
        private static ChequeAR.Domain.Entities.QuestionnaireVersion CurrentVersion(ChequeAR.Domain.Entities.Questionnaire q)
        {
            if (q?.Versions == null || q.Versions.Count == 0) return null;
            if (q.CurrentVersionId.HasValue)
            {
                var current = q.Versions.FirstOrDefault(v => v.Id == q.CurrentVersionId.Value);
                if (current != null) return current;
            }
            return q.Versions.OrderByDescending(v => v.VersionNumber).First();
        }

        // R6 — Read-only
        public async Task<ActionResult> View(Guid id)
        {
            ViewBag.Title = "Ficha";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Ficha", (string)null)
            };
            try
            {
                var qr = await _responses.GetAsync(id);
                if (qr == null) return HttpNotFound();
                var q = await _questionnaires.GetAsync(qr.QuestionnaireId);
                var p = await _patients.GetAsync(qr.PatientHospitalId);
                var model = new EncounterReadonlyViewModel
                {
                    EncounterId = qr.Id,
                    Patient = new PatientSearchItem { HC = qr.PatientHospitalId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? "general", SpecialtyLabel = p?.SpecialtyLabel ?? "General" },
                    QuestionnaireTitle = q?.Title,
                    // La versión con la que se completó ESTA ficha, no la que esté vigente
                    // ahora: si se publicó otra después, la ficha sigue siendo de la suya.
                    QuestionnaireVersion = PinnedVersionNumber(q, qr.QuestionnaireVersionId),
                    Author = qr.AuthorUserId,
                    FinalizedAt = ChequeAR.Api.Helpers.ArgentinaTime.FormatDateTime(qr.LastSavedAt)
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
                                Answer = FormatAnswers(items),
                                Prefilled = items != null && items.Any(i => i.WasPrefilled)
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
                    var answered = new QuestionGroupAnswered { Title = "Respuestas" };
                    foreach (var entry in TextsFromResponse(qr.FhirJson))
                    {
                        itemsByLink.TryGetValue(entry.Key, out var items);
                        answered.Questions.Add(new AnsweredQuestion
                        {
                            Text = entry.Value,
                            Answer = FormatAnswers(items),
                            Prefilled = items != null && items.Any(i => i.WasPrefilled)
                        });
                    }
                    if (answered.Questions.Count > 0) model.Groups.Add(answered);
                }
                return base.View(model);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
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
                var items = Newtonsoft.Json.Linq.JObject.Parse(fhirJson)["item"] as Newtonsoft.Json.Linq.JArray;
                foreach (var item in items ?? new Newtonsoft.Json.Linq.JArray())
                {
                    var linkId = (string)item["linkId"];
                    if (string.IsNullOrWhiteSpace(linkId)) continue;
                    result.Add(new KeyValuePair<string, string>(linkId, (string)item["text"] ?? linkId));
                }
            }
            catch (Newtonsoft.Json.JsonException) { /* recurso ilegible: no se inventa nada */ }
            return result;
        }

        private static string FormatAnswers(List<ChequeAR.Domain.Entities.QuestionnaireResponseItem> items)
        {
            if (items == null || items.Count == 0) return "—";
            var values = items.Select(FormatAnswer).Where(v => v != "—").ToList();
            return values.Count > 0 ? string.Join(", ", values) : "—";
        }

        private static string FormatAnswer(ChequeAR.Domain.Entities.QuestionnaireResponseItem item)
        {
            if (item == null) return "—";
            if (item.ValueBoolean.HasValue) return item.ValueBoolean.Value ? "Sí" : "No";
            if (item.ValueInteger.HasValue) return item.ValueInteger.Value.ToString();
            if (item.ValueDecimal.HasValue) return item.ValueDecimal.Value.ToString();
            if (item.ValueDate.HasValue) return item.ValueDate.Value.ToString("yyyy-MM-dd");
            if (!string.IsNullOrEmpty(item.ValueString)) return item.ValueString;
            if (!string.IsNullOrEmpty(item.ValueCode)) return item.ValueCode;
            return "—";
        }

        // Historial de UNA ficha ("Detalle"): quién y cuándo tocó qué, reconstruido de los
        // eventos de auditoría de esa respuesta. Es la vista por-ficha de Auditoría.
        public async Task<ActionResult> History(Guid id)
        {
            ViewBag.Title = "Detalle de la ficha";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Detalle de la ficha", (string)null)
            };
            try
            {
                var qr = await _responses.GetAsync(id);
                if (qr == null) return HttpNotFound();
                var q = await _questionnaires.GetAsync(qr.QuestionnaireId);
                var p = await _patients.GetAsync(qr.PatientHospitalId);

                var model = new EncounterHistoryViewModel
                {
                    EncounterId = qr.Id,
                    Patient = new PatientSearchItem { HC = qr.PatientHospitalId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? "general", SpecialtyLabel = p?.SpecialtyLabel ?? "General" },
                    QuestionnaireId = qr.QuestionnaireId,
                    QuestionnaireTitle = q?.Title,
                    QuestionnaireVersion = PinnedVersionNumber(q, qr.QuestionnaireVersionId),
                    Status = qr.Status.ToString().ToLowerInvariant()
                };

                foreach (var e in await _audit.ForResponseAsync(id))
                {
                    ChequeAR.Api.Helpers.AuditPresentation.ParseRef(e.EntityRef, out _, out _, out var suffix);
                    var isSave = suffix != null && suffix.StartsWith("save", StringComparison.OrdinalIgnoreCase);
                    var isFinalize = suffix != null && suffix.StartsWith("finalize", StringComparison.OrdinalIgnoreCase);

                    // Sólo interesa lo que se COMPLETÓ: un avance guardado (con su diff) o la
                    // finalización. Se descartan "edición iniciada" ($lock) — alguien pudo abrir
                    // la ficha y cerrarla sin tocar nada —, "ficha iniciada" (create) y los
                    // cambios de versión: ninguno registra trabajo hecho sobre las respuestas.
                    if (!isSave && !isFinalize) continue;

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

                    model.Entries.Add(entry);
                }

                return View(model);
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                return View(new EncounterHistoryViewModel { EncounterId = id });
            }
        }

        // R7 — Timeline
        public async Task<ActionResult> Timeline(string patientId, Guid? cuestionario = null, string especialidad = null, string autor = null, string estado = null, string from = null, string to = null)
        {
            NormalizeDateRange(ref from, ref to);
            ViewBag.Title = "Historial del paciente";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Historial", (string)null)
            };
            var model = new PatientTimelineViewModel
            {
                QuestionnaireFilter = cuestionario, SpecialtyFilter = especialidad, AuthorFilter = autor,
                StatusFilter = NormalizeStatus(estado), From = from, To = to
            };
            try
            {
                var p = await _patients.GetAsync(patientId);
                model.Patient = new PatientSearchItem { HC = patientId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? "general", SpecialtyLabel = p?.SpecialtyLabel ?? "General" };

                // El historial de UN paciente está intrínsecamente acotado (no crece sin
                // límite como el log global de auditoría), así que se trae completo y se
                // filtra en memoria en vez de empujar el filtro al repositorio.
                var encs = await _patients.GetEncountersAsync(patientId, 500);
                var titleCache = new Dictionary<Guid, ChequeAR.Domain.Entities.Questionnaire>();
                var all = new List<EncounterTimelineItem>();
                foreach (var e in encs)
                {
                    if (!titleCache.ContainsKey(e.QuestionnaireId))
                        titleCache[e.QuestionnaireId] = await _questionnaires.GetAsync(e.QuestionnaireId);
                    var q = titleCache[e.QuestionnaireId];
                    all.Add(new EncounterTimelineItem
                    {
                        RawDate = e.AuthoredAt,
                        Date = ChequeAR.Api.Helpers.ArgentinaTime.FormatDate(e.AuthoredAt),
                        QuestionnaireId = e.QuestionnaireId,
                        QuestionnaireTitle = q?.Title ?? e.QuestionnaireId.ToString(),
                        QuestionnaireVersion = PinnedVersionNumber(q, e.QuestionnaireVersionId),
                        SpecialtyCode = q?.SpecialtyCode ?? "general",
                        SpecialtyLabel = SpecialtyMap.Label(q?.SpecialtyCode, q?.SpecialtyDisplay),
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

                var fromUtc = ParseArgDateToUtc(from, endOfDay: false);
                var toUtc = ParseArgDateToUtc(to, endOfDay: true);

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
                ViewBag.DataError = ex.Message;
                model.Patient = new PatientSearchItem { HC = patientId };
                return View(model);
            }
        }

        // Evolución de parámetros (spec 010) — pantalla nueva, accesible desde el Historial.
        public async Task<ActionResult> Trend(string patientId, Guid? questionnaireId = null)
        {
            ViewBag.Title = "Evolución de parámetros";
            ViewBag.PageId = "encounters";
            ViewBag.Breadcrumbs = new List<System.Tuple<string, string>>
            {
                System.Tuple.Create("Inicio", Url.Action("Index", "Home")),
                System.Tuple.Create("Pacientes", Url.Action("Search")),
                System.Tuple.Create("Historial", Url.Action("Timeline", new { patientId })),
                System.Tuple.Create("Evolución", (string)null)
            };

            var model = new PatientTrendViewModel { SelectedQuestionnaireId = questionnaireId };
            try
            {
                var p = await _patients.GetAsync(patientId);
                model.Patient = new PatientSearchItem { HC = patientId, FullName = p?.FullName, AgeLabel = p?.AgeLabel, SpecialtyCode = p?.SpecialtyCode ?? "general", SpecialtyLabel = p?.SpecialtyLabel ?? "General" };

                // El selector ofrece SOLO los cuestionarios que el paciente completó al menos una
                // vez: son los únicos que pueden producir una serie. Un cuestionario con fichas
                // en progreso, pero ninguna finalizada, no aporta datos y no tiene sentido ofrecerlo.
                var encs = await _patients.GetEncountersAsync(patientId, 500);
                var completed = encs.Where(e => e.Status == ResponseStatus.Completed).ToList();
                model.HasAnyCompleted = completed.Count > 0;

                var titleCache = new Dictionary<Guid, string>();
                foreach (var qId in completed.Select(e => e.QuestionnaireId).Distinct())
                {
                    if (!titleCache.ContainsKey(qId))
                        titleCache[qId] = (await _questionnaires.GetAsync(qId))?.Title ?? qId.ToString();
                }
                model.AvailableQuestionnaires = completed
                    .GroupBy(e => e.QuestionnaireId)
                    .Select(g => new TimelineFilterOption { Value = g.Key.ToString(), Label = titleCache[g.Key] })
                    .OrderBy(x => x.Label).ToList();

                // Si el cuestionario elegido no está entre los que el paciente completó, se ignora
                // (no habría datos): la vista queda como si no se hubiera elegido nada.
                if (questionnaireId.HasValue &&
                    !model.AvailableQuestionnaires.Any(o => o.Value == questionnaireId.Value.ToString()))
                    model.SelectedQuestionnaireId = null;
            }
            catch (Exception ex)
            {
                ViewBag.DataError = ex.Message;
                model.Patient = model.Patient ?? new PatientSearchItem { HC = patientId };
            }
            return View(model);
        }

        /// <summary>Fecha "yyyy-MM-dd" (local ARG) a límite UTC. endOfDay=true toma el día siguiente (cota superior exclusiva).</summary>
        private static DateTime? ParseArgDateToUtc(string date, bool endOfDay)
        {
            if (!DateTime.TryParseExact((date ?? string.Empty).Trim(), "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                return null;
            return ChequeAR.Api.Helpers.ArgentinaTime.ToUtc(endOfDay ? d.Date.AddDays(1) : d.Date);
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
            if (filter == "finalizada") return rawStatus == "completed";
            return true;
        }

        /// <summary>
        /// Si "desde" quedó después de "hasta" (rango invertido, tipeado a mano en la URL o
        /// pegado sin pasar por el date-range picker), se intercambian acá. Nunca debe llegar a
        /// filtrarse con un rango negativo.
        /// </summary>
        private static void NormalizeDateRange(ref string from, ref string to)
        {
            var fd = ParseArgDateToUtc(from, endOfDay: false);
            var td = ParseArgDateToUtc(to, endOfDay: false);
            if (fd.HasValue && td.HasValue && fd.Value > td.Value)
            {
                var tmp = from; from = to; to = tmp;
            }
        }
    }
}
