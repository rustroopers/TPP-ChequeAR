using System;
using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    /// <summary>
    /// DTOs de transporte para la evolución longitudinal de parámetros de un paciente
    /// (spec 010). No hay entidades de dominio nuevas: esta feature es solo lectura sobre
    /// datos ya persistidos (Question / QuestionnaireResponse / vw_FlatResponses). Estos
    /// tipos únicamente arman la forma que consume la isla Vue `patient-trend`.
    /// </summary>

    /// <summary>Un parámetro seleccionable del selector de la pantalla Trend.</summary>
    public class TrendParameterOptionDto
    {
        public string LinkId { get; set; }
        public string Text { get; set; }
        /// <summary>"decimal" | "integer" | "date" (en minúscula, para el frontend).</summary>
        public string Type { get; set; }
        /// <summary>Unidad declarada en ValidationJson.unit, si la hay.</summary>
        public string Unit { get; set; }
        /// <summary>null si la pregunta no tiene min/max configurado.</summary>
        public NormalRangeDto NormalRange { get; set; }
    }

    /// <summary>Respuesta del endpoint de parámetros: título del cuestionario + su lista de
    /// parámetros graficables.</summary>
    public class TrendParametersResult
    {
        public Guid QuestionnaireId { get; set; }
        public string QuestionnaireTitle { get; set; }
        public List<TrendParameterOptionDto> Parameters { get; set; } = new List<TrendParameterOptionDto>();
    }

    /// <summary>Franja de referencia (rango normal) de una pregunta. Cualquiera de los dos
    /// extremos puede venir en null: el rango puede estar abierto de un lado.</summary>
    public class NormalRangeDto
    {
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
    }

    /// <summary>Una serie completa (un gráfico) para un parámetro seleccionado.</summary>
    public class TrendSeriesDto
    {
        public string LinkId { get; set; }
        public string Text { get; set; }
        /// <summary>"decimal" | "integer" | "date". El frontend lo usa para formatear el eje Y
        /// (los valores de tipo date viajan como epoch en milisegundos, ver TrendPointDto.Value).</summary>
        public string Type { get; set; }
        public string Unit { get; set; }
        public NormalRangeDto NormalRange { get; set; }
        /// <summary>Ordenados cronológicamente. Vacío (no null) si no hay datos: el frontend
        /// distingue "sin datos" de "error" (FR-011).</summary>
        public List<TrendPointDto> Points { get; set; } = new List<TrendPointDto>();
    }

    /// <summary>Un punto del gráfico: el valor de un parámetro en una ficha finalizada.</summary>
    public class TrendPointDto
    {
        /// <summary>Ficha de origen del punto — habilita, a futuro, un link a la ficha completa.</summary>
        public Guid ResponseId { get; set; }
        /// <summary>
        /// Día en que se finalizó la ficha, en hora de Argentina ("yyyy-MM-dd"); eje X del
        /// gráfico. Va como texto para que el navegador no lo corra según su zona horaria.
        /// </summary>
        public string Date { get; set; }
        /// <summary>Valor numérico. Para preguntas de tipo Date se transporta como epoch en
        /// milisegundos de la fecha a medianoche UTC, y el frontend la formatea en UTC.</summary>
        public decimal Value { get; set; }
    }
}
