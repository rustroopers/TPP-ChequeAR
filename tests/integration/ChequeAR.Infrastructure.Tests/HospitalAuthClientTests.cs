using ChequeAR.Infrastructure.Auth;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    public class HospitalAuthClientTests
    {
        [Fact]
        public void Parse_RespuestaCompleta()
        {
            var u = HospitalAuthClient.Parse(@"{""id"":""m.perez"",""displayName"":""María \""Pérez\"""",""roles"":[""Jefe""],""specialties"":[""cardio"",""nefro""]}");

            Assert.Equal("m.perez", u.Id);
            Assert.Equal("María \"Pérez\"", u.DisplayName);
            Assert.Equal(new[] { "Jefe" }, u.Roles);
            Assert.Equal(new[] { "cardio", "nefro" }, u.Specialties);
        }

        // Antes un id numérico tomaba la siguiente cadena del documento: todos colisionaban.
        [Fact]
        public void Parse_IdNumerico()
        {
            Assert.Equal("1234", HospitalAuthClient.Parse(@"{""id"":1234,""displayName"":""X"",""roles"":[""Medico""]}").Id);
        }

        [Theory]
        [InlineData(@"{""displayName"":""X"",""roles"":[""Medico""]}")]
        [InlineData(@"{""id"":""a"",""displayName"":""X""}")]
        [InlineData(@"{""id"":""a"",""roles"":null,""specialties"":[""cardio""]}")]
        [InlineData(@"{""id"":""a"",""roles"":[]}")]
        [InlineData(@"<html>error</html>")]
        public void Parse_SinIdORoles_NoHaySesion(string body)
        {
            Assert.Null(HospitalAuthClient.Parse(body));
        }
    }
}
