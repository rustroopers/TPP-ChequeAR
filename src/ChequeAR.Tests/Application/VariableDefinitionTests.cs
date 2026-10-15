using System;
using System.Collections.Generic;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Domain.Enums;
using Xunit;

namespace ChequeAR.Tests.Application
{
    public class VariableDefinitionTests
    {
        private static readonly Guid Id = Guid.NewGuid();

        private static ClinicalVariable Severidad(decimal? max = null) => new ClinicalVariable
        {
            Id = Id, Code = "severidad", Name = "Severidad", DefaultText = "Severidad del cuadro",
            Type = QuestionType.Choice, MaxValue = max, SnomedSystem = "http://snomed.info/sct", SnomedCode = "272141005",
            Options =
            {
                new VariableOption { Value = "leve", Display = "Leve", Code = "255604002", SnomedCode = "255604002", OrderIndex = 0 },
                new VariableOption { Value = "moderada", Display = "Moderada", Code = "6736007", SnomedCode = "6736007", OrderIndex = 1 }
            }
        };

        [Fact]
        public void IdaYVuelta_ConservaLaDefinicion()
        {
            var original = VariableDefinition.From(Severidad());

            var leida = VariableDefinition.Parse(original.ToJson());

            Assert.True(original.SameAs(leida));
            Assert.Equal(2, leida.Options.Count);
            Assert.Equal("6736007", leida.Options[1].Code);
        }

        // Un decimal leído de la base trae otra escala: no es un cambio.
        [Fact]
        public void SameAs_IgnoraLaEscalaDeLosDecimales()
        {
            Assert.True(VariableDefinition.From(Severidad(300m)).SameAs(VariableDefinition.From(Severidad(300.0000m))));
            Assert.False(VariableDefinition.From(Severidad(300m)).SameAs(VariableDefinition.From(Severidad(301m))));
        }

        [Fact]
        public void SameAs_DetectaUnaOpcionNueva()
        {
            var con = Severidad();
            con.Options.Add(new VariableOption { Value = "severa", Display = "Severa", Code = "24484000", OrderIndex = 2 });

            Assert.False(VariableDefinition.From(Severidad()).SameAs(VariableDefinition.From(con)));
        }

        [Fact]
        public void Matches_PreguntaArmadaConLaMismaDefinicion()
        {
            var v = Severidad(300.0000m);
            var q = new Question { Id = Guid.NewGuid() };
            ClinicalVariableSnapshot.Apply(q, Severidad(300m));

            Assert.True(VariableDefinition.From(v).Matches(q));
        }

        [Fact]
        public void Matches_PreguntaConOtraDefinicion()
        {
            var q = new Question { Id = Guid.NewGuid() };
            ClinicalVariableSnapshot.Apply(q, Severidad());
            q.Options = new List<AnswerOption> { new AnswerOption { Value = "leve", Display = "Leve", Code = "255604002", SnomedCode = "255604002" } };

            Assert.False(VariableDefinition.From(Severidad()).Matches(q));
        }
    }
}
