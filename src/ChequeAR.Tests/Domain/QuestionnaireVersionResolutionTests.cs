using System;
using ChequeAR.Domain.Entities;
using Xunit;

namespace ChequeAR.Tests.Domain
{
    /// <summary>
    /// Qué versión publicada corresponde en cada caso. Estaba repetido en el servicio de
    /// respuestas y en dos controllers.
    /// </summary>
    public class QuestionnaireVersionResolutionTests
    {
        private static readonly Guid V1 = Guid.NewGuid(), V2 = Guid.NewGuid(), V3 = Guid.NewGuid();

        private static Questionnaire ConVersiones(Guid? currentId)
        {
            var q = new Questionnaire { CurrentVersionId = currentId };
            q.Versions.Add(new QuestionnaireVersion { Id = V1, VersionNumber = 1 });
            q.Versions.Add(new QuestionnaireVersion { Id = V3, VersionNumber = 3 });
            q.Versions.Add(new QuestionnaireVersion { Id = V2, VersionNumber = 2 });
            return q;
        }

        [Fact]
        public void CurrentVersion_EsLaQueApuntaCurrentVersionId()
        {
            Assert.Equal(2, ConVersiones(V2).CurrentVersion().VersionNumber);
        }

        [Fact]
        public void CurrentVersion_ConPunteroColgado_CaeALaMasAlta()
        {
            Assert.Equal(3, ConVersiones(Guid.NewGuid()).CurrentVersion().VersionNumber);
            Assert.Equal(3, ConVersiones(null).CurrentVersion().VersionNumber);
        }

        [Fact]
        public void CurrentVersion_SinPublicar_EsNull()
        {
            Assert.Null(new Questionnaire().CurrentVersion());
        }

        [Fact]
        public void FindVersion_IdVacioODesconocido_EsNull()
        {
            var q = ConVersiones(V2);
            Assert.Null(q.FindVersion(Guid.Empty));
            Assert.Null(q.FindVersion(Guid.NewGuid()));
            Assert.Equal(1, q.FindVersion(V1).VersionNumber);
        }

        [Fact]
        public void VersionOrCurrent_RespetaLaAncladaAunqueNoSeaLaVigente()
        {
            Assert.Equal(1, ConVersiones(V3).VersionOrCurrent(V1).VersionNumber);
        }

        [Fact]
        public void VersionOrCurrent_SinAncla_UsaLaVigente()
        {
            Assert.Equal(3, ConVersiones(V3).VersionOrCurrent(Guid.Empty).VersionNumber);
        }
    }
}
