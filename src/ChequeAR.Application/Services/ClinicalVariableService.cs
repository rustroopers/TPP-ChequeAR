using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChequeAR.Application.Exceptions;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
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

    /// <summary>Estado de publicación de una variable.</summary>
    public class VariablePublication
    {
        /// <summary>Número de la versión vigente; 0 si nunca se publicó.</summary>
        public int CurrentVersion { get; set; }
        public bool HasUnpublishedChanges { get; set; }
    }

    /// <summary>La versión vigente de una variable, con su definición.</summary>
    public class PublishedVariable
    {
        public int VersionNumber { get; set; }
        public VariableDefinition Definition { get; set; }
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
        /// Sólo las que tienen versión publicada: el buscador del editor de cuestionarios no
        /// puede ofrecer una variable que nunca se publicó.
        /// </param>
        public async Task<ClinicalVariablePage> ListAsync(string search, QuestionType? type, bool? active, int skip, int take, bool publishedOnly = false)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            var items = await _repo.ListAsync(search, type, active, skip, take, publishedOnly);
            var total = await _repo.CountAsync(search, type, active, publishedOnly);
            return new ClinicalVariablePage { Items = items, Total = total };
        }

        /// <summary>Versión vigente y cambios sin publicar de cada variable, sin leer las definiciones.</summary>
        public async Task<IReadOnlyDictionary<Guid, VariablePublication>> GetPublicationAsync(IEnumerable<ClinicalVariable> variables)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            var list = (variables ?? Enumerable.Empty<ClinicalVariable>()).Where(v => v != null).ToList();
            var headers = await _repo.GetCurrentVersionHeadersAsync(list.Select(v => v.Id));
            return list.GroupBy(v => v.Id).ToDictionary(g => g.Key, g =>
            {
                var v = g.First();
                return headers.TryGetValue(v.Id, out var current)
                    ? new VariablePublication { CurrentVersion = current.VersionNumber, HasUnpublishedChanges = current.SourceRevision != v.Revision }
                    : new VariablePublication { CurrentVersion = 0, HasUnpublishedChanges = true };
            });
        }

        /// <summary>Definición y número de la versión vigente de cada variable publicada, en una consulta.</summary>
        public async Task<IReadOnlyDictionary<Guid, PublishedVariable>> GetCurrentDefinitionsAsync(IEnumerable<ClinicalVariable> variables)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            var list = (variables ?? Enumerable.Empty<ClinicalVariable>()).Where(v => v?.CurrentVersionId != null).ToList();
            var versions = (await _repo.GetVersionsAsync(list.Select(v => v.Id))).ToDictionary(x => x.Id);
            var result = new Dictionary<Guid, PublishedVariable>();
            foreach (var v in list)
                if (versions.TryGetValue(v.CurrentVersionId.Value, out var version))
                    result[v.Id] = new PublishedVariable { VersionNumber = version.VersionNumber, Definition = VariableDefinition.Parse(version.DefinitionJson) };
            return result;
        }

        public Task<IReadOnlyList<ClinicalVariableVersion>> ListVersionsAsync(Guid variableId)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            return _repo.ListVersionsAsync(variableId);
        }

        /// <summary>La definición de una versión publicada; null si no existe.</summary>
        public async Task<VariableDefinition> GetVersionAsync(Guid variableId, int versionNumber)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            var version = await _repo.GetVersionAsync(variableId, versionNumber);
            return version == null ? null : VariableDefinition.Parse(version.DefinitionJson);
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
            await ValidateAsync(v, existingId: null);

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
            _audit.Enqueue(AuditEventBuilder.For("create", "ClinicalVariable/" + v.Id, userId));
            return v;
        }

        /// <summary>
        /// Guarda el borrador: nadie lo ve hasta que se publique. Ninguna pregunta cambia; el
        /// editor de cuestionarios avisa cuando hay una versión publicada más nueva.
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
            Normalize(patch);
            await ValidateAsync(patch, existingId: id);
            EnsureIdentityKept(v, patch);


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
            v.Pattern = patch.Pattern;
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
        /// Publica el borrador como versión nueva. Si no cambió nada respecto de la vigente, no
        /// crea otra: devuelve la vigente.
        /// </summary>
        public async Task<ClinicalVariableVersion> PublishAsync(Guid id, string userId, string changeNote = null, int? expectedRevision = null)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var v = await _repo.GetAsync(id);
            if (v == null) return null;
            EnsureRevision(v, expectedRevision);
            await ValidateAsync(v, existingId: id);
            if (changeNote != null && changeNote.Trim().Length > 2000)
                throw new ValidationFailedException(new List<string> { "Las notas del cambio no pueden superar los 2000 caracteres." });

            var definition = VariableDefinition.From(v);
            var current = v.CurrentVersionId.HasValue ? await _repo.GetVersionAsync(v.CurrentVersionId.Value) : null;
            if (current != null && definition.SameAs(VariableDefinition.Parse(current.DefinitionJson)))
            {
                if (current.SourceRevision != v.Revision)
                {
                    current.SourceRevision = v.Revision;
                    await _repo.UpdateVersionSourceAsync(current);
                }
                return current;
            }

            var versions = await _repo.ListVersionsAsync(id);
            // Mueve el token de concurrencia: de dos publicaciones simultáneas, la segunda choca.
            v.Revision++;
            var version = new ClinicalVariableVersion
            {
                Id = Guid.NewGuid(),
                VariableId = id,
                VersionNumber = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNumber) + 1,
                SourceRevision = v.Revision,
                DefinitionJson = definition.ToJson(),
                PublishedBy = userId,
                PublishedAt = DateTime.UtcNow,
                ChangeNote = string.IsNullOrWhiteSpace(changeNote) ? null : changeNote.Trim()
            };
            v.CurrentVersionId = version.Id;
            await _repo.AddVersionAsync(v, version);

            _audit.Enqueue(AuditEventBuilder.For("update", "ClinicalVariable/" + id + "$publish", userId));
            return version;
        }

        /// <summary>Sólo el estado: no es un cambio de definición, así que no crea versión ni mueve la revisión.</summary>
        public async Task<bool> SetActiveAsync(Guid id, bool active, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var v = await _repo.GetAsync(id);
            if (v == null) return false;

            v.IsActive = active;
            v.UpdatedBy = userId;
            v.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateHeaderAsync(v);

            _audit.Enqueue(AuditEventBuilder.For(active ? "activate" : "deactivate", "ClinicalVariable/" + id, userId));
            return true;
        }

        /// <summary>
        /// Se borra sólo si nunca se publicó y ninguna pregunta la usa; si no, se desactiva. Una
        /// versión publicada puede estar en un cuestionario publicado, que tiene que seguir
        /// nombrando una variable que el catálogo pueda explicar.
        /// </summary>
        public async Task<VariableDeleteOutcome> DeleteAsync(Guid id, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            var v = await _repo.GetAsync(id);
            var enUso = await _repo.CountQuestionsUsingAsync(id);
            if (v?.CurrentVersionId != null || enUso > 0)
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
                    "Otra persona modificó la variable mientras la editabas. Volvé a abrirla para ver sus cambios.");
        }

        /// <summary>
        /// Código, tipo y repetición definen qué dato es: reportes, evolución y re-anclaje unen
        /// por linkId, y cambiarlos mezclaría datos distintos bajo la misma columna.
        /// </summary>
        private static void EnsureIdentityKept(ClinicalVariable current, ClinicalVariable patch)
        {
            if (!current.CurrentVersionId.HasValue) return;
            var errors = new List<string>();
            if (!string.Equals(current.Code, patch.Code, StringComparison.Ordinal))
                errors.Add("El código no se puede cambiar en una variable publicada.");
            if (current.Type != patch.Type)
                errors.Add("El tipo de respuesta no se puede cambiar en una variable publicada. Para otro tipo, creá otra variable.");
            if (current.RepeatsAllowed != patch.RepeatsAllowed)
                errors.Add("Si admite varias respuestas no se puede cambiar en una variable publicada.");
            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }

        private static void Normalize(ClinicalVariable v)
        {
            v.Code = v.Code?.Trim().ToLower(CultureInfo.InvariantCulture);
            v.Name = v.Name?.Trim();
            v.SnomedCode = Blank(v.SnomedCode);
            v.Unit = Blank(v.Unit);
            v.Pattern = Blank(v.Pattern);
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
        private async Task ValidateAsync(ClinicalVariable v, Guid? existingId)
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

            if (v.Pattern != null)
            {
                // Se compila acá y no al validar cada respuesta: un patrón roto guardado en el
                // catálogo se convertiría en una excepción en el renderer de CADA cuestionario
                // que use la variable, lejos de donde se cometió el error.
                if (!Compila(v.Pattern)) errors.Add("La expresión regular de validación no es válida.");
            }

            if (v.SnomedCode != null)
            {
                var otro = await _repo.GetBySnomedCodeAsync(v.SnomedCode);
                if (otro != null && otro.Id != existingId)
                    errors.Add("La variable '" + otro.Name + "' ya usa el concepto " + v.SnomedCode + ".");
            }

            if (errors.Count > 0) throw new ValidationFailedException(errors);
        }

        private static bool Compila(string pattern)
        {
            try { return new Regex(pattern).ToString() != null; }
            catch (ArgumentException) { return false; }
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
