using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;

// OJO: acá NO va "using Hl7.Fhir.Model". FHIR define un recurso Task (el de "tarea
// pendiente" clínica), que colisiona con System.Threading.Tasks.Task y vuelve ambigua
// toda firma async. Los dos tipos del modelo que se usan acá van calificados.

namespace ChequeAR.Api.Fhir
{
    /// <summary>
    /// Serializa y deserializa recursos FHIR con el SDK de Firely.
    ///
    /// NO es opcional: los POCOs de Firely tienen un modelo de serialización propio
    /// (elecciones de tipo value[x], primitivos con extensiones, elementos con "_" prefijado)
    /// y pasarlos por el serializador JSON genérico produce contenido inválido. Hasta la
    /// feature 009 el proyecto sólo agregaba "application/fhir+json" a la lista de tipos
    /// aceptados del serializador genérico: por eso el tipo de contenido decía FHIR pero el
    /// cuerpo era la representación interna.
    ///
    /// Se registra PRIMERO en la cadena de formatters y declara también "application/json",
    /// para que el formato del estándar sea el comportamiento por omisión y no algo que el
    /// consumidor tenga que pedir (FR-002). CanWriteType lo acota a Resource, así que los
    /// endpoints que no son FHIR (reportes, usuarios, adjuntos) siguen cayendo en el
    /// serializador de siempre.
    /// </summary>
    public class FhirMediaTypeFormatter : MediaTypeFormatter
    {
        public const string FhirJson = "application/fhir+json";

        // Los serializadores de Firely no tienen estado mutable: una instancia alcanza.
        private static readonly FhirJsonSerializer Serializer = new FhirJsonSerializer();
        private static readonly FhirJsonParser Parser = new FhirJsonParser();

        public FhirMediaTypeFormatter()
        {
            // El ORDEN importa: sin Accept explícito, Web API negocia contra el primer tipo
            // soportado del primer formatter que sepa escribir. fhir+json va primero.
            SupportedMediaTypes.Add(new MediaTypeHeaderValue(FhirJson));
            SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/json"));
            SupportedEncodings.Add(new UTF8Encoding(false, true));
            // _format de FHIR: sólo JSON. Un valor que no sea JSON queda sin formatter (406).
            foreach (var valor in new[] { "json", FhirJson, "application/json" })
                MediaTypeMappings.Add(new QueryStringMapping("_format", valor, FhirJson));
        }

        public override bool CanReadType(Type type) => typeof(Hl7.Fhir.Model.Resource).IsAssignableFrom(type);

        public override bool CanWriteType(Type type) => typeof(Hl7.Fhir.Model.Resource).IsAssignableFrom(type);

        /// <summary>
        /// Estampa siempre application/fhir+json al escribir un recurso, incluso si el cliente
        /// pidió application/json. El cuerpo es FHIR en ambos casos; declararlo como JSON
        /// genérico sería describirlo mal, y FR-002 pide que el formato del estándar sea el
        /// que se entrega.
        /// </summary>
        public override void SetDefaultContentHeaders(Type type, HttpContentHeaders headers, MediaTypeHeaderValue mediaType)
        {
            base.SetDefaultContentHeaders(type, headers, mediaType);
            if (CanWriteType(type))
                headers.ContentType = new MediaTypeHeaderValue(FhirJson) { CharSet = "utf-8" };
        }

        public override Task<object> ReadFromStreamAsync(Type type, Stream readStream, HttpContent content, IFormatterLogger formatterLogger)
        {
            return Task.Run<object>(() =>
            {
                try
                {
                    // No se libera el stream: lo administra Web API.
                    var reader = new StreamReader(readStream, new UTF8Encoding(false, true), true, 4096, leaveOpen: true);
                    var json = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(json)) return null;
                    return Parser.Parse(json, type);
                }
                catch (Exception ex)
                {
                    // Se registra y se devuelve null en vez de propagar: un cuerpo ilegible es
                    // un error del cliente (400), no del servidor. Los controllers ya chequean
                    // null y responden BadRequest con OperationOutcome.
                    formatterLogger?.LogError(string.Empty, ex);
                    return null;
                }
            });
        }

        public override Task WriteToStreamAsync(Type type, object value, Stream writeStream, HttpContent content, TransportContext transportContext)
        {
            return Task.Run(() =>
            {
                if (!(value is Hl7.Fhir.Model.Base resource)) return;
                var json = Serializer.SerializeToString(resource);
                var bytes = new UTF8Encoding(false).GetBytes(json);
                writeStream.Write(bytes, 0, bytes.Length);
                writeStream.Flush();
            });
        }
    }
}
