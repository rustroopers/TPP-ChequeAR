using System;
using System.Web.Mvc;

namespace ChequeAR.Api.Helpers
{
    /// <summary>
    /// Registro de las excepciones que una acción MVC atrapa para degradar la pantalla (mostrar
    /// "Ocurrió un error..." en vez de la página de error). El usuario ve un mensaje genérico;
    /// sin esto el detalle se perdía y no quedaba rastro de qué falló.
    /// </summary>
    public static class HandledErrors
    {
        public static void Log(Controller controller, Exception ex)
        {
            var route = controller?.ControllerContext?.RouteData?.Values;
            Serilog.Log.Error(ex, "Error manejado en {Controller}.{Action} ({Path})",
                route?["controller"], route?["action"], controller?.Request?.RawUrl);
        }
    }
}
