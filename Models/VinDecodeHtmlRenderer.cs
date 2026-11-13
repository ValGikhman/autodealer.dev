using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace autodealer.dev.Models {
    public sealed class VinDecodeResponseException : Exception {
        public VinDecodeResponseException(string message) : base(message) { }
    }

    /// <summary>
    /// Renders the same DataOne detail structure used by GTX's _DetailsDataOne partial.
    /// Native disclosure elements keep every panel functional in standalone/injected HTML.
    /// </summary>
    public static class VinDecodeHtmlRenderer {
        private static readonly HashSet<string> EquipmentPairsToSkip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Exterior|Exterior Features",
            "Exterior|Wheels and Tires",
            "Interior|Air Conditioning",
            "Interior|Comfort Features",
            "Interior|Instrumentation",
            "Interior|Convenience Features",
            "Safety and Security|Stability and Traction"
        };

        public static string Render(string xml, string vin) {
            var document = ParseResponse(xml);
            ThrowForDecoderErrors(document);

            var styles = document.Descendants("us_styles").Elements("style").ToList();
            if (styles.Count == 0)
                throw new VinDecodeResponseException("No US-market vehicle styles were found for this VIN.");

            var output = new StringBuilder(32768);
            VinReportStyles.AppendTo(output);
            output.Append("<section class=\"avd-report avd-dataone\" aria-label=\"DataOne vehicle details\">");
            output.Append("<div class=\"avd-report__body avd-dataone__body\">");

            for (var styleIndex = 0; styleIndex < styles.Count; styleIndex++) {
                var style = styles[styleIndex];
                OpenStyle(output, style, styleIndex, styles.Count);
                AppendPricing(output, style.Element("pricing"));
                AppendBasicSummary(output, style, vin);
                AppendTires(output, style.Element("standard_specifications"));
                AppendFuelEfficiency(output, style.Element("epa_fuel_efficiency"));
                AppendEngines(output, style.Element("engines"));
                AppendTransmissions(output, style.Element("transmissions"));
                AppendSpecifications(output, style.Element("standard_specifications"));
                AppendEquipment(output, style.Element("standard_generic_equipment"), "Standard Equipment");
                AppendEquipment(output, style.Element("optional_generic_equipment"), "Optional Equipment");
                AppendCompleteData(output, style);
                output.Append("</div></details>");
            }

            output.Append("</div><footer>Vehicle specifications are supplied by DataOne Software and may vary by configuration.</footer></section>");
            return output.ToString();
        }

        private static XDocument ParseResponse(string xml) {
            if (string.IsNullOrWhiteSpace(xml))
                throw new VinDecodeResponseException("DataOne returned an empty response.");

            try {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var textReader = new StringReader(xml))
                using (var reader = XmlReader.Create(textReader, settings))
                    return XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException) {
                throw new VinDecodeResponseException("DataOne returned an invalid response.");
            }
        }

        private static void ThrowForDecoderErrors(XDocument document) {
            var decoderError = document.Descendants("decoder_errors").Descendants("error").FirstOrDefault();
            if (decoderError != null) {
                var code = Text(decoderError, "code");
                var message = Text(decoderError, "message");
                if (!string.Equals(code, "RI", StringComparison.OrdinalIgnoreCase))
                    throw new VinDecodeResponseException(string.IsNullOrWhiteSpace(message)
                        ? "The VIN could not be decoded."
                        : "The VIN could not be decoded: " + message);
            }

            foreach (var queryError in document.Descendants("query_error")) {
                var queryErrorCode = Text(queryError, "error_code");
                if (!string.IsNullOrWhiteSpace(queryErrorCode)) {
                    var message = Text(queryError, "error_message");
                    throw new VinDecodeResponseException(string.IsNullOrWhiteSpace(message)
                        ? "The VIN could not be decoded (" + queryErrorCode + ")."
                        : "The VIN could not be decoded: " + message);
                }
            }
        }

        private static void OpenStyle(StringBuilder output, XElement style, int index, int count) {
            var basic = style.Element("basic_data");
            var title = Join(Text(basic, "year"), Text(basic, "make"), Text(basic, "model"), Attr(style, "name"));
            VinReportMarkup.OpenStyle(output, (count > 1 ? "Style " + (index + 1) + ": " : string.Empty) + Fallback(title, "Vehicle style"));
        }

        private static void AppendPricing(StringBuilder output, XElement pricing) {
            VinReportMarkup.OpenPanel(output, "DataOne Pricing");
            if (!HasContent(pricing)) {
                AppendEmpty(output, "No pricing data");
            }
            else {
                AppendKeyValueTable(output, new[] {
                    Pair("MSRP", Money(Text(pricing, "msrp"))),
                    Pair("Invoice Price", Money(Text(pricing, "invoice_price"))),
                    Pair("Destination Charge", Money(Text(pricing, "destination_charge"))),
                    Pair("Gas Guzzler Tax", Money(Text(pricing, "gas_guzzler_tax")))
                });
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendBasicSummary(StringBuilder output, XElement style, string vin) {
            var basic = style.Element("basic_data");
            var pricing = style.Element("pricing");
            var engine = style.Element("engines") == null ? null : style.Element("engines").Elements("engine").FirstOrDefault();
            var mpg = style.Element("epa_fuel_efficiency") == null ? null : style.Element("epa_fuel_efficiency").Elements("epa_mpg_record").FirstOrDefault();

            VinReportMarkup.OpenPanel(output, "Basic summary");
            if (!HasContent(basic)) {
                AppendEmpty(output, "No data");
            }
            else {
                AppendPairTable(output, new[] {
                    Pair("VIN#", vin), Pair("MSRP", Money(Text(pricing, "msrp"))),
                    Pair("Doors", Text(basic, "doors")), Pair("Body", Text(basic, "body_type")),
                    Pair("Cylinders", Text(engine, "ice_cylinders")), Pair("Drive", Text(basic, "drive_type")),
                    Pair("Engine", EngineSummary(engine)), Pair("MPG (comb)", WithUnit(Text(mpg, "combined"), "mpg")),
                    Pair("Vehicle Type", Text(basic, "vehicle_type")), Pair("Country", Text(basic, "country_of_manufacture")),
                    Pair("Brake System", Text(basic, "brake_system")), Pair("Plant", Text(basic, "plant"))
                });
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendTires(StringBuilder output, XElement specifications) {
            VinReportMarkup.OpenPanel(output, "Tires");
            output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table avd-table--center\"><thead><tr><th>Front</th><th>Rear</th></tr></thead><tbody><tr><td>")
                .Append(Encode(Specification(specifications, "Wheels and Tires", "Front Tire Description")))
                .Append("</td><td>").Append(Encode(Specification(specifications, "Wheels and Tires", "Rear Tire Description")))
                .Append("</td></tr></tbody></table></div>");
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendFuelEfficiency(StringBuilder output, XElement fuelEfficiency) {
            VinReportMarkup.OpenPanel(output, "Fuel Efficiency");
            var records = fuelEfficiency == null ? new List<XElement>() : fuelEfficiency.Elements("epa_mpg_record").ToList();
            if (records.Count == 0) {
                AppendEmpty(output, "No records");
            }
            else {
                output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table avd-table--center\"><thead><tr><th>Fuel</th><th>Grade</th><th>City</th><th>Hwy</th><th>Comb</th></tr></thead><tbody>");
                foreach (var record in records) {
                    output.Append("<tr>");
                    foreach (var value in new[] { Text(record, "fuel_type"), Text(record, "fuel_grade"), Text(record, "city"), Text(record, "highway"), Text(record, "combined") })
                        output.Append("<td>").Append(Encode(value)).Append("</td>");
                    output.Append("</tr>");
                }
                output.Append("</tbody></table></div>");
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendEngines(StringBuilder output, XElement engines) {
            var items = engines == null ? new List<XElement>() : engines.Elements("engine").ToList();
            var title = items.Count == 1 && !string.IsNullOrWhiteSpace(Attr(items[0], "name"))
                ? "Engine " + Attr(items[0], "name")
                : "Engines (" + items.Count + ")";
            VinReportMarkup.OpenPanel(output, title);
            if (items.Count == 0) {
                AppendEmpty(output, "No engines");
            }
            else {
                foreach (var engine in items) {
                    if (items.Count > 1)
                        output.Append("<h4 class=\"avd-subheading\">").Append(Encode(Fallback(Attr(engine, "name"), "Engine"))).Append("</h4>");
                    var values = new List<KeyValuePair<string, string>> {
                        Pair("Aspiration", Text(engine, "ice_aspiration")), Pair("Block Type", Text(engine, "ice_block_type")),
                        Pair("Cam Type", Text(engine, "ice_cam_type")), Pair("Fuel Type", Text(engine, "fuel_type")),
                        Pair("Compression", Text(engine, "ice_compression")), Pair("Cylinders", Text(engine, "ice_cylinders")),
                        Pair("Displacement", Text(engine, "ice_displacement")), Pair("Type", Text(engine, "engine_type"))
                    };
                    if (!string.Equals(Text(engine, "electric_max_hp"), "0", StringComparison.OrdinalIgnoreCase)) {
                        values.Add(Pair("Electric Max HP", Text(engine, "electric_max_hp")));
                        values.Add(Pair("Electric Max kW", Text(engine, "electric_max_kw")));
                        values.Add(Pair("Electric Motor Configuration", Text(engine, "electric_motor_configuration")));
                        values.Add(Pair("Electric Max Torque", Text(engine, "electric_max_torque")));
                        values.Add(Pair("Generator Description", Text(engine, "generator_description")));
                        values.Add(Pair("Generator Max HP", Text(engine, "generator_max_hp")));
                    }
                    values.AddRange(new[] {
                        Pair("Fuel Induction", Text(engine, "ice_fuel_induction")), Pair("Fuel Quality", Text(engine, "fuel_quality")),
                        Pair("Max HP", Text(engine, "ice_max_hp")), Pair("Max HP @", Text(engine, "ice_max_hp_at")),
                        Pair("Max Torque", Text(engine, "ice_max_torque")), Pair("Max Torque @", Text(engine, "ice_max_torque_at")),
                        Pair("Oil Capacity", Text(engine, "oil_capacity")), Pair("Stroke", Text(engine, "ice_stroke")),
                        Pair("Total Max HP", Text(engine, "total_max_hp")), Pair("Total Max HP @", Text(engine, "total_max_hp_at")),
                        Pair("Total Max Torque", Text(engine, "total_max_torque")), Pair("Total Max Torque @", Text(engine, "total_max_torque_at")),
                        Pair("Valve Timing", Text(engine, "ice_valve_timing")), Pair("Valves", Text(engine, "ice_valves"))
                    });
                    AppendPairTable(output, values);
                }
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendTransmissions(StringBuilder output, XElement transmissions) {
            var items = transmissions == null ? new List<XElement>() : transmissions.Elements("transmission").ToList();
            var title = "Transmission" + (items.Count == 0 || string.IsNullOrWhiteSpace(Attr(items[0], "name")) ? string.Empty : " " + Attr(items[0], "name"));
            VinReportMarkup.OpenPanel(output, title);
            if (items.Count == 0) {
                AppendEmpty(output, "No transmissions");
            }
            else {
                foreach (var transmission in items) {
                    AppendPairTable(output, new[] {
                        Pair("Type", Text(transmission, "type")), Pair("Detail Type", Text(transmission, "detail_type")),
                        Pair("Gears", Text(transmission, "gears")), Pair("Order Code", Text(transmission, "order_code"))
                    });
                }
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendSpecifications(StringBuilder output, XElement specifications) {
            VinReportMarkup.OpenPanel(output, "Standard Specifications");
            var categories = specifications == null ? new List<XElement>() : specifications.Elements("specification_category").ToList();
            if (categories.Count == 0) {
                AppendEmpty(output, "No specifications");
            }
            else {
                foreach (var category in categories) {
                    output.Append("<details class=\"avd-tree\"><summary><span class=\"avd-tree__pm\"></span>")
                        .Append(Encode(Fallback(Attr(category, "name"), "Category"))).Append("</summary><div class=\"avd-tree__content\">");
                    var values = category.Elements("specification_value").ToList();
                    if (values.Count == 0) AppendEmpty(output, "No values");
                    else {
                        output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table\"><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody>");
                        foreach (var value in values)
                            AppendTableRow(output, Attr(value, "name"), Value(value));
                        output.Append("</tbody></table></div>");
                    }
                    output.Append("</div></details>");
                }
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendEquipment(StringBuilder output, XElement equipmentRoot, string title) {
            VinReportMarkup.OpenPanel(output, title);
            var groups = equipmentRoot == null ? new List<XElement>() : equipmentRoot.Elements("generic_equipment_category_group").ToList();
            if (groups.Count == 0) {
                AppendEmpty(output, "None");
            }
            else {
                foreach (var group in groups) {
                    var groupName = Attr(group, "name");
                    output.Append("<details class=\"avd-tree\"><summary><span class=\"avd-tree__pm\"></span>")
                        .Append(Encode(groupName)).Append("</summary><div class=\"avd-tree__content\">");
                    var categories = group.Elements("generic_equipment_category")
                        .Where(c => !EquipmentPairsToSkip.Contains(groupName + "|" + Attr(c, "name"))).ToList();
                    if (categories.Count == 0) AppendEmpty(output, "No categories to show");
                    foreach (var category in categories) {
                        output.Append("<details class=\"avd-tree avd-tree--nested\"><summary><span class=\"avd-tree__pm\"></span>")
                            .Append(Encode(Attr(category, "name"))).Append("</summary><div class=\"avd-tree__content\">");
                        var equipments = category.Elements("generic_equipment").Where(e => e.Elements("generic_equipment_value").Any(v => !string.IsNullOrWhiteSpace(Value(v)))).ToList();
                        if (equipments.Count == 0) AppendEmpty(output, "No equipment to show");
                        else {
                            output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table\"><tbody>");
                            foreach (var equipment in equipments) {
                                var values = string.Join(", ", equipment.Elements("generic_equipment_value").Select(v => TitleCase(Value(v))).Where(v => !string.IsNullOrWhiteSpace(v)));
                                AppendTableRow(output, Attr(equipment, "name"), values);
                            }
                            output.Append("</tbody></table></div>");
                        }
                        output.Append("</div></details>");
                    }
                    output.Append("</div></details>");
                }
            }
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendCompleteData(StringBuilder output, XElement style) {
            VinReportMarkup.OpenPanel(output, "Complete DataOne Record", false);
            AppendAttributeTable(output, style, "Style");
            foreach (var child in style.Elements())
                AppendXmlElement(output, child);
            VinReportMarkup.ClosePanel(output);
        }

        private static void AppendXmlElement(StringBuilder output, XElement element) {
            var label = Humanize(element.Name.LocalName);
            var namedLabel = Attr(element, "name");
            if (!string.IsNullOrWhiteSpace(namedLabel)) label += " - " + namedLabel;

            if (!element.Elements().Any()) {
                AppendKeyValueTable(output, new[] { Pair(label, Value(element)) });
                AppendAttributeTable(output, element, label);
                return;
            }

            output.Append("<details class=\"avd-tree avd-tree--complete\"><summary><span class=\"avd-tree__pm\"></span>")
                .Append(Encode(label)).Append("</summary><div class=\"avd-tree__content\">");
            AppendAttributeTable(output, element, label);

            var simpleChildren = element.Elements().Where(e => !e.Elements().Any()).ToList();
            if (simpleChildren.Count > 0) {
                var values = new List<KeyValuePair<string, string>>();
                foreach (var child in simpleChildren) {
                    var childLabel = Humanize(child.Name.LocalName);
                    var childName = Attr(child, "name");
                    if (!string.IsNullOrWhiteSpace(childName)) childLabel += " - " + childName;
                    values.Add(Pair(childLabel, Value(child)));
                    foreach (var attribute in child.Attributes())
                        values.Add(Pair(childLabel + " @" + Humanize(attribute.Name.LocalName), attribute.Value));
                }
                AppendKeyValueTable(output, values);
            }

            foreach (var child in element.Elements().Where(e => e.Elements().Any()))
                AppendXmlElement(output, child);
            output.Append("</div></details>");
        }

        private static void AppendAttributeTable(StringBuilder output, XElement element, string prefix) {
            var attributes = element.Attributes().ToList();
            if (attributes.Count == 0) return;
            AppendKeyValueTable(output, attributes.Select(a => Pair(prefix + " @" + Humanize(a.Name.LocalName), a.Value)));
        }

        private static void AppendPairTable(StringBuilder output, IEnumerable<KeyValuePair<string, string>> values) {
            var items = values.ToList();
            output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table avd-table--pairs\"><tbody>");
            for (var i = 0; i < items.Count; i += 2) {
                output.Append("<tr>");
                AppendPairCells(output, items[i]);
                if (i + 1 < items.Count) AppendPairCells(output, items[i + 1]);
                else output.Append("<td colspan=\"2\"></td>");
                output.Append("</tr>");
            }
            output.Append("</tbody></table></div>");
        }

        private static void AppendKeyValueTable(StringBuilder output, IEnumerable<KeyValuePair<string, string>> values) {
            output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table avd-table--keyvalue\"><tbody>");
            foreach (var value in values) AppendTableRow(output, value.Key, value.Value);
            output.Append("</tbody></table></div>");
        }

        private static void AppendPairCells(StringBuilder output, KeyValuePair<string, string> pair) {
            output.Append("<th>").Append(Encode(pair.Key)).Append("</th><td>").Append(Encode(pair.Value)).Append("</td>");
        }

        private static void AppendTableRow(StringBuilder output, string label, string value) {
            output.Append("<tr><th>").Append(Encode(label)).Append("</th><td>").Append(Encode(value)).Append("</td></tr>");
        }

        private static void AppendEmpty(StringBuilder output, string text) {
            output.Append("<div class=\"avd-muted\">").Append(Encode(text)).Append("</div>");
        }

        private static string Specification(XElement specifications, string categoryName, string valueName) {
            var category = specifications == null ? null : specifications.Elements("specification_category")
                .FirstOrDefault(c => string.Equals(Attr(c, "name"), categoryName, StringComparison.OrdinalIgnoreCase));
            var value = category == null ? null : category.Elements("specification_value")
                .FirstOrDefault(v => string.Equals(Attr(v, "name"), valueName, StringComparison.OrdinalIgnoreCase));
            return Value(value);
        }

        private static string EngineSummary(XElement engine) {
            if (engine == null) return string.Empty;
            var displacement = Text(engine, "ice_displacement");
            var block = Text(engine, "ice_block_type");
            var cylinders = Text(engine, "ice_cylinders");
            var prefix = string.IsNullOrWhiteSpace(displacement) ? string.Empty : displacement + "L";
            var suffix = Join(block, cylinders);
            return string.IsNullOrWhiteSpace(prefix) ? suffix : prefix + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : " " + suffix.Replace(" ", "-"));
        }

        private static bool HasContent(XElement element) { return element != null && element.DescendantNodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)); }
        private static string Text(XElement parent, string childName) { return parent == null ? string.Empty : Value(parent.Element(childName)); }
        private static string Value(XElement element) { return element == null ? string.Empty : (element.Value ?? string.Empty).Trim(); }
        private static string Attr(XElement element, string name) { var value = element == null ? null : element.Attribute(name); return value == null ? string.Empty : value.Value.Trim(); }
        private static string Fallback(string value, string fallback) { return string.IsNullOrWhiteSpace(value) ? fallback : value; }
        private static string WithUnit(string value, string unit) { return string.IsNullOrWhiteSpace(value) ? string.Empty : value + " " + unit; }
        private static string Join(params string[] values) { return string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))); }
        private static string Encode(string value) { return WebUtility.HtmlEncode(value ?? string.Empty); }
        private static KeyValuePair<string, string> Pair(string key, string value) { return new KeyValuePair<string, string>(key, value ?? string.Empty); }
        private static string Humanize(string value) {
            if (string.IsNullOrWhiteSpace(value)) return "Details";
            var words = value.Replace('_', ' ').Trim();
            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }

        private static string Money(string value) {
            decimal amount;
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out amount)
                ? amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"))
                : string.Empty;
        }

        private static string TitleCase(string value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
        }


    }
}
