using System;
using System.Linq;
using ChequeAR.Application.Services;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class UserMirrorDiffTests
    {
        [Fact]
        public void Especialidad_Nueva_SeAgrega()
        {
            var diff = UserMirrorDiff.Compute(
                currentRoles: Array.Empty<string>(), incomingRoles: Array.Empty<string>(),
                currentSpecialties: Array.Empty<string>(), incomingSpecialties: new[] { "cardio" });

            Assert.Equal(new[] { "cardio" }, diff.SpecialtiesToAdd);
            Assert.Empty(diff.SpecialtiesToRemove);
        }

        [Fact]
        public void Especialidad_Quitada_SeDaDeBaja()
        {
            // La revocación que UpsertAsync nunca hacía: si el hospital ya no informa una
            // especialidad, tiene que desaparecer del espejo.
            var diff = UserMirrorDiff.Compute(
                currentRoles: Array.Empty<string>(), incomingRoles: Array.Empty<string>(),
                currentSpecialties: new[] { "cardio", "onco" }, incomingSpecialties: new[] { "cardio" });

            Assert.Equal(new[] { "onco" }, diff.SpecialtiesToRemove);
            Assert.Empty(diff.SpecialtiesToAdd);
        }

        [Fact]
        public void IncomingNull_NoTocaElEspejo()
        {
            // Respuesta parcial/degradada del hospital: no se debe interpretar como revocación.
            var diff = UserMirrorDiff.Compute(
                currentRoles: new[] { "Medico" }, incomingRoles: null,
                currentSpecialties: new[] { "cardio" }, incomingSpecialties: null);

            Assert.True(diff.IsNoOp);
            Assert.Empty(diff.SpecialtiesToAdd);
            Assert.Empty(diff.SpecialtiesToRemove);
            Assert.Empty(diff.RolesToAdd);
            Assert.Empty(diff.RolesToRemove);
        }

        [Fact]
        public void IncomingVacio_BorraTodo_RevocacionExplicita()
        {
            var diff = UserMirrorDiff.Compute(
                currentRoles: Array.Empty<string>(), incomingRoles: Array.Empty<string>(),
                currentSpecialties: new[] { "cardio", "onco" }, incomingSpecialties: Array.Empty<string>());

            Assert.Equal(2, diff.SpecialtiesToRemove.Count);
            Assert.True(diff.SpecialtiesToRemove.Contains("cardio"));
            Assert.True(diff.SpecialtiesToRemove.Contains("onco"));
        }

        [Fact]
        public void SinCambios_EsNoOp()
        {
            var diff = UserMirrorDiff.Compute(
                currentRoles: new[] { "Medico" }, incomingRoles: new[] { "Medico" },
                currentSpecialties: new[] { "cardio" }, incomingSpecialties: new[] { "cardio" });

            Assert.True(diff.IsNoOp);
        }

        [Fact]
        public void CaseInsensitive_NoGeneraBorrarMasInsertarEspurio()
        {
            var diff = UserMirrorDiff.Compute(
                currentRoles: Array.Empty<string>(), incomingRoles: Array.Empty<string>(),
                currentSpecialties: new[] { "cardio" }, incomingSpecialties: new[] { "Cardio" });

            Assert.True(diff.IsNoOp);
        }

        [Fact]
        public void Roles_MismosCasosQueEspecialidades()
        {
            var diff = UserMirrorDiff.Compute(
                currentRoles: new[] { "Medico" }, incomingRoles: new[] { "Medico", "Jefe" },
                currentSpecialties: Array.Empty<string>(), incomingSpecialties: Array.Empty<string>());

            Assert.Equal(new[] { "Jefe" }, diff.RolesToAdd);
            Assert.Empty(diff.RolesToRemove);
        }
    }
}
