using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using ChequeAR.Infrastructure.EF;

namespace ChequeAR.Infrastructure.Migrations
{
    /// <summary>
    /// Termina lo que empieza la migración RemoveVariableVersions, que deja las versiones de
    /// variables con nombres legacy. Hace falta C# para armar el snapshot de las preguntas:
    ///  1. Una publicada queda con la definición de su versión vigente: un borrador viejo con
    ///     cambios clínicos sin publicar se descarta, como ya mostraba su pantalla.
    ///  2. Las preguntas de borradores armadas con otra versión toman la definición vigente, y
    ///     su cuestionario queda con cambios sin publicar. Las versiones publicadas del
    ///     cuestionario no cambian.
    ///  3. Borra la tabla y las columnas legacy.
    ///
    /// Si algo falla no borra nada, y vuelve a intentarlo en el próximo arranque.
    /// </summary>
    internal static class VariableVersionRemoval
    {
        private const string LegacyTable = "CVV_Legacy";

        private sealed class LegacyDefinition
        {
            public byte[] VariableId { get; set; }
            public string DefinitionJson { get; set; }
        }

        public static int Run(ChequeARDbContext context)
        {
            var pendiente = context.Database.SqlQuery<int>(
                "select count(*) from user_tables where table_name = '" + LegacyTable + "'").Single() > 0;
            if (!pendiente) return 0;

            var definiciones = context.Database.SqlQuery<LegacyDefinition>(
                @"select v.""Id"" as ""VariableId"", l.""DefinitionJson"" as ""DefinitionJson""
                    from ""ClinicalVariable"" v join """ + LegacyTable + @""" l on l.""Id"" = v.""LegacyCurrentVersionId""").ToList();
            foreach (var d in definiciones)
            {
                var publicada = VariableDefinition.Parse(d.DefinitionJson);
                var v = context.ClinicalVariables.Include(x => x.Options).Single(x => x.Id == new Guid(d.VariableId));
                if (publicada == null || publicada.ClinicallySameAs(VariableDefinition.From(v))) continue;
                ApplyClinical(context, v, publicada);
            }
            context.SaveChanges();

            var desactualizadas = context.Database.SqlQuery<byte[]>(
                @"select q.""Id"" from ""Question"" q join ""ClinicalVariable"" v on v.""Id"" = q.""VariableId""
                   where v.""PublishedAt"" is not null
                     and (q.""LegacyVariableVersionId"" is null or q.""LegacyVariableVersionId"" <> v.""LegacyCurrentVersionId"")")
                .Select(b => new Guid(b)).ToList();
            foreach (var id in desactualizadas)
            {
                var q = context.Questions.Include(x => x.Options).Single(x => x.Id == id);
                var v = context.ClinicalVariables.Include(x => x.Options).Single(x => x.Id == q.VariableId);
                context.AnswerOptions.RemoveRange(q.Options.ToList());
                ClinicalVariableSnapshot.Apply(q, v);
                foreach (var o in q.Options) context.AnswerOptions.Add(o);

                var cuestionario = context.Questionnaires.Single(x => x.Id == q.QuestionnaireId);
                if (cuestionario.CurrentVersionId.HasValue) cuestionario.HasPendingChanges = true;
                cuestionario.ConcurrencyVersion++;
                cuestionario.UpdatedAt = DateTime.UtcNow;
            }
            context.SaveChanges();

            context.Database.ExecuteSqlCommand(@"alter table ""Question"" drop column ""LegacyVariableVersionId"" cascade constraints");
            context.Database.ExecuteSqlCommand(@"alter table ""ClinicalVariable"" drop column ""LegacyCurrentVersionId"" cascade constraints");
            context.Database.ExecuteSqlCommand(@"drop table """ + LegacyTable + @""" cascade constraints purge");
            return desactualizadas.Count;
        }

        private static void ApplyClinical(ChequeARDbContext context, ClinicalVariable v, VariableDefinition d)
        {
            v.Name = d.Name;
            v.Type = d.Type;
            v.RepeatsAllowed = d.RepeatsAllowed;
            v.Unit = d.Unit;
            v.MinValue = d.MinValue;
            v.MaxValue = d.MaxValue;
            v.MaxLength = d.MaxLength;
            v.Accept = d.Accept;
            v.SnomedSystem = d.SnomedSystem;
            v.SnomedCode = d.SnomedCode;
            v.SnomedDisplay = d.SnomedDisplay;
            v.Term = d.Term;
            v.Revision++;

            context.VariableOptions.RemoveRange(v.Options.ToList());
            var i = 0;
            foreach (var o in d.Options ?? new List<VariableDefinitionOption>())
                context.VariableOptions.Add(new VariableOption
                {
                    Id = Guid.NewGuid(),
                    VariableId = v.Id,
                    Value = o.Value,
                    Display = o.Display,
                    Code = o.Code,
                    SnomedSystem = o.SnomedSystem,
                    SnomedCode = o.SnomedCode,
                    SnomedDisplay = o.SnomedDisplay,
                    OrderIndex = i++
                });
        }
    }
}
