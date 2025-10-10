using Services;
using System;
using System.Configuration;
using System.Web.Http;

namespace autodealer.dev.Controllers {

    [RoutePrefix("api/inventory")]
    public class InventoryController : ApiController {

        [HttpGet, Route("")]
        public IHttpActionResult Get() {
            return Ok();
        }

        [HttpGet, Route("{vin")]
        public IHttpActionResult Get([FromUri] string vin) {
            return Ok();
        }
    }
}