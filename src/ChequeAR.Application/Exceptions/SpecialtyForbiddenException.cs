using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// El usuario actual no tiene la especialidad del recurso dentro de su scope. La API la
    /// traduce a 404 en lecturas por id (no confirmar existencia de un recurso ajeno) y a 403
    /// en escrituras/operaciones (ahí el id ya pasó por [Authorize(Roles=...)], así que negar
    /// no revela nada que el actor no supiera).
    /// </summary>
    public class SpecialtyForbiddenException : Exception
    {
        public SpecialtyForbiddenException(string specialtyCode)
            : base("No tiene acceso a la especialidad '" + (specialtyCode ?? "(sin especialidad)") + "'.")
        {
            SpecialtyCode = specialtyCode;
        }

        public string SpecialtyCode { get; }
    }
}
