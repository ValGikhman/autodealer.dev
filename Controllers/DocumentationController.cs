using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public class DocumentationController : Controller {
        public ActionResult Index() { return View(); }
        public ActionResult VinHtml() { return View(); }
        public ActionResult Authentication() { return View(); }
        public ActionResult Errors() { return View(); }
    }
}
