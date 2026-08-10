using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChequeAR.Application.Fhir;
using ChequeAR.Application.Interfaces;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using FhirQuestionnaire = Hl7.Fhir.Model.Questionnaire;
using FhirResponse = Hl7.Fhir.Model.QuestionnaireResponse;

namespace ChequeAR.Infrastructure.Fhir
{
    /// <summary>
    /// Validación real de recursos antes de congelar una versión o finalizar una ficha.
    ///
    /// Reemplaza al sustituto que sólo verificaba que el contenido incluyera la cadena
    /// "resourceType". Ese sustituto no era inofensivo: dejó pasar 9 versiones publicadas con
    /// vocabulario R4 mientras el sistema declaraba conformidad 5.0.0 (research.md R8).
    ///
    /// ── Por qué NO usa el validador de perfiles de Firely ──────────────────────────────
    /// El motor que interpreta StructureDefinition se separó del SDK a los paquetes
    /// Firely.Fhir.Validation.*, que exigen netstandard2.1. Sobre .NET Framework 4.7.1 no hay
    /// ninguna versión utilizable — ver research.md (R9).
    ///
    /// Este adaptador aporta lo que sí está disponible y sí es infraestructura:
    ///
    ///   1. Parseo ESTRICTO contra R5 con el SDK, que verifica tipos, cardinalidades y
    ///      vocabularios cerrados. No es poco: es exactamente lo que detectó que el sistema
    ///      venía emitiendo 'choice' en lugar de 'coding'.
    ///   2. La carga de los artefactos de conformidad publicados (el ValueSet de tipos).
    ///
    /// Las REGLAS del perfil viven en ChequeAR.Application.Fhir.ProfileRules, porque son
    /// decisiones del dominio y así se pueden probar sin levantar infraestructura.
    ///
    /// DEBE registrarse como instancia única: el resolver lee y cachea toda la especificación.
    /// </summary>
    public class FhirValidatorService : IFhirValidator, IDisposable
    {
        private const string ItemTypeValueSetUrl =
            "https://chequear.garrahan/ValueSet/ChequeARQuestionnaireItemType";

        private readonly FhirJsonParser _estricto = new FhirJsonParser();
        private readonly IResourceResolver _resolver;
        private readonly HashSet<string> _tiposPermitidos;

        /// <param name="profilesDirectory">
        /// Directorio con los artefactos de conformidad propios. Si no existe se valida sólo
        /// contra la especificación base y las reglas por omisión: es preferible a no validar
        /// nada, y el arranque no se cae por un archivo faltante.
        /// </param>
        public FhirValidatorService(string profilesDirectory = null)
        {
            var fuentes = new List<IResourceResolver>();

            if (!string.IsNullOrWhiteSpace(profilesDirectory) && Directory.Exists(profilesDirectory))
            {
                fuentes.Add(new CachedResolver(new DirectorySource(profilesDirectory,
                    new DirectorySourceSettings { IncludeSubDirectories = true })));
            }

            var spec = UbicarEspecificacion();
            if (spec != null) fuentes.Add(new CachedResolver(new ZipSource(spec)));

            _resolver = fuentes.Count > 0
                ? new CachedResolver(new MultiResolver(fuentes))
                : null;

            _tiposPermitidos = CargarTiposPermitidos();
        }

        /// <summary>
        /// Ubica specification.zip explícitamente en vez de usar
        /// <c>ZipSource.CreateValidationSource()</c>, que lo busca relativo al directorio de
        /// trabajo del proceso. Bajo IIS ese directorio NO es bin/, así que el helper tira
        /// "specification.zip was not found" y —al construirse el validador en
        /// Application_Start— se llevaba puesta la aplicación entera al arrancar.
        ///
        /// Devuelve null si no aparece: sin la especificación se pierde la resolución de
        /// artefactos, pero el parseo estricto y las reglas del perfil siguen funcionando.
        /// Degradar es mejor que no arrancar.
        /// </summary>
        private static string UbicarEspecificacion()
        {
            var raiz = AppDomain.CurrentDomain.BaseDirectory ?? "";
            var candidatos = new[]
            {
                Path.Combine(raiz, "specification.zip"),            // consola / pruebas
                Path.Combine(raiz, "bin", "specification.zip"),     // aplicación web
                Path.Combine(Directory.GetCurrentDirectory(), "specification.zip")
            };
            return candidatos.FirstOrDefault(File.Exists);
        }

        /// <summary>
        /// Los tipos soportados salen del ValueSet publicado, no de una lista en código: el
        /// artefacto es la fuente de verdad, que es justamente lo que exige tener perfiles
        /// versionados y no constantes.
        /// </summary>
        private HashSet<string> CargarTiposPermitidos()
        {
            if (_resolver == null) return new HashSet<string>(ProfileRules.TiposPorOmision);
            try
            {
                var vs = _resolver.ResolveByCanonicalUri(ItemTypeValueSetUrl) as ValueSet;
                var codigos = vs?.Compose?.Include?
                    .SelectMany(i => i.Concept ?? new List<ValueSet.ConceptReferenceComponent>())
                    .Select(c => c.Code)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                if (codigos != null && codigos.Count > 0) return new HashSet<string>(codigos);
            }
            catch (Exception) { /* artefacto ausente o ilegible: se usa el respaldo */ }

            return new HashSet<string>(ProfileRules.TiposPorOmision);
        }

        public ValidationResult Validate(string fhirJson, string profileUrl = null)
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return Fallo("El recurso está vacío.");

            Resource recurso;
            try
            {
                // ESTRICTO a propósito: acá se decide si algo se publica. La tolerancia con el
                // vocabulario viejo vive en la LECTURA (FhirQuestionnaireReader), no acá.
                recurso = _estricto.Parse<Resource>(fhirJson);
            }
            catch (Exception ex)
            {
                // Un recurso que ni siquiera se puede leer no llega a las reglas del perfil:
                // el mensaje del parser es más útil que "no cumple el perfil".
                return Fallo("No cumple la especificación FHIR R5: " + Corto(ex.Message));
            }
            if (recurso == null) return Fallo("El recurso no se pudo interpretar como FHIR.");

            var errores = new List<string>();
            var avisos = new List<string>();

            if (recurso is FhirQuestionnaire q)
                ProfileRules.CheckQuestionnaire(q, errores, avisos, _tiposPermitidos);
            else if (recurso is FhirResponse r)
                ProfileRules.CheckResponse(r, errores);

            return new ValidationResult { IsValid = errores.Count == 0, Errors = errores, Warnings = avisos };
        }

        private static ValidationResult Fallo(string mensaje) => new ValidationResult
        {
            IsValid = false,
            Errors = new List<string> { mensaje },
            Warnings = new List<string>()
        };

        private static string Corto(string s)
            => string.IsNullOrEmpty(s) || s.Length <= 220 ? s : s.Substring(0, 220);

        /// <summary>
        /// Calentamiento al arrancar: la primera resolución paga la carga de la especificación
        /// completa. Sin esto, la primera publicación del día se lleva ese costo y parece que
        /// el sistema se colgó.
        /// </summary>
        public void Warmup()
        {
            if (_resolver == null) return;
            try { _resolver.ResolveByCanonicalUri("http://hl7.org/fhir/StructureDefinition/Questionnaire"); }
            catch (Exception) { /* es una optimización: si falla, sólo se paga después */ }
        }

        public void Dispose() => (_resolver as IDisposable)?.Dispose();
    }
}
