using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ChequeAR.Api.Fhir
{
    /// <summary>Lo que repetían los controllers FHIR: base del servidor, ETag e If-Match.</summary>
    public static class FhirHttp
    {
        public static string FhirBaseUrl(this HttpRequestMessage request)
            => request.RequestUri.GetLeftPart(UriPartial.Authority) + "/fhir/r5";

        /// <summary>Weak ETag: identifica la versión del recurso, no una representación byte a byte.</summary>
        public static EntityTagHeaderValue WeakETag(int version)
            => new EntityTagHeaderValue("\"" + version + "\"", true);

        /// <summary>La versión de If-Match (acepta W/"3" y "3"); null si no viene o no es un número.</summary>
        public static int? IfMatchVersion(this HttpRequestMessage request)
        {
            var tag = request.Headers.IfMatch?.FirstOrDefault();
            if (tag == null) return null;
            return int.TryParse((tag.Tag ?? string.Empty).Trim('"'), out var version) ? version : (int?)null;
        }
    }
}
