using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Lectura tolerante de los recursos guardados en la base.
    ///
    /// Comparte criterio con <see cref="FhirQuestionnaireReader"/>: permisivo al leer lo que
    /// ya está escrito, porque hay contenido anterior a la feature 009 con vocabulario R4 y
    /// es inmutable. Un recurso ilegible devuelve null en vez de propagar la excepción: una
    /// fila corrupta debe degradar esa entrada, no tumbar una búsqueda entera.
    /// </summary>
    public static class FhirResourceReader
    {
        private static readonly FhirJsonParser Parser = new FhirJsonParser(new ParserSettings
        {
            AcceptUnknownMembers = true,
            AllowUnrecognizedEnums = true,
            PermissiveParsing = true
        });

        public static QuestionnaireResponse ReadResponse(string fhirJson)
            => Read<QuestionnaireResponse>(fhirJson);

        private static T Read<T>(string fhirJson) where T : Resource
        {
            if (string.IsNullOrWhiteSpace(fhirJson)) return null;
            try { return Parser.Parse<T>(fhirJson); }
            catch (Exception) { return null; }
        }
    }
}
