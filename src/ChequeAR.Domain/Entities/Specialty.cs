namespace ChequeAR.Domain.Entities
{
    /// <summary>
    /// Catálogo de especialidades médicas. Única fuente de verdad: SpecialtyMap (código),
    /// los &lt;option&gt; hardcodeados del editor y las clases CSS por código quedan reemplazados
    /// por esta tabla (feature 011-especialidades).
    /// </summary>
    public class Specialty
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }

        /// <summary>
        /// Nombre de la variable de zona del tema (red|violet|green|yellow|blue|orange), sin el
        /// prefijo --cq-zone-. Null => color derivado por hash del código (ver SpecialtyCatalog.Color).
        /// </summary>
        public string Color { get; set; }

        /// <summary>Orden de presentación en selects y filtros. Menor primero; empate => Name.</summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Especialidad que se asume cuando hace falta un código y no hay uno más específico
        /// (fallback de lectura, nunca de escritura: un cuestionario siempre debe declarar la suya).
        /// Exactamente una fila en true; lo garantiza el seed, no la base.
        /// </summary>
        public bool IsDefault { get; set; }

        // Nota (fuera de alcance de esta feature): el hospital tiene su propio vocabulario de
        // especialidades (PED/NEO/CARP/... en VW_ESPECIALIDAD / MockOracleViewClient), que
        // ChequeAR no consulta hoy. El día que haga falta puentear ambos, el lugar natural es
        // una columna HospitalCode acá (o una tabla SpecialtyAlias aparte) — no agregarla sin
        // un consumidor real, para no repetir el error de esta misma tabla antes de esta feature.
    }
}
