using System;

namespace autodealer.dev.Services {
    public interface IApiAccessService {
        ApiAccessResult BeginRequest(ApiAccessRequest request);
        void CompleteRequest(Guid requestId, int statusCode, long durationMs);
    }

    public sealed class ApiAccessRequest {
        public string KeyId { get; set; }
        public byte[] SecretHash { get; set; }
        public string RequiredScope { get; set; }
        public string Endpoint { get; set; }
        public string HttpMethod { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
    }

    public sealed class ApiAccessResult {
        public string ResultCode { get; set; }
        public Guid RequestId { get; set; }
        public string ClientNumber { get; set; }
        public string Scopes { get; set; }
        public int MonthlyQuota { get; set; }
        public int MonthlyUsage { get; set; }
    }
}
