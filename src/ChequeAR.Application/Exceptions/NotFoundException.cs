using System;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// El recurso pedido no existe. La API lo traduce a 404.
    ///
    /// Hereda de InvalidOperationException, como ValidationFailedException, para no romper a
    /// quien ya captura ese tipo (las pantallas MVC que muestran el mensaje en un toast).
    /// </summary>
    public class NotFoundException : InvalidOperationException
    {
        public NotFoundException(string message) : base(message) { }
    }
}
