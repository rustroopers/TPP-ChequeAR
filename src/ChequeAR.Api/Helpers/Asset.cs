using System;
using System.Collections.Concurrent;
using System.IO;
using System.Web;
using System.Web.Hosting;

namespace ChequeAR.Api.Helpers
{
    /// Versionado de archivos estáticos (cache busting).
    ///
    /// Problema: las islas se sirven como `~/Scripts/islands/<isla>/index.js`, una URL fija.
    /// IIS responde con ETag y Last-Modified pero SIN Cache-Control, y ante esa combinación
    /// el navegador aplica frescura heurística (una fracción de la antigüedad del archivo):
    /// puede servir la copia vieja sin siquiera preguntar. Editar el .js y recargar no
    /// alcanza -- hay que forzar recarga dura, y en producción el usuario ni eso.
    ///
    /// Solución: colgar de la URL la marca de tiempo del archivo. Cambia el contenido =>
    /// cambia la URL => es otro recurso para el cache, y se pide de nuevo. Mientras no
    /// cambie, la URL es estable y el cache sigue sirviendo (que es lo que queremos).
    ///
    ///     &lt;script src="@Asset.Url("~/Scripts/islands/questionnaire-editor/index.js")"&gt;&lt;/script&gt;
    public static class Asset
    {
        private static readonly ConcurrentDictionary<string, string> _cache
            = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// URL absoluta del recurso con `?v=<ticks de la última escritura>`.
        ///
        /// En debug se recalcula en cada request: durante el desarrollo el archivo cambia
        /// todo el tiempo y cachear la versión sería volver al problema original. Fuera de
        /// debug se resuelve una sola vez por path (los archivos no cambian en caliente).
        public static string Url(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath)) return virtualPath;

            var debug = HttpContext.Current != null && HttpContext.Current.IsDebuggingEnabled;
            if (debug) return Construir(virtualPath);
            return _cache.GetOrAdd(virtualPath, Construir);
        }

        private static string Construir(string virtualPath)
        {
            var absoluta = VirtualPathUtility.ToAbsolute(virtualPath);
            try
            {
                var fisico = HostingEnvironment.MapPath(virtualPath);
                if (fisico == null || !File.Exists(fisico)) return absoluta;
                var marca = File.GetLastWriteTimeUtc(fisico).Ticks;
                return absoluta + (absoluta.Contains("?") ? "&" : "?") + "v=" + marca;
            }
            catch (Exception)
            {
                // Un fallo acá (permisos, path raro) no puede tumbar la página: sin versión
                // el recurso se sigue sirviendo, sólo se vuelve al riesgo de cache viejo.
                return absoluta;
            }
        }
    }
}
