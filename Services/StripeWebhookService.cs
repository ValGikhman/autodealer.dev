using autodealer.dev.Data;
using Newtonsoft.Json.Linq;
using Stripe;
using System;
using System.Configuration;
using System.Data;
using System.Linq;

namespace autodealer.dev.Services {
    public sealed class StripeWebhookService : IStripeWebhookService {
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string connectionString;

        public StripeWebhookService() {
            connectionString = AutoDealerConnectionString.Resolve();
        }

        public void Process(Event stripeEvent, JObject payload) {
            if (stripeEvent == null) throw new ArgumentNullException("stripeEvent");
            if (payload == null) throw new ArgumentNullException("payload");
            if (string.IsNullOrWhiteSpace(stripeEvent.Id) || string.IsNullOrWhiteSpace(stripeEvent.Type))
                throw new InvalidOperationException("The Stripe event ID or type is missing.");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            var dataObject = payload.SelectToken("data.object") as JObject;
            if (dataObject == null)
                throw new InvalidOperationException("The Stripe webhook data object is missing.");
            var eventCreated = payload.Value<long?>("created");
            if (!eventCreated.HasValue)
                throw new InvalidOperationException("The Stripe webhook creation timestamp is missing.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        var receivedEvents = context.GetTable<StripeWebhookEventRecord>();
                        if (receivedEvents.Any(x => x.StripeEventId == stripeEvent.Id)) {
                            transaction.Commit();
                            return;
                        }

                        if (string.Equals(stripeEvent.Type, "checkout.session.completed", StringComparison.Ordinal))
                            LinkCheckoutSession(context, dataObject);
                        else if (string.Equals(stripeEvent.Type, "invoice.paid", StringComparison.Ordinal))
                            ActivatePaidInvoice(context, dataObject);

                        receivedEvents.InsertOnSubmit(new StripeWebhookEventRecord {
                            StripeEventId = stripeEvent.Id,
                            EventType = stripeEvent.Type,
                            EventCreatedUtc = FromUnixTime(eventCreated.Value),
                            ProcessedUtc = DateTime.UtcNow
                        });

                        context.SubmitChanges();
                        transaction.Commit();
                    }
                    catch {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private static void LinkCheckoutSession(AutoDealerDataContext context, JObject session) {
            if (!string.Equals(ReadString(session["mode"]), "subscription", StringComparison.Ordinal)) return;

            var clientNumber = ReadString(session["client_reference_id"]);
            var providerSubscriptionId = ReadExpandableId(session["subscription"]);
            var paymentLinkId = ReadExpandableId(session["payment_link"]);
            if (clientNumber.Length == 0)
                throw new InvalidOperationException("The completed Checkout Session has no client_reference_id.");
            if (providerSubscriptionId.Length == 0)
                throw new InvalidOperationException("The completed Checkout Session has no Stripe subscription ID.");
            if (paymentLinkId.Length == 0)
                throw new InvalidOperationException("The completed Checkout Session has no Stripe Payment Link ID.");

            var client = context.Clients.SingleOrDefault(x => x.ClientNumber == clientNumber);
            if (client == null)
                throw new InvalidOperationException("No local client matches the Stripe client_reference_id.");

            var subscription = context.Subscriptions
                .Where(x => x.ClientId == client.ClientId)
                .OrderByDescending(x => x.CurrentPeriodEndUtc)
                .FirstOrDefault();
            if (subscription == null)
                throw new InvalidOperationException("The referenced client has no local subscription.");

            var expectedPaymentLinkId = GetExpectedPaymentLinkId(subscription.Plan.PlanCode);
            if (!string.Equals(paymentLinkId, expectedPaymentLinkId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Stripe Payment Link does not match the client's subscription plan.");
            if (context.Subscriptions.Any(x =>
                x.ProviderSubscriptionId == providerSubscriptionId && x.SubscriptionId != subscription.SubscriptionId))
                throw new InvalidOperationException("The Stripe subscription is already linked to another local subscription.");
            if (!string.IsNullOrWhiteSpace(subscription.ProviderSubscriptionId) &&
                !string.Equals(subscription.ProviderSubscriptionId, providerSubscriptionId, StringComparison.Ordinal))
                throw new InvalidOperationException("The local subscription is already linked to another Stripe subscription.");

            subscription.ProviderSubscriptionId = providerSubscriptionId;
            subscription.UpdatedUtc = DateTime.UtcNow;
        }

        private static void ActivatePaidInvoice(AutoDealerDataContext context, JObject invoice) {
            if (invoice.Value<bool?>("paid") != true ||
                !string.Equals(ReadString(invoice["status"]), "paid", StringComparison.Ordinal)) return;

            var parentType = ReadString(invoice.SelectToken("parent.type"));
            if (!string.Equals(parentType, "subscription_details", StringComparison.Ordinal)) return;

            var providerSubscriptionId = ReadExpandableId(
                invoice.SelectToken("parent.subscription_details.subscription"));
            if (providerSubscriptionId.Length == 0)
                throw new InvalidOperationException("The paid invoice has no Stripe subscription ID.");

            var subscription = context.Subscriptions
                .SingleOrDefault(x => x.ProviderSubscriptionId == providerSubscriptionId);
            if (subscription == null)
                throw new InvalidOperationException(
                    "The paid Stripe subscription is not linked locally yet; Stripe should retry this event.");

            DateTime periodStartUtc;
            DateTime periodEndUtc;
            if (!TryReadServicePeriod(invoice, out periodStartUtc, out periodEndUtc))
                throw new InvalidOperationException("The paid invoice has no valid subscription service period.");

            subscription.Status = "active";
            if (periodEndUtc > subscription.CurrentPeriodEndUtc) {
                subscription.CurrentPeriodStartUtc = periodStartUtc;
                subscription.CurrentPeriodEndUtc = periodEndUtc;
            }
            subscription.UpdatedUtc = DateTime.UtcNow;
        }

        private static bool TryReadServicePeriod(JObject invoice, out DateTime startUtc, out DateTime endUtc) {
            startUtc = default(DateTime);
            endUtc = default(DateTime);

            var periods = invoice.SelectTokens("lines.data[*]")
                .OfType<JObject>()
                .Where(line => string.Equals(
                    ReadString(line.SelectToken("parent.type")),
                    "subscription_item_details",
                    StringComparison.Ordinal))
                .Select(line => line["period"] as JObject)
                .Where(period => period != null)
                .Select(period => new {
                    Start = period.Value<long?>("start"),
                    End = period.Value<long?>("end")
                })
                .Where(period => period.Start.HasValue && period.End.HasValue &&
                                 period.End.Value > period.Start.Value)
                .ToList();

            if (periods.Count == 0) return false;
            startUtc = FromUnixTime(periods.Min(period => period.Start.Value));
            endUtc = FromUnixTime(periods.Max(period => period.End.Value));
            return endUtc > startUtc;
        }

        private static string GetExpectedPaymentLinkId(string planCode) {
            var normalizedPlanCode = (planCode ?? string.Empty).Trim().ToUpperInvariant();
            var paymentLinkId = (ConfigurationManager.AppSettings[
                "Stripe:PaymentLinkId:" + normalizedPlanCode] ?? string.Empty).Trim();
            if (paymentLinkId.Length == 0)
                throw new InvalidOperationException(
                    "No Stripe Payment Link ID is configured for plan '" + normalizedPlanCode + "'.");
            return paymentLinkId;
        }

        private static string ReadExpandableId(JToken value) {
            if (value == null || value.Type == JTokenType.Null) return string.Empty;
            if (value.Type == JTokenType.String) return ReadString(value);
            return ReadString(value["id"]);
        }

        private static string ReadString(JToken value) {
            return value == null || value.Type == JTokenType.Null
                ? string.Empty
                : (value.Value<string>() ?? string.Empty).Trim();
        }

        private static DateTime FromUnixTime(long seconds) {
            try {
                return UnixEpoch.AddSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException ex) {
                throw new InvalidOperationException("A Stripe timestamp is outside the supported range.", ex);
            }
        }
    }
}
