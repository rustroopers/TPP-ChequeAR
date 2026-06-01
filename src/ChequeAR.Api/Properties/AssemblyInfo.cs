using System.Reflection;
using System.Runtime.InteropServices;
using System.Web;

[assembly: AssemblyTitle("ChequeAR.Api")]
[assembly: AssemblyProduct("ChequeAR")]
[assembly: ComVisible(false)]
[assembly: Guid("a1a1a1a1-0000-0000-0000-000000000001")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: PreApplicationStartMethod(typeof(ChequeAR.Api.PreStart), "Start")]

namespace ChequeAR.Api
{
    public static class PreStart
    {
        public static void Start() { }
    }
}
