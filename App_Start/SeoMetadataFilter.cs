using autodealer.dev.Services;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Web.Mvc;

namespace autodealer.dev {
    public sealed class SeoMetadataFilter : ActionFilterAttribute {
        private sealed class PageMetadata {
            public string RouteName { get; set; }
            public string Description { get; set; }
        }

        private static readonly IDictionary<string, PageMetadata> Pages = new Dictionary<string, PageMetadata>(StringComparer.OrdinalIgnoreCase) {
            { "Home/Index", Page("Home", "Automotive APIs and dealer website tools for VIN decoding, live inventory, digital retail, and secure developer integrations.") },
            { "Home/About", Page("About", "Learn how AutoDealer.dev brings automotive data APIs, dealership websites, inventory workflows, and developer tools into one platform.") },
            { "Home/Contact", Page("Contact", "Contact AutoDealer.dev about automotive APIs, VIN data, inventory integrations, or a modern website for your dealership.") },
            { "Home/Legal", Page("Legal", "Read the AutoDealer.dev Service Terms and Privacy Policy covering platform access, account data, API usage, and customer privacy.") },
            { "Solutions/Index", Page("Solutions", "Explore AutoDealer.dev solutions for VIN decoding, live dealership inventory, customer-facing dealer websites, and automotive development.") },
            { "Solutions/VinDecoder", Page("VinSolution", "Decode VINs into complete vehicle specifications, equipment, pricing, styles, and responsive JSON, XML, or HTML reports.") },
            { "Solutions/DigitalShowroom", Page("ShowroomSolution", "Launch a fast dealership website powered by live inventory, vehicle search, transparent pricing, financing, and lead conversion.") },
            { "Solutions/RequestDemo", Page("DealerDemo", "Request a personalized AutoDealer.dev demonstration for your dealership website, inventory workflow, and automotive API needs.") },
            { "Pricing/Index", Page("Pricing", "Compare AutoDealer.dev vehicle-data API plans and managed dealer website services with inventory administration, hosting, support, and scalable infrastructure.") },
            { "Documentation/Index", Page("Documentation", "Start integrating AutoDealer.dev APIs with authentication, request examples, response formats, VIN decoding, and error guidance.") },
            { "Documentation/VinHtml", Page("DocsVinHtml", "Integrate a responsive HTML VIN report with AutoDealer.dev, including request parameters, examples, and secure API authentication.") },
            { "Documentation/Authentication", Page("DocsAuthentication", "Learn how to authenticate AutoDealer.dev API requests with secure scoped API keys and safe credential handling.") },
            { "Documentation/Errors", Page("DocsErrors", "Understand AutoDealer.dev API status codes, error responses, usage limits, and recommended retry behavior.") },
            { "Account/Register", Page("GetApiKey", "Create an AutoDealer.dev workspace, begin a 14-day API trial, and receive secure credentials without entering payment information.") }
        };

        public override void OnActionExecuting(ActionExecutingContext filterContext) {
            if (!filterContext.HttpContext.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) || filterContext.IsChildAction) return;

            var controller = filterContext.ActionDescriptor.ControllerDescriptor.ControllerName;
            var action = filterContext.ActionDescriptor.ActionName;
            PageMetadata metadata;
            if (!Pages.TryGetValue(controller + "/" + action, out metadata)) return;

            var canonicalPath = new UrlHelper(filterContext.RequestContext).RouteUrl(metadata.RouteName);
            var requestedPath = filterContext.HttpContext.Request.Url == null ? string.Empty : filterContext.HttpContext.Request.Url.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(canonicalPath) && !requestedPath.Equals(canonicalPath, StringComparison.Ordinal)) {
                var query = filterContext.HttpContext.Request.Url == null ? string.Empty : filterContext.HttpContext.Request.Url.Query;
                filterContext.Result = new RedirectResult(canonicalPath + query, true);
            }
        }

        public override void OnActionExecuted(ActionExecutedContext filterContext) {
            if (filterContext.Exception != null || filterContext.IsChildAction) return;

            var controller = filterContext.ActionDescriptor.ControllerDescriptor.ControllerName;
            var action = filterContext.ActionDescriptor.ActionName;
            var key = controller + "/" + action;
            PageMetadata metadata;
            if (Pages.TryGetValue(key, out metadata)) {
                if (filterContext.Controller.ViewBag.Description == null)
                    filterContext.Controller.ViewBag.Description = metadata.Description;
                if (filterContext.Controller.ViewBag.CanonicalUrl == null) {
                    var relative = new UrlHelper(filterContext.RequestContext).RouteUrl(metadata.RouteName);
                    filterContext.Controller.ViewBag.CanonicalUrl = SeoUrl.Absolute(relative);
                }
            }

            var indexingEnabled = string.Equals(ConfigurationManager.AppSettings["Seo:AllowIndexing"], "true", StringComparison.OrdinalIgnoreCase);
            var privatePage = controller.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                (controller.Equals("Account", StringComparison.OrdinalIgnoreCase) && !action.Equals("Register", StringComparison.OrdinalIgnoreCase));
            if (!indexingEnabled || privatePage)
                filterContext.Controller.ViewBag.Robots = "noindex,nofollow";
        }

        private static PageMetadata Page(string routeName, string description) {
            return new PageMetadata { RouteName = routeName, Description = description };
        }
    }
}
