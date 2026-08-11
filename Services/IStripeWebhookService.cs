using Newtonsoft.Json.Linq;
using Stripe;

namespace autodealer.dev.Services {
    public interface IStripeWebhookService {
        void Process(Event stripeEvent, JObject payload);
    }
}
