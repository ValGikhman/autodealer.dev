using System;
using System.Configuration;

namespace autodealer.dev.Services {
    public static class AutoDealerConnectionString {
        private const string DevelopmentMachine = "VALS-PC";
        private const string DevelopmentConnectionName = "AutoDealer.dev.Development";
        private const string ProductionConnectionName = "AutoDealer.dev.Production";

        public static string Resolve() {
            var connectionName = string.Equals(Environment.MachineName, DevelopmentMachine, StringComparison.OrdinalIgnoreCase)
                ? DevelopmentConnectionName
                : ProductionConnectionName;
            var setting = ConfigurationManager.ConnectionStrings[connectionName];
            return setting == null ? null : setting.ConnectionString;
        }
    }
}
