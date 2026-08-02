using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace autodealer.dev.Models {
    /// <summary>
    /// Format-neutral response object for the complete DataOne decoder payload.
    /// </summary>
    public sealed class VinDecodeApiResponse {
        private VinDecodeApiResponse(string vin, DateTime decodedAtUtc, XDocument dataOneDocument) {
            Vin = vin;
            DecodedAtUtc = decodedAtUtc;
            DataOneDocument = dataOneDocument;
        }

        public string Vin { get; private set; }
        public DateTime DecodedAtUtc { get; private set; }
        public XDocument DataOneDocument { get; private set; }

        public static VinDecodeApiResponse Create(string vin, string dataOneXml) {
            var document = Parse(dataOneXml);
            ThrowForDecoderErrors(document);
            return new VinDecodeApiResponse(vin, DateTime.UtcNow, document);
        }

        public JObject ToJsonObject() {
            var convertedXml = JsonConvert.SerializeXNode(DataOneDocument, Newtonsoft.Json.Formatting.None, true);
            var data = string.IsNullOrWhiteSpace(convertedXml)
                ? new JObject()
                : JToken.Parse(convertedXml);

            return new JObject {
                ["vin"] = Vin,
                ["decodedAtUtc"] = DecodedAtUtc.ToString("o"),
                ["data"] = data
            };
        }

        public XDocument ToXmlDocument() {
            return new XDocument(
                new XElement("vin_decode_response",
                    new XElement("vin", Vin),
                    new XElement("decoded_at_utc", DecodedAtUtc.ToString("o")),
                    new XElement("data", new XElement(DataOneDocument.Root))));
        }

        private static XDocument Parse(string xml) {
            if (string.IsNullOrWhiteSpace(xml))
                throw new VinDecodeResponseException("DataOne returned an empty response.");

            try {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var textReader = new StringReader(xml))
                using (var reader = XmlReader.Create(textReader, settings)) {
                    var document = XDocument.Load(reader, LoadOptions.None);
                    if (document.Root == null)
                        throw new VinDecodeResponseException("DataOne returned an empty document.");
                    return document;
                }
            }
            catch (XmlException) {
                throw new VinDecodeResponseException("DataOne returned an invalid response.");
            }
        }

        private static void ThrowForDecoderErrors(XDocument document) {
            foreach (var error in document.Descendants("decoder_errors").Descendants("error")) {
                var code = Text(error, "code");
                if (string.Equals(code, "RI", StringComparison.OrdinalIgnoreCase)) continue;
                var message = Text(error, "message");
                throw new VinDecodeResponseException(string.IsNullOrWhiteSpace(message)
                    ? "The VIN could not be decoded."
                    : "The VIN could not be decoded: " + message);
            }

            foreach (var error in document.Descendants("query_error")) {
                var code = Text(error, "error_code");
                if (string.IsNullOrWhiteSpace(code)) continue;
                var message = Text(error, "error_message");
                throw new VinDecodeResponseException(string.IsNullOrWhiteSpace(message)
                    ? "The VIN could not be decoded (" + code + ")."
                    : "The VIN could not be decoded: " + message);
            }
        }

        private static string Text(XElement parent, string childName) {
            var child = parent == null ? null : parent.Element(childName);
            return child == null ? string.Empty : (child.Value ?? string.Empty).Trim();
        }
    }
}
