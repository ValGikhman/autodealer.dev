using System.Web.Optimization;

namespace autodealer.dev {
    public class BundleConfig {
        // For more information on bundling, visit https://go.microsoft.com/fwlink/?LinkId=301862
        public static void RegisterBundles(BundleCollection bundles) {
            bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                        "~/Scripts/jquery-{version}.js"));

            bundles.Add(new ScriptBundle("~/bundles/jqueryval").Include(
                        "~/Scripts/jquery.validate.js",
                        "~/Scripts/jquery.validate.unobtrusive.js"));

            // Bootstrap is already minified and contains syntax unsupported by the legacy AjaxMin transform.
            bundles.Add(new Bundle("~/bundles/bootstrap").Include(
                      "~/Scripts/bootstrap.bundle.min.js"));

            bundles.Add(new ScriptBundle("~/bundles/password-visibility").Include(
                      "~/Scripts/passwordVisibility.js"));

            bundles.Add(new ScriptBundle("~/bundles/account-validation").Include(
                      "~/Scripts/accountCredentialValidation.js"));

            bundles.Add(new ScriptBundle("~/bundles/vin-report").Include(
                      "~/Scripts/autodealer-vin-report.js"));

            bundles.Add(new ScriptBundle("~/bundles/admin-dashboard").Include(
                      "~/Scripts/pepTools/pepGrid.js",
                      "~/Scripts/pepTools/pepEdit.js",
                      "~/Scripts/accountCredentialValidation.js",
                      "~/Scripts/Admin/adminDashboard.js",
                      "~/Scripts/Admin/adminMail.js"));

            bundles.Add(new StyleBundle("~/Content/css").Include(
                      "~/Content/site.css"));

            bundles.Add(new StyleBundle("~/bundles/admin-css").Include(
                      "~/Content/pepTools/pepGrid.css",
                      "~/Content/pepTools/pepEdit.css",
                      "~/Content/dashboard.css"));
        }
    }
}
