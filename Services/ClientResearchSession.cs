using System;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Web;
using System.Web.Helpers;
using System.Web.Security;
using System.Web.Mvc;

namespace autodealer.dev.Services {
    public static class ClientResearchSession {
        public const string WorkspaceHeader = "X-Research-Workspace";
        public const string KeySelectionHeader = "X-Research-Key-Id";
        public static string Email(IPrincipal user) {
            var identity=user?.Identity as FormsIdentity;
            if(identity==null || !identity.IsAuthenticated || identity.Ticket==null || identity.Ticket.Expired ||
                !string.IsNullOrEmpty(identity.Ticket.UserData) || user.IsInRole("Admin") || string.IsNullOrWhiteSpace(identity.Name)) return null;
            return identity.Name.Trim().ToLowerInvariant();
        }
        public static string Verify(HttpRequestMessage request) {
            var context=HttpContext.Current;
            return Verify(request,context==null?null:new HttpContextWrapper(context),AntiForgery.Validate);
        }
        public static string Verify(HttpRequestMessage request,HttpContextBase context,Action<string,string> verify) {
            if(request==null || request.Method!=HttpMethod.Post || request.RequestUri==null || ResearchDefaultApiKey.HasExplicitCredentials(request)) return null;
            var path=request.RequestUri.AbsolutePath;
            if(!string.Equals(path,"/api/service/price-research",StringComparison.OrdinalIgnoreCase) && !string.Equals(path,"/api/service/neovin",StringComparison.OrdinalIgnoreCase)) return null;
            var email=Email(context?.User);
            if(email==null || !request.Headers.TryGetValues(WorkspaceHeader,out var modes) || modes.SingleOrDefaultSafe()!="account") return null;
            if(!request.Headers.TryGetValues(ResearchDefaultApiKey.VerificationHeaderName,out var tokens)) return null;
            var token=tokens.SingleOrDefaultSafe();
            if(string.IsNullOrWhiteSpace(token)) throw new HttpAntiForgeryException("Invalid research verification.");
            verify(context.Request.Cookies[AntiForgeryConfig.CookieName]?.Value,token);
            return email;
        }
        private static string SingleOrDefaultSafe(this System.Collections.Generic.IEnumerable<string> values) {
            var items=values.Take(2).ToArray();
            return items.Length==1 ? items[0] : null;
        }
    }
}
