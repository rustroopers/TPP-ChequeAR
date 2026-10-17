using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>Tamaño de página cuando el cliente no manda <c>_count</c>.</summary>
        public const int DefaultCount = 50;

        /// <summary>
        /// Tope de <c>_count</c>: sin él, un <c>_count=100000</c> traía la tabla entera (con
        /// los recursos FHIR completos) en un solo pedido.
        /// </summary>
        public const int MaxCount = 200;

        /// <summary><c>_count</c> del cliente llevado al rango válido: sin valor o no positivo, el de omisión; nunca más que el tope.</summary>
        public static int ClampCount(int count) => count <= 0 ? DefaultCount : (count > MaxCount ? MaxCount : count);

        /// <summary><c>_offset</c> del cliente: nunca negativo.</summary>
        public static int ClampOffset(int offset) => offset < 0 ? 0 : offset;

        /// <param name="total">
        /// Total REAL de coincidencias, no el de la página. Es la diferencia entre "hay 3
        /// resultados" y "te estoy mostrando 3 de 240", y de eso depende toda paginación
        /// del lado del cliente. Null cuando no se conoce: FHIR lo define opcional, y omitirlo
        /// es preferible a informar el tamaño de la página como si fuera el total.
        /// </param>
        /// <param name="baseUrl">
        /// Base del servidor FHIR (ej: https://host/fhir/r5). Se usa para el fullUrl de cada
        /// entrada. Si viene vacía, las entradas salen sin fullUrl en vez de con una URL
        /// relativa inválida.
        /// </param>
        /// <param name="self">URL del pedido: con ella se arman los links self, next y previous.</param>
        /// <param name="hasMore">Sin total conocido, si hay resultados después de esta página.</param>
        public static Bundle SearchSet(IEnumerable<Resource> resources, int? total, string baseUrl = null,
            Uri self = null, int offset = 0, int count = 0, bool hasMore = false)
        {
            var items = (resources ?? Enumerable.Empty<Resource>()).Where(r => r != null).ToList();
            var root = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');

            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Searchset,
                Total = total,
                Entry = items.Select(r => new Bundle.EntryComponent
                {
                    FullUrl = FullUrlFor(root, r),
                    Resource = r,
                    Search = new Bundle.SearchComponent { Mode = Bundle.SearchEntryMode.Match }
                }).ToList()
            };

            if (self != null)
            {
                bundle.Link.Add(new Bundle.LinkComponent { Relation = "self", Url = self.ToString() });
                if (count > 0 && (total.HasValue ? offset + count < total.Value : hasMore))
                    bundle.Link.Add(new Bundle.LinkComponent { Relation = "next", Url = WithOffset(self, offset + count) });
                if (count > 0 && offset > 0)
                    bundle.Link.Add(new Bundle.LinkComponent { Relation = "previous", Url = WithOffset(self, Math.Max(0, offset - count)) });
            }
            return bundle;
        }

        private static readonly char[] Ampersand = { '&' };

        private static string WithOffset(Uri self, int offset)
        {
            var query = (self.Query ?? string.Empty).TrimStart('?')
                .Split(Ampersand, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("_offset=", StringComparison.OrdinalIgnoreCase))
                .Concat(new[] { "_offset=" + offset.ToString(CultureInfo.InvariantCulture) });
            return self.GetLeftPart(UriPartial.Path) + "?" + string.Join("&", query);
        }

        private static string FullUrlFor(string root, Resource resource)
        {
            if (root == null || string.IsNullOrEmpty(resource.Id)) return null;
            return root + "/" + resource.TypeName + "/" + resource.Id;
        }
    }
}
