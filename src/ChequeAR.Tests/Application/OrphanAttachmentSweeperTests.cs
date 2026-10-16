using System;
using System.Threading;
using System.Threading.Tasks;
using ChequeAR.Application.Services;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class OrphanAttachmentSweeperTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Nunca = Timeout.InfiniteTimeSpan;

        [Fact]
        public async Task RunAsync_CortaConElMargen()
        {
            DateTime? corte = null;
            using (var sut = new OrphanAttachmentSweeper(c => { corte = c; return Task.FromResult(3); }, Nunca, Nunca, utcNow: () => Ahora))
            {
                Assert.Equal(3, await sut.RunAsync());
            }
            Assert.Equal(Ahora - OrphanAttachmentSweeper.Grace, corte);
        }

        [Fact]
        public async Task RunAsync_UnErrorNoSePropaga()
        {
            Exception reportado = null;
            using (var sut = new OrphanAttachmentSweeper(_ => throw new InvalidOperationException("sin base"), Nunca, Nunca,
                       onError: ex => reportado = ex))
            {
                Assert.Equal(0, await sut.RunAsync());
            }
            Assert.IsType<InvalidOperationException>(reportado);
        }

        [Fact]
        public async Task RunAsync_NoCorreDosVecesALaVez()
        {
            var liberar = new TaskCompletionSource<int>();
            var llamadas = 0;
            using (var sut = new OrphanAttachmentSweeper(_ => { Interlocked.Increment(ref llamadas); return liberar.Task; }, Nunca, Nunca))
            {
                var primera = sut.RunAsync();
                Assert.Equal(0, await sut.RunAsync());
                liberar.SetResult(1);
                Assert.Equal(1, await primera);
            }
            Assert.Equal(1, llamadas);
        }
    }
}
