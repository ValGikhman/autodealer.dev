using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using System.Xml;
using System.Xml.Linq;

namespace autodealer.dev.Models {
    public sealed class DataOneCredentials {
        public string AccessKey { get; private set; }
        public string SecretAccessKey { get; private set; }

        public static DataOneCredentials Load() {
            var configured = new DataOneCredentials {
                AccessKey = ConfigurationManager.AppSettings["DataOne:AccessKey"],
                SecretAccessKey = ConfigurationManager.AppSettings["DataOne:SecretAccessKey"]
            };
            if (configured.IsComplete()) return configured;

            var fromEnvironment = new DataOneCredentials {
                AccessKey = Environment.GetEnvironmentVariable("AUTODEALER_DATAONE_ACCESS_KEY"),
                SecretAccessKey = Environment.GetEnvironmentVariable("AUTODEALER_DATAONE_SECRET_ACCESS_KEY")
            };
            if (fromEnvironment.IsComplete()) return fromEnvironment;

            return LoadSharedDevelopmentConfig() ?? configured;
        }

        private static DataOneCredentials LoadSharedDevelopmentConfig() {
            var relativePath = ConfigurationManager.AppSettings["DataOne:SharedConfigPath"];
            var applicationRoot = HostingEnvironment.MapPath("~/");
            if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(applicationRoot)) return null;

            try {
                var path = Path.GetFullPath(Path.Combine(applicationRoot, relativePath));
                if (!File.Exists(path)) return null;

                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                XDocument document;
                using (var reader = XmlReader.Create(path, settings)) document = XDocument.Load(reader, LoadOptions.None);

                Func<string, string> value = key => document.Descendants("appSettings").Elements("add")
                    .Where(x => string.Equals((string)x.Attribute("key"), key, StringComparison.Ordinal))
                    .Select(x => (string)x.Attribute("value"))
                    .FirstOrDefault();
                var credentials = new DataOneCredentials {
                    AccessKey = value("DataOne:AccessKey"),
                    SecretAccessKey = value("DataOne:SecretAccessKey")
                };
                return credentials.IsComplete() ? credentials : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (XmlException) { return null; }
        }

        private bool IsComplete() {
            return !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretAccessKey);
        }
    }
}
