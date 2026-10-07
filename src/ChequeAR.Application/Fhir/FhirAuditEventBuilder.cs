using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using DomainAuditEvent = ChequeAR.Domain.Entities.AuditEvent;
using FhirAuditEvent = Hl7.Fhir.Model.AuditEvent;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Proyección de un evento de auditoría al recurso AuditEvent de R5. El dominio guarda
    /// quién, qué, cuándo y sobre qué recurso; FHIR los ubica en agent.who, code/action,
    /// recorded y entity.what.
    ///
    /// R5 exige code, recorded, al menos un agent con who, y source.observer: un evento sin
    /// alguno de ellos no valida y un consumidor externo lo rechaza. Lo que el dominio no
    /// tiene se declara ausente (data-absent-reason) en vez de inventarse: en auditoría un
    /// dato ausente es honesto, uno equivocado no.
    /// </summary>
    public static class FhirAuditEventBuilder
    {
        public const string UserSystem = "https://chequear.garrahan/user";
        /// <summary>Identificador de lo auditado cuando no es un recurso FHIR (un reporte, una exportación).</summary>
        public const string EntitySystem = "https://chequear.garrahan/audit-entity";

        private const string RestfulInteraction = "http://hl7.org/fhir/restful-interaction";
        private const string AuditEventType = "http://terminology.hl7.org/CodeSystem/audit-event-type";
        private const string Dicom = "http://dicom.nema.org/resources/ontology/DCM";
        private const string IssueSeverity = "http://hl7.org/fhir/issue-severity";
        private const string DataAbsentReason = "http://hl7.org/fhir/StructureDefinition/data-absent-reason";

        // Los únicos tipos de EntityRef que este servidor expone como recurso FHIR leíble.
        private static readonly HashSet<string> TiposFhir = new HashSet<string>(StringComparer.Ordinal)
        {
            "Questionnaire", "QuestionnaireResponse", "Patient"
        };

        public static FhirAuditEvent Build(DomainAuditEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            Partir(e.EntityRef, out var recurso, out var operacion);

            var evento = new FhirAuditEvent
            {
                Id = e.Id.ToString(),
                // Lo guardado es UTC; se marca explícito para que no lo reinterprete como local.
                Recorded = new DateTimeOffset(DateTime.SpecifyKind(e.Recorded, DateTimeKind.Utc)),
                Action = ParseAction(e.Action),
                Code = Codigo(e.Action, recurso, operacion),
                Source = new FhirAuditEvent.SourceComponent
                {
                    Observer = new ResourceReference { Type = "Device", Display = "ChequeAR" }
                }
            };

            var categoria = Categoria(e.Category);
            if (categoria != null) evento.Category.Add(categoria);

            var resultado = Resultado(e.Outcome);
            if (resultado != null) evento.Outcome = new FhirAuditEvent.OutcomeComponent { Code = resultado };

            evento.Agent.Add(new FhirAuditEvent.AgentComponent { Who = Quien(e.AgentUserId), Requestor = true });

            if (!string.IsNullOrWhiteSpace(e.EntityRef))
                evento.Entity.Add(new FhirAuditEvent.EntityComponent { What = Que(e.EntityRef, recurso) });

            return evento;
        }

        /// <summary>
        /// Separa "Tipo/{id}$operación" o "Tipo/acción?parámetros" en el recurso ("Tipo/{id}")
        /// y la operación ("operación"); sin sufijo, operación queda null.
        /// </summary>
        private static void Partir(string entityRef, out string recurso, out string operacion)
        {
            recurso = entityRef; operacion = null;
            if (string.IsNullOrWhiteSpace(entityRef)) { recurso = null; return; }
            var dolar = entityRef.IndexOf('$');
            if (dolar < 0) return;
            recurso = entityRef.Substring(0, dolar);
            operacion = entityRef.Substring(dolar + 1);
        }

        private static CodeableConcept Codigo(string action, string recurso, string operacion)
        {
            // Una operación con nombre ($publish, $finalize…) es más precisa que el C/R/U/D.
            if (!string.IsNullOrEmpty(operacion))
                return Concepto(RestfulInteraction, "operation", "$" + operacion);

            switch ((action ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "C": return Concepto(RestfulInteraction, "create", null);
                case "R": return Concepto(RestfulInteraction, "read", null);
                case "U": return Concepto(RestfulInteraction, "update", null);
                case "D": return Concepto(RestfulInteraction, "delete", null);
                // "Report/export?…": lo que se ejecutó es lo que va antes de los parámetros.
                case "E": return Concepto(RestfulInteraction, "operation", SinParametros(recurso));
                // code es obligatorio; sin acción registrada no hay código que afirmar.
                default: return new CodeableConcept { Text = "Acción no registrada" };
            }
        }

        private static CodeableConcept Categoria(string category)
        {
            switch ((category ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "rest": return Concepto(AuditEventType, "rest", null);
                case "export": return Concepto(Dicom, "110106", null);   // Export
                case "report": return Concepto(Dicom, "110112", null);   // Query
                default: return null;
            }
        }

        /// <summary>El dominio guarda el resultado con los códigos de R4 (0/4/8/12); R5 usa la severidad.</summary>
        private static Coding Resultado(string outcome)
        {
            switch ((outcome ?? string.Empty).Trim())
            {
                case "0": return new Coding(IssueSeverity, "success");
                case "4": return new Coding(IssueSeverity, "warning");
                case "8": return new Coding(IssueSeverity, "error");
                case "12": return new Coding(IssueSeverity, "fatal");
                default: return null;
            }
        }

        private static ResourceReference Quien(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                // agent.who es obligatorio: se declara que no se sabe, no se inventa un usuario.
                var desconocido = new ResourceReference();
                desconocido.AddExtension(DataAbsentReason, new Code("unknown"));
                return desconocido;
            }
            return new ResourceReference
            {
                Identifier = new Identifier(UserSystem, userId),
                Display = userId
            };
        }

        private static ResourceReference Que(string entityRef, string recurso)
        {
            var barra = recurso.IndexOf('/');
            var tipo = barra > 0 ? recurso.Substring(0, barra) : null;
            var id = barra > 0 ? recurso.Substring(barra + 1) : null;

            // Una referencia literal sólo si apunta a algo que se puede leer por esa URL; el
            // sufijo de operación no forma parte de la referencia.
            if (tipo != null && TiposFhir.Contains(tipo) && Id.IsValidValue(id))
                return new ResourceReference { Reference = recurso, Type = tipo };

            return new ResourceReference { Identifier = new Identifier(EntitySystem, entityRef) };
        }

        private static string SinParametros(string recurso)
        {
            if (string.IsNullOrEmpty(recurso)) return null;
            var pregunta = recurso.IndexOf('?');
            return pregunta >= 0 ? recurso.Substring(0, pregunta) : recurso;
        }

        private static CodeableConcept Concepto(string system, string code, string text)
            => new CodeableConcept { Coding = { new Coding(system, code) }, Text = text };

        /// <summary>
        /// El dominio ya guarda la acción con el vocabulario del estándar (C/R/U/D/E). Lo que
        /// no matchee queda sin action antes que inventar un valor.
        /// </summary>
        private static FhirAuditEvent.AuditEventAction? ParseAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return null;
            return EnumUtility.ParseLiteral<FhirAuditEvent.AuditEventAction>(action.Trim().ToUpperInvariant());
        }
    }
}
