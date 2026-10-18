using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Application.Validation;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;

namespace ChequeAR.Application.Services
{
    /// <summary>Qué terminó pasando al pedir la baja de una variable.</summary>
    public enum VariableDeleteOutcome
    {
        Deleted,
        /// <summary>La usa al menos un cuestionario: se desactivó en vez de eliminarse.</summary>
        DeactivatedBecauseInUse
    }

    /// <summary>Una página del catálogo, con el total para paginar.</summary>
    public class ClinicalVariablePage
    {
        public IReadOnlyList<ClinicalVariable> Items { get; set; }
        public int Total { get; set; }
    }

    /// <summary>Dónde se usa una variable.</summary>
    public class VariableUsage
    {
        public int QuestionCount { get; set; }
        public IReadOnlyList<Guid> QuestionnaireIds { get; set; }
    }

    /// <summary>
    /// ABM del catálogo de variables clínicas.
    ///
    /// Asimetría deliberada de permisos: leer el catálogo pide QuestionnaireRead (el Jefe tiene
    /// que poder elegir variables para armar sus cuestionarios), pero escribirlo pide
    /// VariableManage, que sólo tiene el Administrador. Ésa es exactamente la división que la
    /// feature introduce: el Jefe consume el vocabulario clínico, no lo define.
    ///
    /// Sin caché, a diferencia de SpecialtyCatalogService: aquél cachea 5 filas que no cambian
    /// nunca; acá hay cientos y el Administrador las edita en vivo, así que un TTL de minutos
    /// haría invisible un alta recién hecha justo cuando la va a usar.
    /// </summary>
    public class ClinicalVariableService
    {
        // Slug: minúsculas, dígitos y guiones. Es la semilla del LinkId de la pregunta, que es
        // la clave sobre la que pivotean reportes y tendencias, así que no puede llevar
        // espacios, acentos ni mayúsculas que después haya que normalizar en cada consulta.
        private static readonly Regex CodePattern = new Regex("^[a-z0-9-]{2,64}$", RegexOptions.Compiled);

        private readonly IClinicalVariableRepository _repo;
        private readonly AuditDispatcher _audit;
        private readonly IAccessGuard _access;

        public ClinicalVariableService(IClinicalVariableRepository repo, AuditDispatcher audit, IAccessGuard access)
        {
            _repo = repo;
            _audit = audit;
            _access = access;
        }

        // ── Lecturas ──────────────────────────────────────────────────────────────────

        public Task<ClinicalVariable> GetAsync(Guid id)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            return _repo.GetAsync(id);
        }

        /// <summary>Varias variables por id en una sola consulta. Las que no existen no vienen.</summary>
        public Task<IReadOnlyList<ClinicalVariable>> GetManyAsync(IEnumerable<Guid> ids)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            return _repo.GetManyAsync(ids);
        }

        /// <param name="publishedOnly">
        /// Sólo las publicadas: el buscador del editor de cuestionarios no
        /// puede ofrecer una variable que nunca se publicó.
        /// </param>
        public async Task<ClinicalVariablePage> ListAsync(string search, QuestionType? type, bool? active, int skip, int take, bool publishedOnly = false)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            var items = await _repo.ListAsync(search, type, active, skip, take, publishedOnly);
            var total = await _repo.CountAsync(search, type, active, publishedOnly);
            return new ClinicalVariablePage { Items = items, Total = total };
        }

        /// <summary>
        /// La variable publicada que reemplaza a cada una de <paramref name="ids"/>, si hay. Si
        /// una se reemplazó más de una vez, la más reciente.
        /// </summary>
        public async Task<IReadOnlyDictionary<Guid, ClinicalVariable>> GetSuccessorsAsync(IEnumerable<Guid> ids)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            return ((await _repo.GetSuccessorsAsync(ids)) ?? new List<ClinicalVariable>())
                .GroupBy(v => v.ReplacesId.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.CreatedAt).First());
        }

        public async Task<VariableUsage> GetUsageAsync(Guid id)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            return new VariableUsage
            {
                QuestionCount = await _repo.CountQuestionsUsingAsync(id),
                QuestionnaireIds = await _repo.QuestionnaireIdsUsingAsync(id)
            };
        }

        // ── Escrituras ────────────────────────────────────────────────────────────────

        public async Task<ClinicalVariable> CreateAsync(ClinicalVariable v, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            if (v == null) throw new ArgumentNullException(nameof(v));

            Normalize(v);
            if (v.ReplacesId.HasValue)
            {
                var source = await _repo.GetAsync(v.ReplacesId.Value);
                if (source?.IsPublished != true)
                    throw new ValidationFailedException(new List<string> { "Sólo se puede crear una variable a partir de una publicada." });
            }
            // Si la anterior se da de baja se decide al publicar.
            v.DeactivateReplacedOnPublish = false;
            await ValidateAsync(v, existingId: null, ignoreConflictWith: v.ReplacesId);

            var now = DateTime.UtcNow;
            v.Id = v.Id == Guid.Empty ? Guid.NewGuid() : v.Id;
            v.IsActive = true;
            v.Revision = 1;
            v.CreatedBy = userId;
            v.CreatedAt = now;
            v.UpdatedBy = userId;
            v.UpdatedAt = now;
            AssignOptionIds(v);

            await _repo.AddAsync(v);
            _audit.Enqueue(AuditEventBuilder.For("create",
                "ClinicalVariable/" + v.Id + (v.ReplacesId.HasValue ? "$derive-from-" + v.ReplacesId.Value : ""), userId));
            return v;
        }

        /// <summary>
        /// Arma, sin guardarla, una variable nueva a partir de una publicada: misma definición,
        /// código libre y sin nombre, que tiene que ser distinto. Es como se cambia lo clínico de
        /// una variable publicada; ella no se edita.
        /// </summary>
        public async Task<ClinicalVariable> DeriveAsync(Guid sourceId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var source = await _repo.GetAsync(sourceId);
            if (source == null) return null;
            if (!source.IsPublished)
                throw new ValidationFailedException(new List<string> { "La variable no está publicada: se edita directamente." });

            var copy = VariableDefinition.From(source).ToVariable();
            copy.Id = Guid.Empty;
            copy.Name = null;
            copy.Code = await FreeCodeAsync(source.Code);
            copy.SortOrder = source.SortOrder;
            copy.ReplacesId = source.Id;
            foreach (var o in copy.Options) o.VariableId = Guid.Empty;
            return copy;
        }

        /// <summary>Primer "{código}-N" libre, desde N = 2.</summary>
        private async Task<string> FreeCodeAsync(string code)
        {
            var raiz = System.Text.RegularExpressions.Regex.Replace(code, "-\\d+$", "");
            for (var n = 2; ; n++)
            {
                var candidato = raiz + "-" + n;
                if (candidato.Length > 64) candidato = raiz.Substring(0, 64 - (n.ToString(CultureInfo.InvariantCulture).Length + 1)) + "-" + n;
                if (await _repo.GetByCodeAsync(candidato) == null) return candidato;
            }
        }

        /// <summary>
        /// Guarda el borrador: nadie lo ve hasta que se publique. En una variable publicada sólo
        /// se corrigen el enunciado por defecto, la descripción y el orden; lo clínico pide
        /// crear otra variable (<see cref="DeriveAsync"/>).
        /// </summary>
        /// <param name="expectedRevision">
        /// La revisión que vio quien edita. Si otro guardó en el medio, se rechaza en vez de pisarlo.
        /// </param>
        public async Task<ClinicalVariable> UpdateAsync(Guid id, ClinicalVariable patch, string userId, int? expectedRevision = null)
        {
            _access.EnsureCan(Permissions.VariableManage);
            if (patch == null) throw new ArgumentNullException(nameof(patch));

            var v = await _repo.GetAsync(id);
            if (v == null) return null;
            EnsureRevision(v, expectedRevision);

            patch.Id = id;
            patch.ReplacesId = v.ReplacesId;
            Normalize(patch);
            // Los conflictos de nombre y concepto se miran al publicar; acá sólo en un borrador.
            await ValidateAsync(patch, existingId: id, checkConflicts: !v.IsPublished, ignoreConflictWith: v.ReplacesId);

            if (v.IsPublished && !VariableDefinition.From(patch).ClinicallySameAs(VariableDefinition.From(v)))
                throw new ValidationFailedException(new List<string>
                {
                    "Una variable publicada sólo admite cambios en el enunciado por defecto y la descripción. Para cambiar el resto, cree una variable nueva a partir de esta."
                });

            v.Code = patch.Code;
            v.Name = patch.Name;
            v.DefaultText = patch.DefaultText;
            v.Description = patch.Description;
            v.Type = patch.Type;
            v.RepeatsAllowed = patch.RepeatsAllowed;
            v.Unit = patch.Unit;
            v.MinValue = patch.MinValue;
            v.MaxValue = patch.MaxValue;
            v.MaxLength = patch.MaxLength;
            v.Accept = patch.Accept;
            v.SnomedSystem = patch.SnomedSystem;
            v.SnomedCode = patch.SnomedCode;
            v.SnomedDisplay = patch.SnomedDisplay;
            v.Term = patch.Term;
            v.SortOrder = patch.SortOrder;
            v.Revision++;
            v.UpdatedBy = userId;
            v.UpdatedAt = DateTime.UtcNow;

            var options = (patch.Options ?? new List<VariableOption>()).ToList();
            foreach (var o in options) { o.Id = Guid.NewGuid(); o.VariableId = id; }

            await _repo.UpdateWithOptionsAsync(v, options);

            _audit.Enqueue(AuditEventBuilder.For("update", "ClinicalVariable/" + id, userId));
            return v;
        }

        /// <summary>
        /// Publica la variable por primera vez. Una publicada no se vuelve a publicar: sus textos
        /// se corrigen en el lugar y lo clínico se cambia creando otra. Si reemplaza a otra, se
        /// valida que se distinga y, con <paramref name="deactivateReplaced"/>, la anterior se da
        /// de baja en la misma operación.
        /// </summary>
        public async Task<ClinicalVariable> PublishAsync(Guid id, string userId, int? expectedRevision = null, bool deactivateReplaced = false)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var v = await _repo.GetAsync(id);
            if (v == null) return null;
            EnsureRevision(v, expectedRevision);
            if (v.IsPublished) return v;
            v.DeactivateReplacedOnPublish = v.ReplacesId.HasValue && deactivateReplaced;
            await ValidateAsync(v, existingId: id, ignoreConflictWith: v.DeactivateReplacedOnPublish ? v.ReplacesId : null);

            ClinicalVariable source = null;
            if (v.ReplacesId.HasValue)
            {
                source = await _repo.GetAsync(v.ReplacesId.Value);
                EnsureDistinctFromSource(v, source);
            }

            var now = DateTime.UtcNow;
            // Mueve el token de concurrencia: de dos publicaciones simultáneas, la segunda choca.
            v.Revision++;
            v.PublishedAt = now;
            v.UpdatedBy = userId;
            v.UpdatedAt = now;

            var deactivate = source != null && source.IsActive && v.DeactivateReplacedOnPublish
                ? new ClinicalVariable { Id = source.Id, UpdatedBy = userId, UpdatedAt = now }
                : null;
            await _repo.PublishAsync(v, deactivate);

            _audit.Enqueue(AuditEventBuilder.For("update", "ClinicalVariable/" + id + "$publish", userId));
            if (deactivate != null)
                _audit.Enqueue(AuditEventBuilder.For("deactivate", "ClinicalVariable/" + source.Id + "$deactivate", userId));
            return v;
        }

        /// <summary>
        /// La que reemplaza tiene que poder distinguirse de la anterior en catálogo, reportes y
        /// auditoría: otro nombre y alguna diferencia clínica. Sin diferencia clínica no hay
        /// motivo para una variable nueva.
        /// </summary>
        private static void EnsureDistinctFromSource(ClinicalVariable v, ClinicalVariable source)
        {
            if (source?.IsPublished != true)
                throw new ValidationFailedException(new List<string> { "La variable que reemplaza ya no existe o no está publicada." });

            var errors = new List<string>();
            if (NormalizeName(v.Name) == NormalizeName(source.Name))
                errors.Add("El nombre tiene que ser distinto del de '" + source.Name + "'.");
            if (VariableDefinition.From(v).ClinicallySameAs(VariableDefinition.From(source), ignoreIdentity: true))
                errors.Add("No hay cambios respecto de '" + source.Name + "': cambie el tipo, la unidad, el rango, las opciones o el concepto SNOMED.");
            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }

        /// <summary>Sólo el estado: no es un cambio de definición, así que no mueve la revisión.</summary>
        public async Task<bool> SetActiveAsync(Guid id, bool active, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var v = await _repo.GetAsync(id);
            if (v == null) return false;
            if (active && !v.IsActive && v.IsPublished)
            {
                var errors = await PublishedConflictsAsync(v, ignoreId: null);
                if (errors.Count > 0)
                {
                    errors.Add("Para reactivarla, primero dé de baja esa variable.");
                    throw new ValidationFailedException(errors);
                }
            }

            v.IsActive = active;
            v.UpdatedBy = userId;
            v.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateHeaderAsync(v);

            _audit.Enqueue(AuditEventBuilder.For(active ? "activate" : "deactivate", "ClinicalVariable/" + id + (active ? "$activate" : "$deactivate"), userId));
            return true;
        }

        /// <summary>
        /// Se borra sólo si nunca se publicó y ninguna pregunta la usa; si no, se desactiva. Una
        /// variable publicada puede estar en un cuestionario publicado, que tiene que seguir
        /// nombrando una variable que el catálogo pueda explicar.
        /// </summary>
        public async Task<VariableDeleteOutcome> DeleteAsync(Guid id, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var v = await _repo.GetAsync(id);
            var enUso = await _repo.CountQuestionsUsingAsync(id);
            if (v?.IsPublished == true || enUso > 0)
            {
                await SetActiveAsync(id, false, userId);
                return VariableDeleteOutcome.DeactivatedBecauseInUse;
            }

            await _repo.DeleteAsync(id);
            _audit.Enqueue(AuditEventBuilder.For("delete", "ClinicalVariable/" + id, userId));
            return VariableDeleteOutcome.Deleted;
        }

        // ── Validación ────────────────────────────────────────────────────────────────

        private static void EnsureRevision(ClinicalVariable v, int? expected)
        {
            if (expected.HasValue && expected.Value != v.Revision)
                throw new ConcurrencyConflictException(expected.Value, v.Revision,
                    "Otra persona modificó la variable mientras usted la editaba. Vuelva a abrirla para ver sus cambios.");
        }

        /// <summary>
        /// Concepto SNOMED y nombre ya usados por otra variable activa y publicada. Son lo que
        /// distingue a una variable en el catálogo y en los reportes. <paramref name="ignoreId"/>
        /// es la que se va a dar de baja al publicar esta.
        /// </summary>
        private async Task<List<string>> PublishedConflictsAsync(ClinicalVariable v, Guid? ignoreId)
        {
            var errors = new List<string>();
            if (v.SnomedCode != null)
            {
                var otra = ((await _repo.GetPublishedActiveBySnomedCodeAsync(v.SnomedCode)) ?? new List<ClinicalVariable>())
                    .FirstOrDefault(x => x.Id != v.Id && x.Id != ignoreId);
                if (otra != null)
                    errors.Add("La variable '" + otra.Name + "' ya usa el concepto " + v.SnomedCode + ".");
            }
            var nombre = NormalizeName(v.Name);
            if (nombre.Length > 0)
            {
                var otra = ((await _repo.GetPublishedActiveNamesAsync()) ?? new List<ClinicalVariable>())
                    .FirstOrDefault(x => x.Id != v.Id && x.Id != ignoreId && NormalizeName(x.Name) == nombre);
                if (otra != null)
                    errors.Add("Ya hay una variable activa con el nombre '" + otra.Name + "'.");
            }
            return errors;
        }

        /// <summary>Sin mayúsculas, acentos ni espacios de más: "Peso " y "peso" son el mismo nombre.</summary>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var sinAcentos = new string(name.Trim().Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            return Regex.Replace(sinAcentos, "\\s+", " ").ToLowerInvariant();
        }

        private static void Normalize(ClinicalVariable v)
        {
            v.Code = v.Code?.Trim().ToLower(CultureInfo.InvariantCulture);
            v.Name = v.Name?.Trim();
            v.SnomedCode = Blank(v.SnomedCode);
            v.Unit = Blank(v.Unit);
            v.Accept = Blank(v.Accept);
            v.Term = Blank(v.Term);

            // Un código sin sistema no es interoperable con nadie: si el admin eligió un
            // concepto, el sistema es SNOMED salvo que declare otro.
            if (v.SnomedCode != null && string.IsNullOrWhiteSpace(v.SnomedSystem))
                v.SnomedSystem = TerminologyService.SnomedSystem;

            // Campos que no aplican al tipo se limpian en vez de rechazarse: el formulario los
            // muestra y esconde según el tipo, y cambiar de tipo dejaría residuos invisibles
            // que después viajarían al ValidationJson de cada pregunta.
            if (!v.IsNumeric()) { v.Unit = null; v.MinValue = null; v.MaxValue = null; }
            if (!v.AcceptsMaxLength()) v.MaxLength = null;
            if (v.Type != QuestionType.Attachment) v.Accept = null;
            if (!v.RequiresOptions()) v.Options = new List<VariableOption>();

            foreach (var o in v.Options ?? new List<VariableOption>())
            {
                o.Display = o.Display?.Trim();
                o.Value = Blank(o.Value) ?? o.Display;
                o.SnomedCode = Blank(o.SnomedCode);
                // Code == SnomedCode cuando la opción está codificada: R5 admite un solo Coding
                // por answerOption.value, y unificarlos evita que el código que guardan las
                // respuestas (ValueCode, sobre el que agrupan los reportes) difiera del FHIR.
                o.Code = o.SnomedCode ?? Blank(o.Code) ?? o.Value;
                if (o.SnomedCode != null && string.IsNullOrWhiteSpace(o.SnomedSystem))
                    o.SnomedSystem = TerminologyService.SnomedSystem;
            }
        }

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>
        /// Junta TODAS las reglas incumplidas en una sola excepción. Devolver la primera haría
        /// que el admin corrija y reenvíe una vez por error; la interfaz emite un issue por regla.
        /// </summary>
        /// <param name="ignoreConflictWith">
        /// La anterior, cuando no choca: en un borrador porque todavía se puede elegir darla de
        /// baja, y al publicar porque se la da de baja.
        /// </param>
        private async Task ValidateAsync(ClinicalVariable v, Guid? existingId, bool checkConflicts = true, Guid? ignoreConflictWith = null)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(v.Name)) errors.Add("La variable debe tener un nombre.");
            if (string.IsNullOrWhiteSpace(v.Code)) errors.Add("La variable debe tener un código.");
            else if (!CodePattern.IsMatch(v.Code))
                errors.Add("El código sólo admite minúsculas, números y guiones (entre 2 y 64 caracteres).");
            else
            {
                var otro = await _repo.GetByCodeAsync(v.Code);
                if (otro != null && otro.Id != existingId)
                    errors.Add("Ya existe una variable con el código '" + v.Code + "'.");
            }

            if (!Enum.IsDefined(typeof(QuestionType), v.Type))
                errors.Add("El tipo de respuesta no es válido.");

            if (v.RequiresOptions())
            {
                var opts = (v.Options ?? new List<VariableOption>()).ToList();
                if (opts.Count < 2)
                    errors.Add("Una variable de opción necesita al menos dos opciones de respuesta.");
                if (opts.Any(o => string.IsNullOrWhiteSpace(o.Display)))
                    errors.Add("Todas las opciones de respuesta necesitan un texto.");
                var dupes = opts.Where(o => o.Code != null)
                                .GroupBy(o => o.Code, StringComparer.OrdinalIgnoreCase)
                                .Where(g => g.Count() > 1)
                                .Select(g => g.Key);
                foreach (var d in dupes)
                    errors.Add("Hay más de una opción con el código '" + d + "'.");
            }

            if (v.MinValue.HasValue && v.MaxValue.HasValue && v.MinValue > v.MaxValue)
                errors.Add("El mínimo no puede ser mayor que el máximo.");

            if (v.MaxLength.HasValue && v.MaxLength <= 0)
                errors.Add("El largo máximo debe ser mayor que cero.");
            if (v.MaxLength > ResponseValidator.MaxTextLength)
                errors.Add("El largo máximo no puede superar los " + ResponseValidator.MaxTextLength + " caracteres.");


            v.Id = existingId ?? v.Id;
            if (checkConflicts)
                errors.AddRange(await PublishedConflictsAsync(v, ignoreConflictWith));

            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }


        private static void AssignOptionIds(ClinicalVariable v)
        {
            var i = 0;
            foreach (var o in v.Options ?? new List<VariableOption>())
            {
                o.Id = o.Id == Guid.Empty ? Guid.NewGuid() : o.Id;
                o.VariableId = v.Id;
                o.OrderIndex = i++;
            }
        }
    }
}
