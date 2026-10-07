using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;
using ChequeAR.Application.Security;
using ChequeAR.Domain.Entities;
using Newtonsoft.Json;

namespace ChequeAR.Application.Services
{
    /// <summary>Qué pasó al intentar modificar una consulta guardada.</summary>
    public enum SavedReportOutcome
    {
        Ok,
        NotFound,
        /// <summary>Existe, pero es de otra persona.</summary>
        Forbidden
    }

    public class SavedReportResult
    {
        public SavedReportOutcome Outcome { get; set; }
        public SavedReport Report { get; set; }

        public static SavedReportResult Ok(SavedReport r) => new SavedReportResult { Outcome = SavedReportOutcome.Ok, Report = r };
        public static SavedReportResult NotFound() => new SavedReportResult { Outcome = SavedReportOutcome.NotFound };
        public static SavedReportResult Forbidden() => new SavedReportResult { Outcome = SavedReportOutcome.Forbidden };
    }

    /// <summary>
    /// Consultas de reporte guardadas.
    ///
    /// La autorización por propietario vivía dentro del controller, y por eso no tenía
    /// ninguna prueba: verificar "una persona no puede editar la consulta de otra" exigía
    /// levantar la capa web. Es una regla de negocio y pertenece acá.
    ///
    /// Lo mismo con la autorización por especialidad: antes ListForUserAsync recibía la
    /// lista de especialidades COMO PARÁMETRO, y un llamador podía mandar una lista vacía
    /// (a propósito o por bug -- pasó, ver Mvc/ReportsController) sin que el servicio tuviera
    /// forma de notarlo. Ahora el scope sale de ISpecialtyGuard, la misma fuente de verdad
    /// que usa QuestionnaireService: la firma ya no permite mentirle al servicio.
    /// </summary>
    public class SavedReportService
    {
        private readonly ISavedReportRepository _repo;
        private readonly AuditDispatcher _audit;
        private readonly ISpecialtyGuard _guard;

        public SavedReportService(ISavedReportRepository repo, AuditDispatcher audit, ISpecialtyGuard guard)
        {
            _repo = repo;
            _audit = audit;
            _guard = guard;
        }

        /// <summary>
        /// Lo propio + lo compartido a nivel especialidad dentro del scope del usuario
        /// actual. Unrestricted (Administrador, o el flag de scope apagado) => null al
        /// repositorio, que lo interpreta como "cualquier especialidad" -- no sólo las que
        /// el claim del usuario declara, que para un Administrador puede ser ninguna.
        /// </summary>
        public Task<IReadOnlyList<SavedReport>> ListForUserAsync(string userId)
        {
            var scope = _guard.Scope;
            IEnumerable<string> specialties = scope.IsUnrestricted ? null : scope.Codes;
            return _repo.ListForUserAsync(userId, specialties);
        }

        public Task<SavedReport> GetAsync(Guid id) => _repo.GetAsync(id);

        /// <summary>
        /// Lectura por id con el MISMO chequeo de acceso que ListForUserAsync (dueño, o
        /// scope='specialty' dentro de las especialidades del usuario). Antes GetAsync se
        /// exponía crudo en el controller: cualquiera con el GUID leía la consulta guardada
        /// de otra persona (List sí filtraba; Get, no).
        /// </summary>
        public async Task<SavedReportResult> GetForUserAsync(Guid id, string userId)
        {
            var r = await _repo.GetAsync(id);
            if (r == null) return SavedReportResult.NotFound();
            if (EsPropietario(r, userId)) return SavedReportResult.Ok(r);
            if (string.Equals(r.Scope, "specialty", StringComparison.OrdinalIgnoreCase)
                && _guard.Scope.Allows(r.SpecialtyCode)) return SavedReportResult.Ok(r);
            // No confirmar existencia de una consulta ajena: mismo criterio que el resto de
            // la app (una lectura por id sin acceso responde 404, no 403).
            return SavedReportResult.NotFound();
        }

        public async Task<SavedReport> CreateAsync(SavedReport nueva, string userId)
        {
            // SpecialtyCode es null para Scope="private" (no hay a qué especialidad
            // acotarlo); sólo se valida cuando efectivamente declara una.
            if (!string.IsNullOrEmpty(nueva.SpecialtyCode)) _guard.EnsureAllows(nueva.SpecialtyCode);

            nueva.Id = Guid.NewGuid();
            nueva.CreatedBy = userId;
            nueva.CreatedAt = DateTime.UtcNow;
            nueva.UpdatedAt = DateTime.UtcNow;
            await _repo.AddAsync(nueva);
            Auditar("create", nueva, userId);
            return nueva;
        }

        /// <summary>
        /// Actualización con merge campo a campo: lo que el cliente no manda se conserva. Un
        /// reemplazo completo convertiría cualquier omisión en un borrado silencioso.
        /// </summary>
        public async Task<SavedReportResult> UpdateAsync(Guid id, SavedReport patch, string userId)
        {
            var actual = await _repo.GetAsync(id);
            if (actual == null) return SavedReportResult.NotFound();
            if (!EsPropietario(actual, userId)) return SavedReportResult.Forbidden();

            var nuevaEspecialidad = patch.SpecialtyCode ?? actual.SpecialtyCode;
            if (!string.IsNullOrEmpty(nuevaEspecialidad)) _guard.EnsureAllows(nuevaEspecialidad);

            actual.Name = patch.Name ?? actual.Name;
            actual.Scope = patch.Scope ?? actual.Scope;
            actual.SpecialtyCode = nuevaEspecialidad;
            actual.QueryJson = patch.QueryJson ?? actual.QueryJson;
            // El cuestionario base acompaña a los filtros: si sólo se actualizara QueryJson,
            // la consulta quedaría con filtros de un cuestionario y el id de otro.
            if (patch.QuestionnaireId != Guid.Empty) actual.QuestionnaireId = patch.QuestionnaireId;
            actual.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(actual);
            Auditar("update", actual, userId);
            return SavedReportResult.Ok(actual);
        }

        public async Task<SavedReportResult> DeleteAsync(Guid id, string userId)
        {
            var actual = await _repo.GetAsync(id);
            if (actual == null) return SavedReportResult.NotFound();
            if (!EsPropietario(actual, userId)) return SavedReportResult.Forbidden();

            await _repo.DeleteAsync(id);
            Auditar("delete", actual, userId);
            return SavedReportResult.Ok(actual);
        }

        /// <summary>
        /// Registra el evento de auditoría de una consulta guardada, con una foto de su
        /// definición (nombre, alcance y filtros) para que el detalle sea legible aunque la
        /// consulta cambie o se borre después. Sin esto, guardar un reporte no dejaba rastro.
        /// </summary>
        private void Auditar(string action, SavedReport r, string userId)
        {
            var evt = AuditEventBuilder.For(action, "SavedReport/" + r.Id, userId, category: "report");
            evt.FhirJson = JsonConvert.SerializeObject(new SavedReportAuditSnapshot
            {
                Name = r.Name,
                Scope = r.Scope,
                SpecialtyCode = r.SpecialtyCode,
                QuestionnaireId = r.QuestionnaireId,
                QueryJson = r.QueryJson
            });
            _audit.Enqueue(evt);
        }

        /// <summary>
        /// Sólo quien la creó puede modificarla o eliminarla. Que una consulta sea visible
        /// para toda una especialidad (Scope) no la vuelve editable por cualquiera: son dos
        /// permisos distintos y confundirlos dejaría que alguien pise el trabajo ajeno.
        /// </summary>
        private static bool EsPropietario(SavedReport r, string userId)
            => !string.IsNullOrEmpty(userId)
            && string.Equals(r.CreatedBy, userId, StringComparison.Ordinal);
    }
}
