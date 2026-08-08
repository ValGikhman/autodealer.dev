namespace autodealer.dev.Services {
    public interface IApiKeyIssuanceService {
        ApiKeyIssuanceResult Issue(long clientId, string name);
    }

    public sealed class ApiKeyIssuanceResult {
        public long ApiKeyId { get; set; }
        public string Name { get; set; }
        public string FullApiKey { get; set; }
        public string RecipientEmail { get; set; }
        public bool EmailSent { get; set; }
    }
}
