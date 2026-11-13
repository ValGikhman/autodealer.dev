using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace autodealer.dev.Models {
    public static class NeoVinHtmlRenderer {
        public static string Render(NeoVinResult result) {
            if (result == null) throw new ArgumentNullException("result");

            var output = new StringBuilder(65536);
            VinReportStyles.AppendTo(output);
            output.Append("<section class=\"avd-report neovin-report\" aria-label=\"MarketCheck VIN specifications\"><div class=\"avd-report__body\">");
            VinReportMarkup.OpenStyle(output, string.IsNullOrWhiteSpace(result.VehicleTitle) ? "VIN specification report" : result.VehicleTitle);
            AppendSummary(output, result);

            foreach (var category in GroupCategories(result.Specifications))
                AppendCategory(output, category, 0, result.Data);

            VinReportMarkup.OpenPanel(output, "Complete MarketCheck response");
            output.Append("<pre class=\"avd-report__raw\">").Append(Encode(result.RawJson)).Append("</pre>");
            VinReportMarkup.ClosePanel(output);
            output.Append("</div></details></div><footer>MarketCheck NeoVIN · Decoded ")
                .Append(Encode(result.DecodedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)))
                .Append(" · Specifications and confidence values are shown as returned by the provider.</footer></section>");
            return output.ToString();
        }

        private static void AppendSummary(StringBuilder output, NeoVinResult result) {
            VinReportMarkup.OpenPanel(output, "Basic summary");
            OpenTable(output);
            AppendRow(output, "VIN#", result.Vin);
            AppendRow(output, "Provider", "MarketCheck NeoVIN");
            foreach (var key in new[] { "engine", "transmission", "drivetrain", "fuel_type" }) {
                var token = result.Data[key];
                if (token == null || token.Type == JTokenType.Null) continue;
                var value = token is JValue ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.None);
                if (value.Length > 0) AppendRow(output, NeoVinResult.Label(key), value);
            }
            output.Append("</tbody></table></div>");
            VinReportMarkup.ClosePanel(output);
        }

        private static IEnumerable<Category> GroupCategories(IEnumerable<NeoVinSpecification> rows) {
            var root = new Category(string.Empty);
            foreach (var row in rows) {
                var parent = root;
                foreach (var name in row.Category.Split(new[] { " / " }, StringSplitOptions.None)) {
                    var category = parent.Children.FirstOrDefault(child => child.Name == name);
                    if (category == null) {
                        category = new Category(name);
                        parent.Children.Add(category);
                    }
                    parent = category;
                }
                parent.Rows.Add(row);
            }
            return root.Children;
        }

        private static void AppendCategory(StringBuilder output, Category category, int depth, JObject data) {
            if (depth == 0) {
                VinReportMarkup.OpenPanel(output, category.Name, category.Name == "Vehicle");
            } else {
                output.Append("<details class=\"avd-tree").Append(depth > 1 ? " avd-tree--nested" : string.Empty)
                    .Append("\"><summary><span class=\"avd-tree__pm\"></span>").Append(Encode(category.Name))
                    .Append("</summary><div class=\"avd-tree__content\">");
            }
            AppendCategoryRows(output, category.Rows, data);
            foreach (var child in category.Children) AppendCategory(output, child, depth + 1, data);
            output.Append("</div></details>");
        }

        private static void AppendCategoryRows(StringBuilder output, IEnumerable<NeoVinSpecification> rows, JObject data) {
            var groups = new List<ItemGroup>();
            foreach (var row in rows) {
                var record = SourceRecord(data, row.Path);
                ItemGroup group = null;
                if (record != null) {
                    var feature = record["feature_type"] != null && record["description"] != null;
                    var item = Text(record[feature ? "feature_type" : "item"]);
                    var attribute = feature ? string.Empty : Text(record["attribute"]);
                    if (!string.IsNullOrWhiteSpace(item)) {
                        group = groups.FirstOrDefault(candidate => candidate.Feature == feature && candidate.Item == item && candidate.Attribute == attribute);
                        if (group == null) {
                            group = new ItemGroup { Feature = feature, Item = item, Attribute = attribute };
                            groups.Add(group);
                        }
                        group.RecordPaths.Add(record.Path);
                    }
                }
                if (group == null) {
                    group = new ItemGroup();
                    groups.Add(group);
                }
                group.Rows.Add(row);
            }

            var ordinary = new List<NeoVinSpecification>();
            foreach (var group in groups) {
                if (group.RecordPaths.Count < 2) {
                    ordinary.AddRange(group.Rows);
                    continue;
                }
                AppendRows(output, ordinary);
                ordinary.Clear();
                var title = group.Feature || string.Equals(group.Item, group.Attribute, StringComparison.OrdinalIgnoreCase)
                    ? group.Item : group.Item + " · " + group.Attribute;
                output.Append("<details class=\"avd-tree avd-tree--nested avd-item-group\"><summary><span class=\"avd-tree__pm\"></span>")
                    .Append(Encode(title)).Append("</summary><div class=\"avd-tree__content\">");
                AppendRows(output, group.Rows);
                output.Append("</div></details>");
            }
            AppendRows(output, ordinary);
        }

        private static bool IsItemRecord(JObject value) {
            return value["feature_type"] != null && value["description"] != null
                || value["item"] != null && value["attribute"] != null && value["value"] != null;
        }

        private static JObject SourceRecord(JObject data, string path) {
            if (data == null || string.IsNullOrEmpty(path)) return null;
            try {
                var source = data.SelectToken(path, false);
                return source == null ? null : source.AncestorsAndSelf().OfType<JObject>().FirstOrDefault(IsItemRecord);
            } catch (Newtonsoft.Json.JsonException) {
                return null;
            }
        }

        private static string Text(JToken token) {
            return token == null || token.Type == JTokenType.Null ? string.Empty
                : token is JValue ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static void AppendRows(StringBuilder output, ICollection<NeoVinSpecification> rows) {
            if (rows.Count == 0) return;
            OpenTable(output);
            foreach (var row in rows)
                AppendRow(output, row.Field, string.IsNullOrWhiteSpace(row.Value) ? "—" : row.Value);
            output.Append("</tbody></table></div>");
        }

        private sealed class ItemGroup {
            internal bool Feature;
            internal string Item;
            internal string Attribute;
            internal readonly List<NeoVinSpecification> Rows = new List<NeoVinSpecification>();
            internal readonly HashSet<string> RecordPaths = new HashSet<string>(StringComparer.Ordinal);
        }

        private sealed class Category {
            internal readonly string Name;
            internal readonly List<Category> Children = new List<Category>();
            internal readonly List<NeoVinSpecification> Rows = new List<NeoVinSpecification>();
            internal Category(string name) { Name = name; }
        }

        private static void OpenTable(StringBuilder output) {
            output.Append("<div class=\"avd-table-wrap\"><table class=\"avd-table avd-table--keyvalue\"><thead><tr><th scope=\"col\">Specification</th><th scope=\"col\">Value</th></tr></thead><tbody>");
        }

        private static void AppendRow(StringBuilder output, string field, string value) {
            output.Append("<tr><th scope=\"row\">").Append(Encode(field)).Append("</th><td>").Append(Encode(value)).Append("</td></tr>");
        }

        private static string Encode(string value) { return WebUtility.HtmlEncode(value ?? string.Empty); }
    }
}
