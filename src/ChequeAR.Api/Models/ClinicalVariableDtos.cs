using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Application.Services;
using ChequeAR.Domain.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ChequeAR.Api.Models
{
    /// <summary>
    /// Forma con la que una variable viaja por HTTP.
    ///
    /// DTO explícito y no la entidad: Revision, IsActive y los campos de auditoría los decide
    /// el servidor, y aceptarlos del cliente permitiría, por ejemplo, congelar la revisión para
    /// que ningún editor se entere de que la definición cambió. También mantiene el contrato
    /// estable si mañana la entidad gana una columna interna.
    ///
    /// El camelCase va declarado en la clase y no en un resolver global: el JsonFormatter de
    /// esta Api no tiene ninguno -el resto de los endpoints devuelven objetos anónimos ya
    /// escritos en minúscula- y ponerlo global cambiaría de golpe el contrato de api/users y
    /// api/specialties. Sin esto el JSON sale en PascalCase y las islas leen undefined.
    /// </summary>
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class ClinicalVariableDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string DefaultText { get; set; }
        public string Description { get; set; }

        /// <summary>El vocabulario del editor ("decimal", "openChoice"), no el número del enum.</summary>
        public string Type { get; set; }

        public bool RepeatsAllowed { get; set; }
        public string Unit { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public int? MaxLength { get; set; }
        public string Accept { get; set; }

        public string SnomedSystem { get; set; }
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }
        public string Term { get; set; }

        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public int Revision { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Cuándo se publicó; null si nunca. Sólo de salida.</summary>
        public DateTime? PublishedAt { get; set; }
        public bool IsPublished => PublishedAt.HasValue;

        /// <summary>Variable de la que se creó esta; sólo se fija al crearla.</summary>
        public Guid? ReplacesId { get; set; }
        /// <summary>Nombre de esa variable. Sólo de salida, para el formulario.</summary>
        public string ReplacesName { get; set; }
        /// <summary>Si al publicarla se dio de baja la que reemplaza. Sólo de salida: se elige al publicar.</summary>
        public bool DeactivateReplacedOnPublish { get; set; }

        public List<VariableOptionDto> Options { get; set; } = new List<VariableOptionDto>();
    }

    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class VariableOptionDto
    {
        public string Value { get; set; }
        public string Display { get; set; }
        public string Code { get; set; }
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }
    }

    /// <summary>Traducción entidad &lt;-&gt; DTO. Un solo lugar para las dos direcciones.</summary>
    public static class ClinicalVariableDtoMap
    {
        public static ClinicalVariableDto ToDto(ClinicalVariable v)
        {
            if (v == null) return null;
            return new ClinicalVariableDto
            {
                Id = v.Id,
                Code = v.Code,
                Name = v.Name,
                DefaultText = v.DefaultText,
                Description = v.Description,
                Type = QuestionTypeMap.ToUi(v.Type),
                RepeatsAllowed = v.RepeatsAllowed,
                Unit = v.Unit,
                Min = v.MinValue,
                Max = v.MaxValue,
                MaxLength = v.MaxLength,
                Accept = v.Accept,
                SnomedSystem = v.SnomedSystem,
                SnomedCode = v.SnomedCode,
                SnomedDisplay = v.SnomedDisplay,
                Term = v.Term,
                IsActive = v.IsActive,
                SortOrder = v.SortOrder,
                Revision = v.Revision,
                UpdatedAt = v.UpdatedAt,
                PublishedAt = v.PublishedAt,
                ReplacesId = v.ReplacesId,
                DeactivateReplacedOnPublish = v.DeactivateReplacedOnPublish,
                Options = (v.Options ?? new List<VariableOption>())
                    .OrderBy(o => o.OrderIndex)
                    .Select(o => new VariableOptionDto
                    {
                        Value = o.Value,
                        Display = o.Display,
                        Code = o.Code,
                        SnomedCode = o.SnomedCode,
                        SnomedDisplay = o.SnomedDisplay
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// No copia Id, IsActive, Revision ni auditoría: los pone el servicio. Un tipo que no
        /// reconoce se rechaza (QuestionTypeMap.FromUi): un 422 con el detalle, no un 500 ni
        /// una variable de otro tipo.
        /// </summary>
        public static ClinicalVariable ToEntity(ClinicalVariableDto d)
        {
            if (d == null) return null;
            var v = new ClinicalVariable
            {
                Code = d.Code,
                Name = d.Name,
                DefaultText = d.DefaultText,
                Description = d.Description,
                Type = QuestionTypeMap.FromUi(d.Type),
                RepeatsAllowed = d.RepeatsAllowed,
                Unit = d.Unit,
                MinValue = d.Min,
                MaxValue = d.Max,
                MaxLength = d.MaxLength,
                Accept = d.Accept,
                SnomedSystem = d.SnomedSystem,
                SnomedCode = d.SnomedCode,
                SnomedDisplay = d.SnomedDisplay,
                Term = d.Term,
                SortOrder = d.SortOrder,
                ReplacesId = d.ReplacesId
            };
            foreach (var o in d.Options ?? new List<VariableOptionDto>())
                v.Options.Add(new VariableOption
                {
                    Value = o.Value,
                    Display = o.Display,
                    Code = o.Code,
                    SnomedCode = o.SnomedCode,
                    SnomedDisplay = o.SnomedDisplay
                });
            return v;
        }
    }
}
