using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Infrastructure.Oracle;
using Xunit;

namespace ChequeAR.Infrastructure.Tests
{
    public class MockOracleViewClientTests
    {
        private readonly MockOracleViewClient _sut = new MockOracleViewClient(new[] { "VW_PACIENTE" });

        [Fact]
        public async Task QueryAsync_ColumnaInexistente_Falla()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.QueryAsync("VW_PACIENTE",
                new Dictionary<string, object> { ["NO_EXISTE"] = "x" }));
        }

        [Fact]
        public async Task SearchAsync_ColumnaInexistente_Falla()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SearchAsync("VW_PACIENTE", new[] { "NO_EXISTE" }, "ana"));
        }

        [Fact]
        public async Task QueryAsync_ColumnaExistente_Filtra()
        {
            var rows = await _sut.QueryAsync("VW_PACIENTE", new Dictionary<string, object> { ["ID_PACIENTE"] = "PAC-000001" });

            Assert.Single(rows);
        }
    }
}
