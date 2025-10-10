using Services;
using System;
using System.Configuration;
using System.Web.Http;

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
    }
}