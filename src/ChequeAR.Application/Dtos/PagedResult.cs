using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    public class PagedResult<T>
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public IReadOnlyList<T> Items { get; set; }
    }
}
