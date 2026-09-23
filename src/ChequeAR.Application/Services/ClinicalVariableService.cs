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

        public async Task<ClinicalVariablePage> ListAsync(string search, QuestionType? type, bool? active, int skip, int take)
        {
            _access.EnsureCan(Permissions.QuestionnaireRead);
            var items = await _repo.ListAsync(search, type, active, skip, take);
            var total = await _repo.CountAsync(search, type, active);
            return new ClinicalVariablePage { Items = items, Total = total };
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
        /// Incrementa Revision y NO toca ninguna pregunta existente: lo ya armado queda
        /// congelado con su snapshot, y es el editor el que avisa que quedó desfasado. Propagar
        /// el cambio por atrás cambiaría cuestionarios que nadie pidió cambiar -incluidos los
        /// publicados, que deben ser reproducibles-.
        /// </summary>
        public async Task<ClinicalVariable> UpdateAsync(Guid id, ClinicalVariable patch, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);
            if (patch == null) throw new ArgumentNullException(nameof(patch));

            var v = await _repo.GetAsync(id);
            if (v == null) return null;

            patch.Id = id;
            Normalize(patch);
            await ValidateAsync(patch, existingId: id);

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

        public async Task<bool> SetActiveAsync(Guid id, bool active, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);

            var v = await _repo.GetAsync(id);
            if (v == null) return false;

            v.IsActive = active;
            v.Revision++;
            v.UpdatedBy = userId;
            v.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateWithOptionsAsync(v, v.Options.ToList());

            _audit.Enqueue(AuditEventBuilder.For(active ? "activate" : "deactivate", "ClinicalVariable/" + id, userId));
            return true;
        }

        /// <summary>
        /// Espejo de QuestionnaireService.DeleteAsync: si algo la usa no se borra, se desactiva.
        /// Los cuestionarios que ya la tienen siguen funcionando porque guardaron el snapshot;
        /// lo que se bloquea es el uso NUEVO. Borrar de verdad dejaría una versión publicada
        /// nombrando un concepto que el catálogo ya no puede explicar.
        /// </summary>
        public async Task<VariableDeleteOutcome> DeleteAsync(Guid id, string userId)
        {
            _access.EnsureCan(Permissions.VariableManage);

            var enUso = await _repo.CountQuestionsUsingAsync(id);
            if (enUso > 0)
            {
                await SetActiveAsync(id, false, userId);
                return VariableDeleteOutcome.DeactivatedBecauseInUse;
            }

            await _repo.DeleteAsync(id);
            _audit.Enqueue(AuditEventBuilder.For("delete", "ClinicalVariable/" + id, userId));
            return VariableDeleteOutcome.Deleted;
        }

        // ── Validación ────────────────────────────────────────────────────────────────

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
