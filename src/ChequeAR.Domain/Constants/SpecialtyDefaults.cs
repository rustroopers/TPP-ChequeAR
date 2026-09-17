namespace ChequeAR.Domain.Constants
{
    /// <summary>
    /// Último recurso cuando el catálogo de especialidades (CHEQUEAR.Specialty) no se pudo leer
    /// (por ejemplo, Oracle caído al renderizar una vista). NO es la fuente de verdad: esa es la
    /// fila con IsDefault=true en el catálogo. Referenciar SOLO desde SpecialtyCatalog.
    /// </summary>
    public static class SpecialtyDefaults
    {
        public const string Code = "general";
        public const string Name = "General";
    }
}
