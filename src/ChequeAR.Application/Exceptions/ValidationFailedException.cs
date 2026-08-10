using System;
using System.Collections.Generic;
using System.Linq;

namespace ChequeAR.Application.Exceptions
{
    /// <summary>
    /// La validación del recurso falló. Lleva las reglas incumplidas por separado, no
    /// concatenadas en un mensaje: la interfaz emite un OperationOutcome con UN issue por
    /// regla, que es la diferencia entre "arreglá esto" y "arreglá algo de esta lista".
    ///
    /// Hereda de InvalidOperationException para no romper a quien ya captura ese tipo — los
    /// controllers que todavía no distinguen el caso siguen respondiendo 422 como antes.
    /// </summary>
    public class ValidationFailedException : InvalidOperationException
    {
        public IReadOnlyList<string> Errors { get; }

        public ValidationFailedException(IEnumerable<string> errors)
            : base(Resumen(errors))
        {
            Errors = (errors ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        }

        private static string Resumen(IEnumerable<string> errors)
        {
            var lista = (errors ?? Enumerable.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            return lista.Count == 0
                ? "La validación falló sin detalle disponible."
                : string.Join(" ", lista);
        }
    }
}
