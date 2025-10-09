using System.Collections.Generic;
using System.Web.Http;

namespace autodealer.dev.Controllers {
    [RoutePrefix("api/service")]
    public class ServiceController : ApiController {
        // GET api/<controller>
        [HttpGet, Route("")]
        public IEnumerable<string> Get() {
            return new string[] { "value1", "value2" };
        }

        [HttpGet, Route("{id:int}")]
        // GET api/<controller>/5
        public string Get(int id) {
            return "value";
        }

        [HttpPost, Route("")]
        // POST api/<controller>
        public void Post([FromBody] string value) {
        }

        [HttpPut, Route("{id:int}")]
        // PUT api/<controller>/5
        public void Put(int id, [FromBody] string value) {
        }

        [HttpDelete, Route("{id:int}")]
        // DELETE api/<controller>/5
        public void Delete(int id) {
        }
    }
}