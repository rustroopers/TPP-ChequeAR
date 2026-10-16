using System.Threading.Tasks;

namespace ChequeAR.Application.Interfaces
{
    /// <summary>Comprueba que un servicio HTTP responda.</summary>
    public interface IHttpProbe
    {
        /// <summary>Lanza si no hay respuesta. Cualquier código HTTP cuenta como respuesta.</summary>
        Task PingAsync(string url);
    }
}
