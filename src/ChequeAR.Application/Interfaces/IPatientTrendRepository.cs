using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ChequeAR.Application.Dtos;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>
    /// Acceso a los puntos de evolución de un paciente, acotado a esta feature (spec 010).
    ///
    /// Devuelve una serie por cada linkId que tenga al menos un valor cargado, con sus puntos
    /// ya ordenados cronológicamente. Cada serie trae únicamente LinkId + Points: la metadata
    /// de presentación (texto, tipo, unidad, rango normal) la agrega PatientTrendService desde
    /// la definición del cuestionario, no la base de respuestas.
    ///
    /// Un linkId sin datos simplemente no aparece en el resultado; es el servicio el que se
    /// encarga de completar la respuesta con una serie vacía para no perder ese parámetro.
    /// </summary>
    public interface IPatientTrendRepository
    {
        Task<List<TrendSeriesDto>> GetSeriesAsync(string patientHospitalId, Guid questionnaireId, IEnumerable<string> linkIds);
    }
}
