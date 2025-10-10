using System.Web.Http;                // <-- add this
using System.Web.Mvc;
using Unity;
using Services;
using Unity.AspNet.Mvc;

namespace autodealer.dev
{
    public static class UnityConfig {
        private static IUnityContainer _container;
        public static IUnityContainer Container => _container;

        public static void RegisterComponents()
        {
            var container = new UnityContainer();

            // Register services (choose one lifetime manager)
            // PerRequest works well; if you hit scope issues, use HierarchicalLifetimeManager.
            container.RegisterType<IVinDecoderService, VinDecoderService>(new PerRequestLifetimeManager());
            // container.RegisterType<IVinDecoderService, VinDecoderService>(new HierarchicalLifetimeManager());

            // Set MVC resolver
            DependencyResolver.SetResolver(new Unity.Mvc5.UnityDependencyResolver(container));

            // Set Web API resolver
            GlobalConfiguration.Configuration.DependencyResolver = new Unity.WebApi.UnityDependencyResolver(container);

            _container = container;
        }
    }
}