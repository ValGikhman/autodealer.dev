using Common;
using Services;
using System;
using System.Configuration;
using System.Web.Http;

namespace autodealer.dev.Controllers {

    [RoutePrefix("api/inventory")]
    public class InventoryController : ApiController {

        [HttpGet, Route("")]
        public IHttpActionResult Get() {
            var service = new InventoryService();
            var vehicles = service.GetAll();

            var response = new InventoryResponse
            {
                InventoryDate = DateTime.UtcNow,
                Vehicles = vehicles
            };
            return Ok(response);
        }
    }
}