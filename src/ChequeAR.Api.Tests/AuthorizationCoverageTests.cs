using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ChequeAR.Api.Tests
{
    /// <summary>
    /// Ni la API ni MVC tienen un filtro de autorización global: cada acción declara quién
    /// puede llamarla. Una acción nueva sin atributo quedaría abierta sin que nadie lo note.
    /// </summary>
    public class AuthorizationCoverageTests
    {
        private static readonly Assembly Api = typeof(Controllers.Api.HealthController).Assembly;

        [Fact]
        public void TodaAccionDeLaApi_DeclaraSuAutorizacion()
        {
            var sinDeclarar = Actions(typeof(System.Web.Http.ApiController), m => m.GetCustomAttribute<System.Web.Http.NonActionAttribute>() == null)
                .Where(m => !Declares(m, typeof(System.Web.Http.AuthorizeAttribute), typeof(System.Web.Http.AllowAnonymousAttribute)))
                .Select(Name).ToList();

            Assert.True(sinDeclarar.Count == 0, "Sin [Authorize], [RequiresPermissionApi] ni [AllowAnonymous]: " + string.Join(", ", sinDeclarar));
        }

        [Fact]
        public void TodaAccionMvc_DeclaraSuAutorizacion()
        {
            var sinDeclarar = Actions(typeof(System.Web.Mvc.Controller), m => m.GetCustomAttribute<System.Web.Mvc.NonActionAttribute>() == null)
                .Where(m => !Declares(m, typeof(System.Web.Mvc.AuthorizeAttribute), typeof(System.Web.Mvc.AllowAnonymousAttribute)))
                .Select(Name).ToList();

            Assert.True(sinDeclarar.Count == 0, "Sin [Authorize], [RequiresPermission] ni [AllowAnonymous]: " + string.Join(", ", sinDeclarar));
        }

        [Fact]
        public void HayAccionesParaRevisar()
        {
            Assert.True(Actions(typeof(System.Web.Http.ApiController), _ => true).Count() > 20);
            Assert.True(Actions(typeof(System.Web.Mvc.Controller), _ => true).Count() > 20);
        }

        private static IEnumerable<MethodInfo> Actions(Type baseType, Func<MethodInfo, bool> isAction) =>
            Api.GetTypes()
                .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition && isAction(m));

        private static bool Declares(MethodInfo m, params Type[] attributes) =>
            attributes.Any(a => m.IsDefined(a, true) || m.DeclaringType.IsDefined(a, true));

        private static string Name(MethodInfo m) => m.DeclaringType.Name + "." + m.Name;
    }
}
