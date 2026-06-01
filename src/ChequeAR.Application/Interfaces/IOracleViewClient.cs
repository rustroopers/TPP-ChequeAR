using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChequeAR.Application.Interfaces
{
    public interface IOracleViewClient
    {
        Task<IReadOnlyList<IDictionary<string, object>>> QueryAsync(
            string viewName,
            IReadOnlyDictionary<string, object> filters);
    }
}
