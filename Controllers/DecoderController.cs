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
    }
}
