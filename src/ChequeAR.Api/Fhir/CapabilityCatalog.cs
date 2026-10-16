using System.Collections.Generic;

namespace ChequeAR.Api.Fhir
{
    /// Fuente unica de verdad del CapabilityStatement del server.
    ///
    /// La consumen dos lugares que antes tenian cada uno su copia literal:
    ///   - Controllers/Api/MetadataController  -> GET /fhir/r5/metadata (JSON FHIR)
    ///   - Controllers/Mvc/AdminController     -> pantalla F2 (vista HTML)
    ///
    /// Si se agrega un recurso o una operacion, se agrega aca y las dos salidas
    /// quedan sincronizadas solas.
    public static class CapabilityCatalog
    {
        public const string FhirVersion = "5.0.0";
        public const string SoftwareName = "ChequeAR";
        public const string SoftwareVersion = "0.1.0";

        public sealed class OperationCap
        {
            public string Name { get; set; }
            public string Definition { get; set; }
        }

        public sealed class SearchParamCap
        {
            public string Name { get; set; }
            public string Type { get; set; }
        }

        public sealed class ResourceCap
        {
            public string Type { get; set; }
            public IList<string> Interactions { get; set; } = new List<string>();
            public IList<OperationCap> Operations { get; set; } = new List<OperationCap>();
            public IList<SearchParamCap> SearchParams { get; set; } = new List<SearchParamCap>();

            /// <summary>
            /// Nota para quien integra. Se usa sobre todo para declarar explícitamente lo que
            /// NO se soporta: un recurso sin interacciones y con esta nota comunica una
            /// decisión; omitirlo del catálogo se leería como un olvido.
            /// </summary>
            public string Documentation { get; set; }
        }

        public static IReadOnlyList<ResourceCap> Resources { get; } = new List<ResourceCap>
        {
            new ResourceCap
            {
                Type = "Questionnaire",
                Interactions = new List<string> { "read", "search-type", "create", "update", "delete" },
                Operations = new List<OperationCap>
                {
                    new OperationCap { Name = "publish", Definition = "https://chequear.garrahan/OperationDefinition/publish" },
                    new OperationCap { Name = "archive", Definition = "https://chequear.garrahan/OperationDefinition/archive" },
                    new OperationCap { Name = "clone", Definition = "https://chequear.garrahan/OperationDefinition/clone" }
                },
                SearchParams = new List<SearchParamCap>
                {
                    new SearchParamCap { Name = "status", Type = "token" },
                    new SearchParamCap { Name = "title", Type = "string" },
                    new SearchParamCap { Name = "url", Type = "uri" },
                    new SearchParamCap { Name = "version", Type = "token" }
                },
                Documentation = "GET por id devuelve el borrador de trabajo; una versión publicada se lee con url y version."
            },
            new ResourceCap
            {
                Type = "QuestionnaireResponse",
                // Sin update: el guardado parcial recibe la forma interna, no un recurso ($save-progress).
                Interactions = new List<string> { "read", "search-type", "create" },
                Operations = new List<OperationCap>
                {
                    new OperationCap { Name = "lock", Definition = "https://chequear.garrahan/OperationDefinition/lock" },
                    new OperationCap { Name = "renew-lock", Definition = "https://chequear.garrahan/OperationDefinition/renew-lock" },
                    new OperationCap { Name = "unlock", Definition = "https://chequear.garrahan/OperationDefinition/unlock" },
                    new OperationCap { Name = "save-progress", Definition = "https://chequear.garrahan/OperationDefinition/save-progress" },
                    new OperationCap { Name = "finalize", Definition = "https://chequear.garrahan/OperationDefinition/finalize" },
                    new OperationCap { Name = "amend", Definition = "https://chequear.garrahan/OperationDefinition/amend" },
                    new OperationCap { Name = "amend-commit", Definition = "https://chequear.garrahan/OperationDefinition/amend-commit" },
                    new OperationCap { Name = "abandon", Definition = "https://chequear.garrahan/OperationDefinition/abandon" },
                    new OperationCap { Name = "retarget-preview", Definition = "https://chequear.garrahan/OperationDefinition/retarget-preview" },
                    new OperationCap { Name = "retarget-version", Definition = "https://chequear.garrahan/OperationDefinition/retarget-version" }
                },
                SearchParams = new List<SearchParamCap>
                {
                    new SearchParamCap { Name = "subject", Type = "reference" },
                    new SearchParamCap { Name = "questionnaire", Type = "reference" },
                    new SearchParamCap { Name = "status", Type = "token" },
                    new SearchParamCap { Name = "_summary", Type = "token" }
                }
            },
            new ResourceCap
            {
                Type = "AuditEvent",
                Interactions = new List<string> { "read", "search-type" },
                SearchParams = new List<SearchParamCap>
                {
                    new SearchParamCap { Name = "agent.who", Type = "reference" },
                    new SearchParamCap { Name = "entity.what", Type = "reference" }
                }
            },
            new ResourceCap
            {
                Type = "Patient",
                Interactions = new List<string> { "read", "search-type" },
                SearchParams = new List<SearchParamCap>
                {
                    new SearchParamCap { Name = "name", Type = "string" }
                },
                Documentation = "Sólo lectura. ChequeAR no posee la entidad: la deriva de la vista "
                              + "del hospital y, cuando esa fuente no responde, de sus propias fichas."
            },
            new ResourceCap
            {
                // Sin 'read': un encuentro derivado de una ficha no tiene identidad propia
                // estable que se pueda prometer como dirección individual.
                Type = "Encounter",
                Interactions = new List<string> { "search-type" },
                SearchParams = new List<SearchParamCap>
                {
                    new SearchParamCap { Name = "subject", Type = "reference" }
                },
                Documentation = "Sólo búsqueda por paciente. Derivado de las fichas cargadas; "
                              + "ChequeAR no registra episodios de atención."
            },
            new ResourceCap
            {
                // Declarado SIN interacciones a propósito: es la forma de decir "conozco este
                // tipo y no lo soporto". Omitirlo del catálogo se leería como un olvido.
                Type = "ValueSet",
                Operations = new List<OperationCap>
                {
                    new OperationCap { Name = "expand", Definition = "https://chequear.garrahan/OperationDefinition/expand" }
                },
                Documentation = "$expand sirve al autocompletado SNOMED del editor y devuelve un formato propio, no un ValueSet."
            },
            new ResourceCap
            {
                Type = "Observation",
                Documentation = "NO SOPORTADO. ChequeAR no produce observaciones; las consume del "
                              + "servidor FHIR del hospital al precargar datos de una ficha."
            }
        };
    }
}
