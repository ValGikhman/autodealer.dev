using autodealer.dev.Services;
using System;
using System.Data.SqlClient;
using System.Web;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    [Authorize]
    public sealed class ClientResearchController : ResearchWorkspaceController {
        private readonly IClientAccountService accounts;
        public ClientResearchController(IClientAccountService accounts) : base(() => new AuctionBidService()) { this.accounts=accounts; }

        protected override bool CanUseWorkspace() {
            var email = ClientResearchSession.Email(User);
            if (email == null) return false;
            try { return !string.IsNullOrWhiteSpace(accounts.GetDashboard(email)?.ClientNumber); }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException) { return false; }
        }

        [HttpGet]
        public ActionResult PriceResearch() { return Tool("~/Views/PriceResearch/Index.cshtml"); }
        [HttpGet]
        public ActionResult VinDecoder() { return Tool("~/Views/NeoVin/Index.cshtml"); }

        private ActionResult Tool(string view) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!CanUseWorkspace()) return new HttpStatusCodeResult(403);
            try { ViewBag.ClientResearchKeys = new ApiAccessService().GetClientResearchKeys(ClientResearchSession.Email(User)); }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException) {
                return new HttpStatusCodeResult(503, "Account API keys are temporarily unavailable.");
            }
            ViewBag.ClientResearch=true;
            ViewBag.ResearchDefaultApiKeyEnabled=true;
            ViewBag.Robots="noindex,nofollow";
            return View(view);
        }
    }
}
