using autodealer.dev.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stripe;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Web;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public sealed class StripeWebhookController : Controller {
        private const int MaximumPayloadBytes = 1024 * 1024;
        private readonly IStripeWebhookService webhookService;

        public StripeWebhookController(IStripeWebhookService webhookService) {
            this.webhookService = webhookService;
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateInput(false)]
        public ActionResult Index() {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (Request.ContentLength > MaximumPayloadBytes)
                return new HttpStatusCodeResult(413);

            string payload;
            Request.InputStream.Position = 0;
            using (var reader = new StreamReader(Request.InputStream))
                payload = reader.ReadToEnd();

            var webhookSecret = (ConfigurationManager.AppSettings["Stripe:WebhookSecret"] ?? string.Empty).Trim();
            if (webhookSecret.Length == 0) {
                Trace.TraceError("Stripe webhook processing is disabled because Stripe:WebhookSecret is missing.");
                return new HttpStatusCodeResult(503);
            }

            try {
                // The destination can use a newer monthly Dahlia version than the SDK.
                // Signature and timestamp verification remain enabled; business fields
                // are read from the versioned raw JSON below.
                var stripeEvent = EventUtility.ConstructEvent(
                    payload,
                    Request.Headers["Stripe-Signature"],
                    webhookSecret,
                    300,
                    false);
                webhookService.Process(stripeEvent, JObject.Parse(payload));
                return new HttpStatusCodeResult(200);
            }
            catch (StripeException ex) {
                Trace.TraceWarning("Stripe webhook signature or event validation failed: " + ex.Message);
                return new HttpStatusCodeResult(400);
            }
            catch (JsonException ex) {
                Trace.TraceWarning("Stripe webhook JSON validation failed: " + ex.Message);
                return new HttpStatusCodeResult(400);
            }
            catch (Exception ex) {
                Trace.TraceError("Stripe webhook processing failed: " + ex);
                return new HttpStatusCodeResult(500);
            }
        }
    }
}
