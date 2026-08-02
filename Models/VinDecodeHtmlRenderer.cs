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
            AppendStyles(output);
            output.Append("<section class=\"avd-dataone\" aria-label=\"DataOne vehicle details\">");
            output.Append("<div class=\"avd-dataone__body\">");

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
            output.Append("<details class=\"avd-style\"><summary class=\"avd-style__toggle\"><span class=\"avd-style__label\"><span class=\"avd-style__icon\" aria-hidden=\"true\"><svg viewBox=\"0 0 24 24\" focusable=\"false\"><path d=\"M4 15v-4l2.2-4h11.6l2.2 4v4M4 12h16M7 12h.01M17 12h.01M6 15v2M18 15v2\"/></svg></span><span>");
            if (count > 1) output.Append("Style ").Append(index + 1).Append(": ");
            output.Append(Encode(Fallback(title, "Vehicle style")))
                .Append("</span></span><span class=\"avd-toggle-icons\" aria-hidden=\"true\"><b class=\"avd-plus\">+</b><b class=\"avd-minus\">&minus;</b></span></summary><div class=\"avd-style__content\">");
        }

        private static void AppendPricing(StringBuilder output, XElement pricing) {
            OpenPanel(output, "✓ DataOne Pricing");
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
            ClosePanel(output);
        }

        private static void AppendBasicSummary(StringBuilder output, XElement style, string vin) {
            var basic = style.Element("basic_data");
            var pricing = style.Element("pricing");
            var engine = style.Element("engines") == null ? null : style.Element("engines").Elements("engine").FirstOrDefault();
            var mpg = style.Element("epa_fuel_efficiency") == null ? null : style.Element("epa_fuel_efficiency").Elements("epa_mpg_record").FirstOrDefault();

            OpenPanel(output, "Basic summary");
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
            ClosePanel(output);
        }

        private static void AppendTires(StringBuilder output, XElement specifications) {
            OpenPanel(output, "Tires");
            output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table avd-table--center\"><thead><tr><th>Front</th><th>Rear</th></tr></thead><tbody><tr><td>")
                .Append(Encode(Specification(specifications, "Wheels and Tires", "Front Tire Description")))
                .Append("</td><td>").Append(Encode(Specification(specifications, "Wheels and Tires", "Rear Tire Description")))
                .Append("</td></tr></tbody></table></div>");
            ClosePanel(output);
        }

        private static void AppendFuelEfficiency(StringBuilder output, XElement fuelEfficiency) {
            OpenPanel(output, "Fuel Efficiency");
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
            ClosePanel(output);
        }

        private static void AppendEngines(StringBuilder output, XElement engines) {
            var items = engines == null ? new List<XElement>() : engines.Elements("engine").ToList();
            var title = items.Count == 1 && !string.IsNullOrWhiteSpace(Attr(items[0], "name"))
                ? "Engine " + Attr(items[0], "name")
                : "Engines (" + items.Count + ")";
            OpenPanel(output, title);
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
            ClosePanel(output);
        }

        private static void AppendTransmissions(StringBuilder output, XElement transmissions) {
            var items = transmissions == null ? new List<XElement>() : transmissions.Elements("transmission").ToList();
            var title = "Transmission" + (items.Count == 0 || string.IsNullOrWhiteSpace(Attr(items[0], "name")) ? string.Empty : " " + Attr(items[0], "name"));
            OpenPanel(output, title);
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
            ClosePanel(output);
        }

        private static void AppendSpecifications(StringBuilder output, XElement specifications) {
            OpenPanel(output, "Standard Specifications");
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
            ClosePanel(output);
        }

        private static void AppendEquipment(StringBuilder output, XElement equipmentRoot, string title) {
            OpenPanel(output, title);
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
            ClosePanel(output);
        }

        private static void AppendCompleteData(StringBuilder output, XElement style) {
            OpenPanel(output, "Complete DataOne Record", false);
            AppendAttributeTable(output, style, "Style");
            foreach (var child in style.Elements())
                AppendXmlElement(output, child);
            ClosePanel(output);
        }

        private static void AppendXmlElement(StringBuilder output, XElement element) {
            var label = Humanize(element.Name.LocalName);
            var namedLabel = Attr(element, "name");
            if (!string.IsNullOrWhiteSpace(namedLabel)) label += " — " + namedLabel;

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
                    if (!string.IsNullOrWhiteSpace(childName)) childLabel += " — " + childName;
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

        private static void OpenPanel(StringBuilder output, string title) {
            OpenPanel(output, title, false);
        }

        private static void OpenPanel(StringBuilder output, string title, bool isOpen) {
            output.Append("<details class=\"avd-panel\"");
            if (isOpen) output.Append(" open");
            output.Append("><summary class=\"avd-panel__toggle\"><span class=\"avd-panel__label\"><span class=\"avd-panel__icon\" aria-hidden=\"true\">")
                .Append(PanelIcon(title)).Append("</span><span class=\"avd-panel__title\">")
                .Append(Encode(title)).Append("</span></span><span class=\"avd-toggle-icons\" aria-hidden=\"true\"><b class=\"avd-plus\">+</b><b class=\"avd-minus\">&minus;</b></span></summary><div class=\"avd-panel__content\">");
        }

        private static string PanelIcon(string title) {
            var key = (title ?? string.Empty).ToLowerInvariant();
            const string start = "<svg viewBox=\"0 0 24 24\" focusable=\"false\">";
            const string end = "</svg>";

            if (key.Contains("pricing"))
                return start + "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><path d=\"M15 8.5h-4.2a2 2 0 0 0 0 4H13a2 2 0 0 1 0 4H8.8M12 6.5v11\"/>" + end;
            if (key.Contains("basic"))
                return start + "<path d=\"M4 15V11l2-4h12l2 4v4M6 15v2M18 15v2M4 12h16M7 12h.01M17 12h.01\"/>" + end;
            if (key.Contains("tire"))
                return start + "<circle cx=\"12\" cy=\"12\" r=\"8.5\"/><circle cx=\"12\" cy=\"12\" r=\"4\"/><path d=\"M12 3.5v4.5M20.5 12H16M12 20.5V16M3.5 12H8\"/>" + end;
            if (key.Contains("fuel"))
                return start + "<path d=\"M7 20V5h8v15M5 20h12M9 8h4M15 9h2l2 2v6a1.5 1.5 0 0 0 3 0v-6l-2-2\"/>" + end;
            if (key.Contains("engine"))
                return start + "<path d=\"M7 8h9l3 3v6H7l-3-3v-4h3V8ZM10 8V5h4v3M19 12h2M4 11H2M9 12h5\"/>" + end;
            if (key.Contains("transmission"))
                return start + "<circle cx=\"6\" cy=\"6\" r=\"2\"/><circle cx=\"18\" cy=\"6\" r=\"2\"/><circle cx=\"6\" cy=\"18\" r=\"2\"/><circle cx=\"18\" cy=\"18\" r=\"2\"/><path d=\"M8 6h8M6 8v8M18 8v8M8 18h8M12 6v12\"/>" + end;
            if (key.Contains("specification"))
                return start + "<path d=\"M7 3h8l4 4v14H7V3Z M15 3v5h4M10 12h6M10 16h6M10 8h2\"/>" + end;
            if (key.Contains("optional"))
                return start + "<rect x=\"4\" y=\"6\" width=\"16\" height=\"14\" rx=\"2\"/><path d=\"M9 6V4h6v2M12 10v6M9 13h6\"/>" + end;
            if (key.Contains("equipment"))
                return start + "<path d=\"M4 9h16v10H4V9ZM9 9V6h6v3M4 13h16M10 13v2h4v-2\"/>" + end;
            if (key.Contains("complete"))
                return start + "<ellipse cx=\"12\" cy=\"6\" rx=\"7\" ry=\"3\"/><path d=\"M5 6v6c0 1.7 3.1 3 7 3s7-1.3 7-3V6M5 12v6c0 1.7 3.1 3 7 3s7-1.3 7-3v-6\"/>" + end;
            return start + "<rect x=\"4\" y=\"4\" width=\"6\" height=\"6\" rx=\"1\"/><rect x=\"14\" y=\"4\" width=\"6\" height=\"6\" rx=\"1\"/><rect x=\"4\" y=\"14\" width=\"6\" height=\"6\" rx=\"1\"/><rect x=\"14\" y=\"14\" width=\"6\" height=\"6\" rx=\"1\"/>" + end;
        }

        private static void ClosePanel(StringBuilder output) { output.Append("</div></details>"); }

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

        private static void AppendStyles(StringBuilder output) {
            output.Append(@"<style>
.avd-dataone{max-width:1180px;margin:20px auto;padding:18px;color:#172033;font-family:Segoe UI,Arial,sans-serif;background:#f5f5f5;border:1px solid #e3e3e3;border-radius:8px;box-shadow:inset 0 1px 1px rgba(0,0,0,.05)}.avd-dataone *{box-sizing:border-box}.avd-dataone details>summary{list-style:none}.avd-dataone details>summary::-webkit-details-marker{display:none}.avd-dataone details>summary::marker{content:''}.avd-panel__toggle{position:relative;display:block;width:100%;cursor:pointer;user-select:none}.avd-dataone__body{padding:2px 12px}.avd-panel{margin-bottom:14px}.avd-panel__toggle{min-height:46px;padding:12px 54px;border:1px solid #111827;border-radius:16px;background:linear-gradient(180deg,#fff,#aaaab3);box-shadow:0 4px 10px rgba(0,0,0,.2);color:#172033;font-size:16px;font-weight:700;text-align:center;text-shadow:0 1px rgba(255,255,255,.8)}.avd-panel__toggle:hover{filter:brightness(1.035)}.avd-panel__toggle:focus-visible,.avd-tree>summary:focus-visible{outline:3px solid rgba(21,90,150,.3);outline-offset:2px}.avd-toggle-icons{position:absolute;top:50%;display:inline-flex;align-items:center;justify-content:center;width:24px;height:24px;transform:translateY(-50%);font-size:22px;line-height:1}.avd-panel__toggle .avd-toggle-icons{left:18px}.avd-minus{display:none}.avd-panel[open]>.avd-panel__toggle .avd-plus{display:none}.avd-panel[open]>.avd-panel__toggle .avd-minus{display:inline}.avd-panel__content{margin-top:9px;padding:8px;border-radius:5px;background:#fff;box-shadow:0 2px 8px rgba(0,0,0,.06)}.avd-table-wrap{width:100%;overflow-x:auto}.avd-table{width:100%;border-collapse:collapse;background:#fff;font-size:14px}.avd-table th,.avd-table td{padding:9px 11px;border:1px solid #d8dee8;text-align:left;vertical-align:middle}.avd-table th{font-weight:700}.avd-table thead th{background:#edf2f7}.avd-table tbody tr:nth-child(even){background:#f8fafc}.avd-table--pairs th{width:19%}.avd-table--pairs td{width:31%}.avd-table--keyvalue th{width:40%}.avd-table--center th,.avd-table--center td{text-align:center}.avd-tree{margin:6px 4px}.avd-tree>summary{display:flex;align-items:center;gap:7px;padding:7px 9px;border-radius:5px;color:#155a96;cursor:pointer;font-weight:700}.avd-tree>summary:hover{background:#edf6ff}.avd-tree__pm::before{display:inline-block;width:16px;content:'+'}.avd-tree[open]>summary .avd-tree__pm::before{content:'−'}.avd-tree__content{margin:5px 0 8px 20px;padding-left:10px;border-left:2px solid #d7e3ef}.avd-tree--nested{margin-left:2px}.avd-muted{padding:12px;color:#687386;text-align:center}.avd-dataone footer{padding:8px 10px 0;color:#687386;font-size:12px;text-align:center}@media(max-width:640px){.avd-dataone{margin:10px;padding:10px}.avd-dataone__body{padding:0}.avd-panel__toggle{padding:11px 44px;font-size:14px}.avd-table--pairs th,.avd-table--pairs td{width:auto}.avd-table th,.avd-table td{padding:8px;font-size:12px}.avd-tree__content{margin-left:8px;padding-left:6px}}
.avd-style{margin:0 0 20px;padding:8px;border:1px solid #d8e1eb;border-radius:12px;background:rgba(255,255,255,.58);box-shadow:0 5px 18px rgba(41,65,89,.07)}.avd-style__toggle{position:relative;display:block;width:100%;padding:13px 54px 13px 18px;border:1px solid #c8d6e5;border-left:4px solid #6b91b5;border-radius:9px;background:linear-gradient(135deg,#fbfdff,#eaf1f8);box-shadow:0 2px 7px rgba(41,65,89,.08);color:#294b69;cursor:pointer;font-size:16px;font-weight:600;letter-spacing:.01em;text-align:left;transition:border-color .18s ease,box-shadow .18s ease,transform .18s ease}.avd-style__toggle:hover{border-color:#9eb7ce;box-shadow:0 4px 12px rgba(41,65,89,.13);transform:translateY(-1px)}.avd-style__toggle:focus-visible{outline:3px solid rgba(79,124,166,.22);outline-offset:2px}.avd-style__toggle .avd-toggle-icons{right:16px;color:#567b9d;font-weight:500}.avd-style[open]>.avd-style__toggle .avd-plus{display:none}.avd-style[open]>.avd-style__toggle .avd-minus{display:inline}.avd-style__content{padding:14px 2px 0}.avd-subheading{margin:12px 4px 7px;padding:8px 10px;border-left:4px solid #155a96;background:#edf6ff;font-size:15px}.avd-tree--complete>summary{color:#334155}.avd-tree--complete>.avd-tree__content{border-left-color:#cbd5e1}
.avd-panel__label{display:inline-flex;align-items:center;justify-content:center;gap:9px}.avd-panel__icon{display:inline-flex;align-items:center;justify-content:center;width:25px;height:25px;border:1px solid rgba(41,75,105,.2);border-radius:7px;background:rgba(255,255,255,.55);color:#365f82;box-shadow:inset 0 1px rgba(255,255,255,.8)}.avd-panel__icon svg{width:17px;height:17px;overflow:visible;fill:none;stroke:currentColor;stroke-width:1.7;stroke-linecap:round;stroke-linejoin:round}.avd-panel__title{line-height:1.2}
.avd-style{padding:9px;border-color:#cbd9e7;background:linear-gradient(145deg,rgba(255,255,255,.9),rgba(235,242,249,.72));box-shadow:0 10px 28px rgba(44,76,108,.1)}.avd-style__toggle{padding:14px 56px 14px 15px;border-color:#b7cce0;border-left:0;background:linear-gradient(120deg,#f9fcff 0,#e4effa 52%,#d7e7f6 100%);box-shadow:0 4px 14px rgba(54,95,130,.12),inset 0 1px rgba(255,255,255,.9);color:#234e73}.avd-style__toggle::after{position:absolute;inset:auto 12% 0;height:2px;border-radius:999px;background:linear-gradient(90deg,transparent,#78a6cc,transparent);content:''}.avd-style__label{display:inline-flex;align-items:center;gap:11px}.avd-style__icon{display:inline-flex;align-items:center;justify-content:center;width:34px;height:34px;flex:0 0 34px;border:1px solid #b7cce0;border-radius:10px;background:linear-gradient(145deg,#fff,#dceaf6);color:#3e729d;box-shadow:0 3px 9px rgba(44,76,108,.13)}.avd-style__icon svg{width:21px;height:21px;fill:none;stroke:currentColor;stroke-width:1.7;stroke-linecap:round;stroke-linejoin:round}.avd-style__content{position:relative;margin:0 4px 2px 17px;padding:18px 2px 2px 20px;border-left:2px solid #c9d9e8}.avd-style__content>.avd-panel{position:relative;margin-bottom:11px}.avd-style__content>.avd-panel::before{position:absolute;z-index:1;top:22px;left:-21px;width:18px;height:2px;background:#c9d9e8;content:''}.avd-style__content>.avd-panel::after{position:absolute;z-index:2;top:19px;left:-24px;width:8px;height:8px;border:2px solid #a9c2d8;border-radius:50%;background:#f5f8fb;content:''}.avd-style__content>.avd-panel>.avd-panel__toggle{min-height:42px;padding-top:9px;padding-bottom:9px;border-color:#cdd8e4;border-radius:10px;background:linear-gradient(180deg,#fff,#edf1f5);box-shadow:0 2px 7px rgba(44,62,80,.09);color:#344c63;font-size:14px;font-weight:600;text-shadow:none}.avd-style__content>.avd-panel>.avd-panel__toggle:hover{border-color:#afc2d4;background:linear-gradient(180deg,#fff,#e7eef5)}@media(max-width:640px){.avd-style__toggle{padding:11px 48px 11px 11px;font-size:14px}.avd-style__icon{width:30px;height:30px;flex-basis:30px}.avd-style__content{margin-left:9px;padding-left:13px}.avd-style__content>.avd-panel::before{left:-14px;width:11px}.avd-style__content>.avd-panel::after{left:-17px}}
</style>");
        }
    }
}
