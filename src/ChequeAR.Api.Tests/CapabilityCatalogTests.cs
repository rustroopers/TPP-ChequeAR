using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using ChequeAR.Api.Fhir;
using Xunit;

namespace ChequeAR.Api.Tests
{
    /// <summary>
    /// El CapabilityStatement es lo que lee un cliente FHIR para saber qué hay. Una operación
    /// nueva que no se declara no existe para él, y una declarada que se borró lo hace fallar.
    /// </summary>
    public class CapabilityCatalogTests
    {
        [Fact]
        public void LasOperacionesDeLasRutasYDelCatalogoCoinciden()
        {
            var enRutas = OperacionesEnRutas();
            var enCatalogo = new HashSet<string>(CapabilityCatalog.Resources
                .SelectMany(r => r.Operations.Select(o => r.Type + "/$" + o.Name)));

            Assert.True(enRutas.Count > 10);
            Assert.Empty(enRutas.Except(enCatalogo));
            Assert.Empty(enCatalogo.Except(enRutas));
        }

        [Fact]
        public void QuestionnaireResponse_SinUpdate()
        {
            var qr = CapabilityCatalog.Resources.Single(r => r.Type == "QuestionnaireResponse");
            Assert.DoesNotContain("update", qr.Interactions);
        }

        private static HashSet<string> OperacionesEnRutas()
        {
            var result = new HashSet<string>();
            var api = typeof(Controllers.Api.HealthController).Assembly;
            foreach (var type in api.GetTypes().Where(t => typeof(ApiController).IsAssignableFrom(t)))
            {
                var prefix = type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix ?? "";
                if (!prefix.StartsWith("fhir/r5/", System.StringComparison.Ordinal)) continue;
                var recurso = prefix.Substring("fhir/r5/".Length);
                foreach (var route in type.GetMethods().SelectMany(m => m.GetCustomAttributes<RouteAttribute>()))
                {
                    var i = route.Template.IndexOf('$');
                    if (i >= 0) result.Add(recurso + "/" + route.Template.Substring(i));
                }
            }
            return result;
        }
    }
}
