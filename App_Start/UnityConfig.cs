using System.Web.Http;                // <-- add this
using System.Web.Mvc;
using Unity;
using Services;
using Unity.AspNet.Mvc;
using autodealer.dev.Services;

namespace autodealer.dev
{
    public static class UnityConfig {
        private static IUnityContainer _container;
        public static IUnityContainer Container => _container;

        public static void RegisterComponents()
        {
            if (_container != null) return;
            var container = new UnityContainer();

            // Register services (choose one lifetime manager)
            // PerRequest works well; if you hit scope issues, use HierarchicalLifetimeManager.
            container.RegisterType<IVinDecoderService, VinDecoderService>(new PerRequestLifetimeManager());
            container.RegisterType<ICredentialEmailService, SmtpCredentialEmailService>();
            container.RegisterType<IClientAccountService, ClientAccountService>();
            container.RegisterType<IApiAccessService, ApiAccessService>();
            container.RegisterType<IAdminService, AdminService>();
            container.RegisterType<IPlanService, PlanService>();
            // container.RegisterType<IVinDecoderService, VinDecoderService>(new HierarchicalLifetimeManager());

            // Set MVC resolver
            DependencyResolver.SetResolver(new Unity.Mvc5.UnityDependencyResolver(container));

            // Set Web API resolver
            GlobalConfiguration.Configuration.DependencyResolver = new Unity.WebApi.UnityDependencyResolver(container);

            _container = container;
        }
    }
}
