using System;
using System.Collections.Generic;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Nombre legible de una especialidad a partir de su código. El editor guarda sólo el
    /// código (SpecialtyDisplay queda null), así que sin esto la UI mostraba "general" o
    /// "cardio" en crudo donde debería decir "General" o "Cardiología".
    ///
    /// Vive en Application y no en la capa web porque no sólo la usan los controllers:
    /// PatientService también arma un label de especialidad, y tener dos tablas de nombres
    /// terminaba mostrando el código crudo en las pantallas que pasan por ahí.
    ///
    /// Provisorio: cuando el hospital defina su catálogo, esto sale de VW_ESPECIALIDAD.
    /// </summary>
    public static class SpecialtyMap
    {
        private static readonly Dictionary<string, string> Labels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "general", "General" },
                { "cardio",  "Cardiología" },
                { "neumo",   "Neumonología" },
                { "onco",    "Oncología" },
                { "reuma",   "Reumatología" }
            };

        /// <param name="display">Nombre que ya venga de la base; tiene prioridad.</param>
        public static string Label(string code, string display = null)
        {
            if (!string.IsNullOrWhiteSpace(display)) return display;
            if (string.IsNullOrWhiteSpace(code)) return "General";
            return Labels.TryGetValue(code.Trim(), out var label) ? label : code;
        }
    }
}
