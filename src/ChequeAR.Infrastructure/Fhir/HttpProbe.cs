using System;
using System.Net.Http;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Fhir
{
    public class HttpProbe : IHttpProbe
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        public async Task PingAsync(string url)
        {
            using (await Http.GetAsync(url).ConfigureAwait(false)) { }
        }
    }
}
