using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// Una regla de negocio impide la operación en el estado actual del recurso (la ficha ya
    /// no está en curso, el cuestionario está archivado...). La API la traduce a 422 con el
    /// mensaje, que está pensado para mostrarse al usuario.
    ///
    /// Existe para distinguirla de una InvalidOperationException cualquiera (un bug, una
    /// secuencia vacía): esa tiene que seguir siendo un 500 logueado, sin exponer su mensaje.
    /// Hereda de InvalidOperationException para no romper a quien ya captura ese tipo.
    /// </summary>
    public class BusinessRuleException : InvalidOperationException
    {
        public BusinessRuleException(string message) : base(message) { }
    }
}
