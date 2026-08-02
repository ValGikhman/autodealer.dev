using Services;
using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web.Http;
using System.Text.RegularExpressions;
using autodealer.dev.Models;

namespace autodealer.dev.Controllers {

    [RoutePrefix("api/service")]
    public class DecoderController : ApiController {

        public IVinDecoderService VinDecoderService { get; set; }
        public readonly string dataOneApiKey = ConfigurationManager.AppSettings["DataOne:AccessKey"];
        public readonly string dataOneSecretApiKey = ConfigurationManager.AppSettings["DataOne:SecretAccessKey"];

        public DecoderController(IVinDecoderService vinDecoderService) {
            VinDecoderService = vinDecoderService;
        }

        [HttpGet, Route("{vin:regex(^[A-HJ-NPR-Z0-9]{17}$)}")]
        public IHttpActionResult Get([FromUri] string vin) {
            if (string.IsNullOrWhiteSpace(vin))
                return BadRequest("VIN is required.");

            if (VinDecoderService == null)
                return InternalServerError(new InvalidOperationException("VinDecoderService is not configured."));

            var details = VinDecoderService.DecodeVin(vin, dataOneApiKey, dataOneSecretApiKey);
            return Ok(details);
        }

        /// <summary>
        /// Decodes a VIN with DataOne and returns an embeddable, self-styled HTML fragment.
        /// </summary>
        [HttpGet, Route("vin/{vin}/html")]
        public IHttpActionResult GetHtml([FromUri] string vin) {
            if (VinDecoderService == null)
                return InternalServerError(new InvalidOperationException("VinDecoderService is not configured."));

            try {
                var normalizedVin = (vin ?? string.Empty).Trim().ToUpperInvariant();
                if (!Regex.IsMatch(normalizedVin, "^[A-HJ-NPR-Z0-9]{17}$"))
                    return BadRequest("VIN must contain exactly 17 letters or digits and cannot contain I, O, or Q.");

                var dataOneXml = VinDecoderService.DecodeVin(normalizedVin, dataOneApiKey, dataOneSecretApiKey);
                var html = VinDecodeHtmlRenderer.Render(dataOneXml, normalizedVin);
                var response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(html, Encoding.UTF8, "text/html")
                };
                response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                return ResponseMessage(response);
            }
            catch (VinDecodeResponseException ex) {
                return Content((HttpStatusCode)422, new { message = ex.Message });
            }
            catch (Exception) {
                return Content(HttpStatusCode.BadGateway, new { message = "The VIN decoder is temporarily unavailable." });
            }
        }

        /// <summary>
        /// Decodes a VIN and returns the complete DataOne response as JSON.
        /// </summary>
        [HttpGet, Route("vin/{vin}/json")]
        public IHttpActionResult GetJson([FromUri] string vin) {
            return GetStructuredResponse(vin, "json");
        }

        /// <summary>
        /// Decodes a VIN and returns the complete DataOne response as XML.
        /// </summary>
        [HttpGet, Route("vin/{vin}/xml")]
        public IHttpActionResult GetXml([FromUri] string vin) {
            return GetStructuredResponse(vin, "xml");
        }

        private IHttpActionResult GetStructuredResponse(string vin, string format) {
            if (VinDecoderService == null)
                return CreateStructuredError(format, HttpStatusCode.InternalServerError, "VinDecoderService is not configured.");

            try {
                var normalizedVin = (vin ?? string.Empty).Trim().ToUpperInvariant();
                if (!Regex.IsMatch(normalizedVin, "^[A-HJ-NPR-Z0-9]{17}$"))
                    return CreateStructuredError(format, HttpStatusCode.BadRequest, "VIN must contain exactly 17 letters or digits and cannot contain I, O, or Q.");

                var dataOneXml = VinDecoderService.DecodeVin(normalizedVin, dataOneApiKey, dataOneSecretApiKey);
                var result = VinDecodeApiResponse.Create(normalizedVin, dataOneXml);
                var isJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
                var body = isJson
                    ? result.ToJsonObject().ToString(Newtonsoft.Json.Formatting.Indented)
                    : result.ToXmlDocument().ToString();
                var response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(body, Encoding.UTF8, isJson ? "application/json" : "application/xml")
                };
                response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
                return ResponseMessage(response);
            }
            catch (VinDecodeResponseException ex) {
                return CreateStructuredError(format, (HttpStatusCode)422, ex.Message);
            }
            catch (Exception) {
                return CreateStructuredError(format, HttpStatusCode.BadGateway, "The VIN decoder is temporarily unavailable.");
            }
        }

        private IHttpActionResult CreateStructuredError(string format, HttpStatusCode statusCode, string message) {
            var isJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            var body = isJson
                ? new Newtonsoft.Json.Linq.JObject { ["message"] = message }.ToString(Newtonsoft.Json.Formatting.Indented)
                : new System.Xml.Linq.XDocument(
                    new System.Xml.Linq.XElement("error",
                        new System.Xml.Linq.XElement("message", message))).ToString();
            var response = new HttpResponseMessage(statusCode) {
                Content = new StringContent(body, Encoding.UTF8, isJson ? "application/json" : "application/xml")
            };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            return ResponseMessage(response);
        }
    }
}
