using System.Linq;
using System.Web.Http.ModelBinding;

namespace ChequeAR.Api.Fhir
{
    public static class FhirModelState
    {
        /// <summary>Lo que el formatter no pudo leer del cuerpo, para el diagnostics del 400.</summary>
        public static string Describe(ModelStateDictionary state)
        {
            var errores = state.Values.SelectMany(v => v.Errors)
                .Select(e => !string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.ErrorMessage : e.Exception?.Message)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct().Take(5).ToList();
            return errores.Count == 0 ? "El recurso enviado no es válido." : "El recurso enviado no es válido: " + string.Join(" ", errores);
        }
    }
}
