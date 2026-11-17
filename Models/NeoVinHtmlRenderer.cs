using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace autodealer.dev.Models {
    public static class NeoVinHtmlRenderer {
        private static readonly Regex NumericValue = new Regex(@"\A(?<sign>[+-]?)(?<whole>[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)(?<fraction>\.[0-9]+)?\z", RegexOptions.CultureInvariant);
        private static readonly Regex IdentifierField = new Regex(@"\b(years?|vin|codes?|ids?|identifiers?|zip|postal|stock|serial|sku|model|trim|part\s*(number|no))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AuditField = new Regex(@"\A(?:Last\s+)?(?:Created|Updated)(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Render(NeoVinResult result) {
            if (result == null) throw new ArgumentNullException("result");

            var output = new StringBuilder(65536);
            VinReportStyles.AppendTo(output);
            output.Append("<section class=\"avd-report neovin-report\" aria-label=\"MarketCheck VIN specifications\"><div class=\"avd-report__body\">");
            VinReportMarkup.OpenStyle(output, string.IsNullOrWhiteSpace(result.VehicleTitle) ? "VIN specification report" : result.VehicleTitle);

            foreach (var category in GroupCategories(result.Specifications.Where(row => !IsAuditField(row, result.Data))))
                AppendCategory(output, category, 0, result.Data);

            VinReportMarkup.OpenPanel(output, "Complete MarketCheck response");
            output.Append("<pre class=\"avd-report__raw\">").Append(Encode(result.RawJson)).Append("</pre>");
            VinReportMarkup.ClosePanel(output);
            output.Append("</div></details></div><footer>MarketCheck NeoVIN · Decoded ")
                .Append(Encode(result.DecodedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)))
                .Append(" · Specifications and confidence values are shown as returned by the provider.</footer></section>");
            return output.ToString();
        }

        private static bool IsAuditField(NeoVinSpecification row, JObject data) {
            var source = SourceToken(data, row.Path);
            if (source != null)
                return source.AncestorsAndSelf().OfType<JProperty>().Any(property => AuditField.IsMatch(NeoVinResult.Label(property.Name)));
            return ((row.Category ?? string.Empty) + " / " + (row.Field ?? string.Empty))
                .Split(new[] { " / " }, StringSplitOptions.None).Any(label => AuditField.IsMatch(label));
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
            foreach (var child in category.Children
                .OrderBy(child => child.Name == "Standard" ? 0 : child.Name == "Optional" ? 1 : 2)
                .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(child => child.Name, StringComparer.Ordinal))
                AppendCategory(output, child, depth + 1, data);
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
                AppendFieldRows(output, ordinary, data);
                ordinary.Clear();
                var title = group.Feature || string.Equals(group.Item, group.Attribute, StringComparison.OrdinalIgnoreCase)
                    ? group.Item : group.Item + " · " + group.Attribute;
                output.Append("<details class=\"avd-tree avd-tree--nested avd-item-group\"><summary><span class=\"avd-tree__pm\"></span>")
                    .Append(Encode(title)).Append("</summary><div class=\"avd-tree__content\">");
                if (!AppendRepeatedValues(output, group.Rows, title, data)) AppendFieldRows(output, group.Rows, data);
                output.Append("</div></details>");
            }
            AppendFieldRows(output, ordinary, data);
        }

        private static void AppendFieldRows(StringBuilder output, ICollection<NeoVinSpecification> rows, JObject data) {
            AppendFieldNodes(output, GroupFieldRows(rows), data);
        }

        private static List<FieldNode> GroupFieldRows(IEnumerable<NeoVinSpecification> rows, string heading = null) {
            var root = new FieldNode(string.Empty);
            foreach (var row in rows) {
                var label = row.Field ?? string.Empty;
                if (heading != null && label.StartsWith(heading + " / ", StringComparison.Ordinal))
                    label = label.Substring(heading.Length + 3);
                var parts = label.Split(new[] { " / " }, StringSplitOptions.None);
                // Only the explicit path separator creates groups; literal slashes and
                // malformed empty segments remain part of the original field label.
                if (parts.Any(string.IsNullOrWhiteSpace)) parts = new[] { label };
                var parent = root;
                foreach (var name in parts.Take(parts.Length - 1)) {
                    var child = parent.Children.FirstOrDefault(candidate => candidate.Row == null && candidate.Name == name);
                    if (child == null) {
                        child = new FieldNode(name);
                        parent.Children.Add(child);
                    }
                    parent = child;
                }
                parent.Children.Add(new FieldNode(parts.Last(), row));
            }
            return root.Children;
        }

        private static bool AppendRepeatedValues(StringBuilder output, IEnumerable<NeoVinSpecification> rows, string title, JObject data) {
            var items = new List<DescriptionItem>();
            foreach (var record in rows.GroupBy(row => SourceRecord(data, row.Path).Path, StringComparer.Ordinal)) {
                var primary = record.Where(row => row.Path == record.Key && row.Field == title).ToList();
                // Distinct labels (for example front/rear locations) still need their
                // specification cells. Only a redundant heading can become a value list.
                if (primary.Count != 1) return false;
                var value = DisplayValue(primary[0], data);
                var numeric = NumericValue.Match(value);
                items.Add(new DescriptionItem {
                    Description = numeric.Success ? FormatNumber(primary[0].Field, value, numeric) : value,
                    Numeric = numeric.Success,
                    Fields = GroupFieldRows(record.Where(row => row != primary[0]), title)
                });
            }
            if (items.Count < 2) return false;
            AppendDescriptionItems(output, items, data, true);
            return true;
        }

        private static void AppendFieldNodes(StringBuilder output, IEnumerable<FieldNode> fields, JObject data) {
            var nodes = fields.ToList();
            DescriptionItem direct;
            if (TryDescriptionItem(nodes, data, out direct)) {
                AppendDescriptionGroup(output, direct.Category, new[] { direct }, data);
                return;
            }
            string namedTitle;
            List<FieldNode> namedFields;
            if (TryNamedItem(nodes, data, out namedTitle, out namedFields)) {
                AppendNamedItem(output, namedTitle, namedFields, data);
                return;
            }

            var records = new HashSet<FieldNode>();
            var categories = new Dictionary<string, List<DescriptionItem>>(StringComparer.Ordinal);
            foreach (var node in nodes.Where(node => node.Row == null)) {
                DescriptionItem item;
                if (!TryDescriptionItem(node.Children, data, out item)) continue;
                records.Add(node);
                List<DescriptionItem> items;
                if (!categories.TryGetValue(item.Category, out items)) {
                    items = new List<DescriptionItem>();
                    categories.Add(item.Category, items);
                }
                items.Add(item);
            }

            var categoriesEmitted = false;
            var ordinary = new List<FieldNode>();
            foreach (var field in nodes) {
                if (records.Contains(field)) {
                    AppendRows(output, ordinary, data);
                    ordinary.Clear();
                    if (!categoriesEmitted) {
                        foreach (var category in categories.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair.Key, StringComparer.Ordinal))
                            AppendDescriptionGroup(output, category.Key, category.Value, data);
                        categoriesEmitted = true;
                    }
                    continue;
                }
                if (field.Row != null) {
                    ordinary.Add(field);
                    continue;
                }
                AppendRows(output, ordinary, data);
                ordinary.Clear();
                if (TryNamedItem(field.Children, data, out namedTitle, out namedFields)) {
                    AppendNamedItem(output, namedTitle, namedFields, data);
                    continue;
                }
                output.Append("<details class=\"avd-tree avd-tree--nested avd-field-group\"><summary><span class=\"avd-tree__pm\"></span>")
                    .Append(Encode(field.Name)).Append("</summary><div class=\"avd-tree__content\">");
                AppendFieldNodes(output, field.Children, data);
                output.Append("</div></details>");
            }
            AppendRows(output, ordinary, data);
        }

        private static bool TryNamedItem(ICollection<FieldNode> fields, JObject data, out string title, out List<FieldNode> remaining) {
            title = null;
            remaining = null;
            FieldNode code = null, name = null;
            JObject record = null;
            foreach (var field in fields.Where(field => field.Row != null)) {
                var token = SourceToken(data, field.Row.Path);
                var property = token == null ? null : token.Parent as JProperty;
                if (property == null || (property.Name != "code" && property.Name != "name")) continue;
                var owner = property.Parent as JObject;
                if (owner == null || owner == data || record != null && record != owner) return false;
                record = owner;
                if (property.Name == "code") {
                    if (code != null || (token.Type != JTokenType.String && token.Type != JTokenType.Integer)
                        || string.IsNullOrWhiteSpace(Text(token))) return false;
                    code = field;
                } else {
                    if (name != null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)) return false;
                    name = field;
                }
            }
            if (code == null || name == null) return false;
            title = (string)record["name"];
            remaining = fields.Where(field => field != name).ToList();
            return true;
        }

        private static void AppendNamedItem(StringBuilder output, string title, IEnumerable<FieldNode> fields, JObject data) {
            output.Append("<details class=\"avd-tree avd-tree--nested avd-field-group avd-named-item-group\"><summary><span class=\"avd-tree__pm\"></span>")
                .Append(Encode(title)).Append("</summary><div class=\"avd-tree__content\">");
            AppendFieldNodes(output, fields, data);
            output.Append("</div></details>");
        }

        private static bool TryDescriptionItem(ICollection<FieldNode> fields, JObject data, out DescriptionItem item) {
            item = null;
            FieldNode category = null, description = null;
            JObject record = null;
            foreach (var field in fields.Where(field => field.Row != null)) {
                var token = SourceToken(data, field.Row.Path);
                var property = token == null ? null : token.Parent as JProperty;
                if (property == null || (property.Name != "category" && property.Name != "description")) continue;
                var owner = property.Parent as JObject;
                // Match real sibling source properties, never similarly named labels
                // from different records or the vehicle's root object.
                if (owner == null || owner == data || record != null && record != owner) return false;
                if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)) return false;
                record = owner;
                if (property.Name == "category") {
                    if (category != null) return false;
                    category = field;
                } else {
                    if (description != null) return false;
                    description = field;
                }
            }
            if (category == null || description == null || IsItemRecord(record)) return false;
            item = new DescriptionItem {
                Category = (string)record["category"], Description = (string)record["description"],
                Fields = fields.Where(field => field != category && field != description).ToList()
            };
            return true;
        }

        private static void AppendDescriptionGroup(StringBuilder output, string category, IEnumerable<DescriptionItem> items, JObject data) {
            output.Append("<details class=\"avd-tree avd-tree--nested avd-description-group\"><summary><span class=\"avd-tree__pm\"></span>")
                .Append(Encode(category)).Append("</summary><div class=\"avd-tree__content\">");
            AppendDescriptionItems(output, items, data);
            output.Append("</div></details>");
        }

        private static void AppendDescriptionItems(StringBuilder output, IEnumerable<DescriptionItem> items, JObject data, bool repeated = false) {
            output.Append("<ul class=\"avd-description-list").Append(repeated ? " avd-repeated-values" : string.Empty).Append("\">");
            foreach (var item in items) {
                output.Append(item.Numeric && item.Fields.Count == 0 ? "<li class=\"avd-numeric\">" : "<li>");
                if (item.Fields.Count == 0) {
                    output.Append(Encode(item.Description));
                } else {
                    output.Append("<details class=\"avd-tree avd-description-item\"><summary><span class=\"avd-tree__pm\"></span>")
                        .Append(item.Numeric ? "<span class=\"avd-numeric\">" : string.Empty)
                        .Append(Encode(item.Description)).Append(item.Numeric ? "</span>" : string.Empty)
                        .Append("</summary><div class=\"avd-tree__content\">");
                    AppendFieldNodes(output, item.Fields, data);
                    output.Append("</div></details>");
                }
                output.Append("</li>");
            }
            output.Append("</ul>");
        }

        private static bool IsItemRecord(JObject value) {
            return value["feature_type"] != null && value["description"] != null
                || value["item"] != null && value["attribute"] != null && value["value"] != null;
        }

        private static JObject SourceRecord(JObject data, string path) {
            var source = SourceToken(data, path);
            return source == null ? null : source.AncestorsAndSelf().OfType<JObject>().FirstOrDefault(IsItemRecord);
        }

        private static JToken SourceToken(JObject data, string path) {
            if (data == null || string.IsNullOrEmpty(path)) return null;
            try {
                return data.SelectToken(path, false);
            } catch (Newtonsoft.Json.JsonException) {
                return null;
            }
        }

        private static string Text(JToken token) {
            return token == null || token.Type == JTokenType.Null ? string.Empty
                : token is JValue ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static void AppendRows(StringBuilder output, ICollection<FieldNode> rows, JObject data) {
            if (rows.Count == 0) return;
            OpenTable(output);
            var index = 0;
            foreach (var field in rows) {
                if (index % 2 == 0) output.Append("<tr>");
                AppendPair(output, field.Row.Field, DisplayValue(field.Row, data), field.Name);
                if (++index % 2 == 0) output.Append("</tr>");
            }
            if (index % 2 != 0) output.Append("<td aria-hidden=\"true\"></td><td aria-hidden=\"true\"></td></tr>");
            output.Append("</tbody></table></div>");
        }

        private static string DisplayValue(NeoVinSpecification row, JObject data) {
            var token = SourceToken(data, row.Path);
            var record = token as JObject;
            if (record != null && IsItemRecord(record))
                token = record[record["feature_type"] != null && record["description"] != null ? "description" : "value"];
            // Request cultures may use decimal commas in flattened API rows. Format the
            // typed source number for display without changing those rows or the payload.
            var value = token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                ? Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture) : row.Value;
            return string.IsNullOrWhiteSpace(value) ? "—" : value;
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

        private sealed class FieldNode {
            internal readonly string Name;
            internal readonly NeoVinSpecification Row;
            internal readonly List<FieldNode> Children = new List<FieldNode>();
            internal FieldNode(string name, NeoVinSpecification row = null) { Name = name; Row = row; }
        }

        private sealed class DescriptionItem {
            internal string Category;
            internal string Description;
            internal List<FieldNode> Fields;
            internal bool Numeric;
        }

        private static void OpenTable(StringBuilder output) {
            output.Append("<div class=\"avd-table-wrap\" tabindex=\"0\"><table class=\"avd-table avd-table--pairs avd-table--neovin\"><tbody>");
        }

        private static void AppendPair(StringBuilder output, string field, string value, string label) {
            var numeric = NumericValue.Match(value);
            output.Append("<th scope=\"row\">").Append(Encode(label)).Append("</th><td");
            if (numeric.Success) output.Append(" class=\"avd-numeric\"");
            output.Append(">").Append(Encode(numeric.Success ? FormatNumber(field, value, numeric) : value)).Append("</td>");
        }

        private static string FormatNumber(string field, string value, Match numeric) {
            var whole = numeric.Groups["whole"].Value.Replace(",", string.Empty);
            // Years and identifiers are labels, even when their characters are all digits.
            if (IdentifierField.IsMatch(field ?? string.Empty) || whole.Length < 4 || whole[0] == '0') return value;
            // Group the text directly so provider precision and trailing zeroes survive unchanged.
            var formatted = new StringBuilder(numeric.Groups["sign"].Value);
            for (var index = 0; index < whole.Length; index++) {
                if (index > 0 && (whole.Length - index) % 3 == 0) formatted.Append(',');
                formatted.Append(whole[index]);
            }
            return formatted.Append(numeric.Groups["fraction"].Value).ToString();
        }

        private static string Encode(string value) { return WebUtility.HtmlEncode(value ?? string.Empty); }
    }
}
