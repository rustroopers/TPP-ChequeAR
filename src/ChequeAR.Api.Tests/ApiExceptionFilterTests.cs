using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using ChequeAR.Api.Filters;
using ChequeAR.Application.Exceptions;
using Xunit;

namespace ChequeAR.Api.Tests
{
    public class ApiExceptionFilterTests
    {
        public static IEnumerable<object[]> Casos() => new[]
        {
            new object[] { new PermissionForbiddenException("x"), 403 },
            new object[] { new SpecialtyForbiddenException("cardio"), 403 },
            new object[] { new NotFoundException("x"), 404 },
            new object[] { new ValidationFailedException(new List<string> { "a", "b" }), 422 },
            new object[] { new LockConflictException("otro", DateTime.UtcNow), 409 },
            new object[] { new ConcurrencyConflictException(1, 2), 409 },
            new object[] { new BusinessRuleException("x"), 422 },
        };

        [Theory]
        [MemberData(nameof(Casos))]
        public void ExcepcionDeApplication_CodigoHttp(Exception ex, int codigo)
        {
            var response = ApiExceptionFilter.ToResponse(Request(), ex);

            Assert.Equal((HttpStatusCode)codigo, response.StatusCode);
            Assert.True(ApiExceptionFilter.IsClientError(ex));
        }

        public static IEnumerable<object[]> Dependencias() => new[]
        {
            new object[] { new TerminologyException(503, "Snowstorm no responde"), 503 },
            new object[] { new TerminologyException(429, "Demasiados pedidos", retryAfterSeconds: 10), 429 },
        };

        [Theory]
        [MemberData(nameof(Dependencias))]
        public void DependenciaCaida_CodigoHttp(Exception ex, int codigo)
        {
            var response = ApiExceptionFilter.ToResponse(Request(), ex);

            Assert.Equal((HttpStatusCode)codigo, response.StatusCode);
            Assert.Equal(codigo < 500, ApiExceptionFilter.IsClientError(ex));
        }

        // Una excepción cualquiera puede ser un bug: sigue siendo un 500 sin detalles.
        [Fact]
        public void OtraExcepcion_NoSeTraduce()
        {
            var ex = new InvalidOperationException("detalle interno");

            Assert.Null(ApiExceptionFilter.ToResponse(Request(), ex));
            Assert.False(ApiExceptionFilter.IsClientError(ex));
        }

        private static HttpRequestMessage Request()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/x");
            request.SetConfiguration(new HttpConfiguration());
            return request;
        }
    }
}
