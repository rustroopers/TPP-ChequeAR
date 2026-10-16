using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace ChequeAR.Api.Tests
{
    /// <summary>
    /// Los tests corren con los redirects que genera el SDK, no con los de Web.config. Si un
    /// paquete cambia de versión y el redirect queda viejo, CI pasa y la app no arranca en IIS.
    /// </summary>
    public class BindingRedirectTests
    {
        private static readonly XNamespace Asm = "urn:schemas-microsoft-com:asm.v1";

        [Fact]
        public void CadaRedirect_ApuntaALaVersionQueQuedaEnBin()
        {
            var api = ApiDirectory();
            var bin = Path.Combine(api, "bin");
            var redirects = XDocument.Load(Path.Combine(api, "Web.config"))
                .Descendants(Asm + "dependentAssembly")
                .Select(d => new
                {
                    Name = (string)d.Element(Asm + "assemblyIdentity")?.Attribute("name"),
                    Version = (string)d.Element(Asm + "bindingRedirect")?.Attribute("newVersion")
                })
                .Where(r => r.Name != null && r.Version != null)
                .ToList();
            Assert.NotEmpty(redirects);

            var enBin = redirects
                .Select(r => new { r.Name, r.Version, Dll = Path.Combine(bin, r.Name + ".dll") })
                .Where(r => File.Exists(r.Dll))
                .Select(r => new { r.Name, r.Version, Real = AssemblyName.GetAssemblyName(r.Dll).Version.ToString() })
                .ToList();
            Assert.True(enBin.Count >= redirects.Count / 2, "Sólo " + enBin.Count + " de " + redirects.Count + " ensamblados en " + bin);

            var distintos = enBin
                .Where(r => r.Real != r.Version)
                .Select(r => r.Name + " (redirect " + r.Version + ", bin " + r.Real + ")")
                .ToList();

            Assert.True(distintos.Count == 0, "Redirects que no coinciden con bin: " + string.Join(", ", distintos));
        }

        private static string ApiDirectory()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var api = Path.Combine(dir.FullName, "ChequeAR.Api");
                if (File.Exists(Path.Combine(api, "Web.config"))) return api;
            }
            throw new DirectoryNotFoundException("No se encontró ChequeAR.Api/Web.config");
        }
    }
}
