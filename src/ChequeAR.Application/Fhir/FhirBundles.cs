using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;

namespace ChequeAR.Application.Fhir
{
    /// <summary>
    /// Armado de Bundles de búsqueda conformes.
    ///
    /// Los controllers devolvían <c>{ resourceType: "Bundle", entry: [entidades] }</c>: tenía
    /// el nombre correcto pero no la estructura. FHIR exige que cada resultado venga envuelto
    /// en <c>entry[].resource</c> y acompañado de su <c>fullUrl</c>; un cliente estándar itera
    /// esperando eso y con la forma vieja no encuentra nada.
    /// </summary>
    public static class FhirBundles
    {
        /// <param name="total">
        /// Total REAL de coincidencias, no el de la página. Es la diferencia entre "hay 3
        /// resultados" y "te estoy mostrando 3 de 240", y de eso depende toda paginación
        /// del lado del cliente.
        /// </param>
        /// <param name="baseUrl">
        /// Base del servidor FHIR (ej: https://host/fhir/r5). Se usa para el fullUrl de cada
        /// entrada. Si viene vacía, las entradas salen sin fullUrl en vez de con una URL
        /// relativa inválida.
        /// </param>
        public static Bundle SearchSet(IEnumerable<Resource> resources, int total, string baseUrl = null)
        {
            var items = (resources ?? Enumerable.Empty<Resource>()).Where(r => r != null).ToList();
            var root = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');

            return new Bundle
            {
                Type = Bundle.BundleType.Searchset,
                Total = total,
                Entry = items.Select(r => new Bundle.EntryComponent
                {
                    FullUrl = FullUrlFor(root, r),
                    Resource = r
                }).ToList()
            };
        }

        private static string FullUrlFor(string root, Resource resource)
        {
            if (root == null || string.IsNullOrEmpty(resource.Id)) return null;
            return root + "/" + resource.TypeName + "/" + resource.Id;
        }
    }
}
