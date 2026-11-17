using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

class NeoVinRegression {
    private const string Vin="4T1BE46K19U342407";
    private const string Fixture="{\"vin\":\"4T1BE46K19U342407\",\"year\":2009,\"make\":\"Toyota\",\"model\":\"Camry\",\"trim\":\"LE\",\"engine\":{\"liters\":2.4,\"cylinders\":4},\"features\":{\"STANDARD\":[{\"category\":\"Safety\",\"feature_type\":\"Airbags\",\"description\":\"Side curtain airbags\",\"provider_detail\":\"Retained\"}]},\"installed_equipment\":{\"STANDARD\":[{\"category\":\"Interior\",\"item\":\"Seats\",\"attribute\":\"Heating\",\"location\":\"driver\",\"value\":\"standard\"}]},\"options\":[\"One\",\"Two\"],\"empty\":[],\"warranty\":{\"transferable\":false,\"notes\":null}}";
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static int Main(string[] args){try{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Run(args).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}
    static async Task Run(string[] args){
        var input=new NeoVinRequest{Vin=" 4t1be46k19u342407 "};input.NormalizeAndValidate();Check(input.Vin==Vin,"NeoVIN accepts normalized VIN and rejects unsupported characters");
        var invalid=false;try{NeoVinRequest.NormalizeVin("I2345678901234567");}catch(ArgumentException){invalid=true;}Check(invalid,"invalid VIN is rejected before provider calls");
        var result=NeoVinResult.Create(Vin,Fixture);Check(result.RawJson==Fixture&&JToken.DeepEquals(JObject.Parse(Fixture),result.Data)&&result.ToJsonObject()["data"]!=null,"full nested provider payload is retained unchanged");
        var decodedUtc=new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc);var saved=NeoVinResult.Create(Vin,Fixture,decodedUtc);
        Check(saved.DecodedAtUtc==decodedUtc&&saved.DecodedAtUtc.Kind==DateTimeKind.Utc&&(string)saved.ToJsonObject()["decodedAtUtc"]==decodedUtc.ToString("o")&&NeoVinHtmlRenderer.Render(saved).Contains("2021-04-05 06:07 UTC"),"saved NeoVIN reports preserve their original UTC decode timestamp in HTML and JSON");
        Check(result.Specifications.Any(r=>r.Category=="Engine"&&r.Field=="Liters"&&r.Value=="2.4")&&result.Specifications.Any(r=>r.Field=="Item 2"&&r.Value=="Two"),"nested objects and arrays produce readable specification rows");
        Check(result.Specifications.Any(r=>r.Category.Contains("Safety")&&r.Field=="Airbags"&&r.Value=="Side curtain airbags")&&result.Specifications.Any(r=>r.Value=="Retained"),"feature category and description are readable while extra provider fields remain");
        Check(result.Specifications.Any(r=>r.Field=="Seats · Heating (driver)"&&r.Value=="standard"),"installed equipment preserves item, attribute and location");
        Check(result.Specifications.Any(r=>r.Value=="[]")&&result.Specifications.Any(r=>r.Value=="No")&&result.Specifications.Any(r=>r.Value=="Not provided"),"empty, boolean and null values remain represented");
        var html=NeoVinHtmlRenderer.Render(result);Check(html.Contains("2009 Toyota Camry LE")&&html.Contains("Side curtain airbags")&&html.Contains("Complete MarketCheck response"),"formatted report includes vehicle identity and every specification category");
        CheckSharedReport(result,html);
        CheckCategoryHierarchy();
        CheckRepeatedItems();
        CheckRedundantSpecificationValues();
        CheckFourColumnNumbers();
        CheckFieldPrefixGroups();
        CheckCategoryDescriptions();
        CheckHiddenAuditFields();
        CheckNamedCodeGroups();
        var hostile=NeoVinResult.Create(Vin,Fixture.Replace("Toyota","<script>alert(1)</script>"));html=NeoVinHtmlRenderer.Render(hostile);Check(!html.Contains("<script>alert(1)</script>")&&html.Contains("&lt;script&gt;"),"provider text is HTML encoded in summaries, rows and raw data");
        invalid=false;try{NeoVinResult.Create(Vin,Fixture.Replace(Vin,"1HGCM82633A004352"));}catch(NeoVinException){invalid=true;}Check(invalid,"a response for another VIN is never presented as the requested vehicle");
        invalid=false;try{NeoVinResult.Create(Vin,"{\"message\":\"unavailable\"}");}catch(NeoVinException){invalid=true;}Check(invalid,"provider error objects are not misrepresented as decoded specifications");
        var handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Fixture)}};
        using(var client=new HttpClient(handler)){
            var fetched=await new NeoVinService(client,"fixture-key").DecodeAsync(" "+Vin.ToLowerInvariant()+" ",CancellationToken.None);
            Check(handler.Uri.AbsolutePath=="/v2/decode/car/neovin/"+Vin+"/specs"&&handler.Uri.Host=="api.marketcheck.com"&&handler.Uri.Query=="?api_key=fixture-key"&&fetched.Specifications.Count>0,"NeoVIN sends the exact fixed provider URL with a server-side key");
        }
        foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,(HttpStatusCode)429}){
            handler=new Handler{Response=new HttpResponseMessage(status){Content=new StringContent("fixture-key private provider details")}};
            using(var client=new HttpClient(handler)){NeoVinException failure=null;try{await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,CancellationToken.None);}catch(NeoVinException ex){failure=ex;}Check(failure!=null&&!failure.Message.Contains("fixture-key")&&!failure.Message.Contains("private provider")&&(status!=(HttpStatusCode)429||failure.StatusCode==(HttpStatusCode)429),"NeoVIN safely reports provider HTTP "+(int)status);}
        }
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.Found){Content=new StringContent("redirect")}};handler.Response.Headers.Location=new Uri("https://other.example/");
        using(var client=new HttpClient(handler)){invalid=false;try{await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,CancellationToken.None);}catch(NeoVinException){invalid=true;}Check(invalid&&handler.Calls==1,"provider redirects never forward the API credential");}
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Fixture.Replace("Retained","fixture-key"))}};
        using(var client=new HttpClient(handler)){var redacted=await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,CancellationToken.None);Check(!redacted.RawJson.Contains("fixture-key")&&redacted.RawJson.Contains("[redacted]"),"unexpected provider credential echoes are redacted");}
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new WaitingStream())}};
        using(var client=new HttpClient(handler))using(var cancel=new CancellationTokenSource(40)){invalid=false;try{await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,cancel.Token);}catch(OperationCanceledException){invalid=true;}Check(invalid,"NeoVIN cancellation stops response-body reads");}
        if(args.Length==1){var sample=NeoVinResult.Create(Vin,File.ReadAllText(args[0]));Check(sample.Specifications.Count>100&&sample.VehicleTitle=="2009 Toyota Camry LE","cached sample preserves its complete specification set");CheckCategoryRows(sample,ReportDocument(NeoVinHtmlRenderer.Render(sample)));Console.WriteLine("Cached sample: "+sample.Specifications.Count+" specification rows across "+sample.Specifications.Select(r=>r.Category).Distinct().Count()+" categories.");}
    }
    static void CheckSharedReport(NeoVinResult result,string html){
        var neo=ReportDocument(html);
        var dataOne=ReportDocument(VinDecodeHtmlRenderer.Render("<decoded_data><us_styles><style name=\"LE\"><basic_data><year>2009</year><make>Toyota</make><model>Camry</model></basic_data></style></us_styles></decoded_data>",Vin));
        Check(neo.Root.Element("style").Value==dataOne.Root.Element("style").Value,"DataOne and NeoVIN embed the same self-contained report styles");
        Check(HasClass(neo.Root.Element("section"),"avd-report")&&HasClass(neo.Root.Element("section"),"neovin-report")&&HasClass(dataOne.Root.Element("section"),"avd-dataone"),"shared report styling preserves both provider root hooks");
        Check(neo.Descendants("details").Count(e=>HasClass(e,"avd-style"))==1&&neo.Descendants("summary").Any(e=>HasClass(e,"avd-style__toggle")),"NeoVIN uses the same expandable vehicle header as DataOne");
        var panels=neo.Descendants("details").Where(e=>HasClass(e,"avd-panel")).ToList();
        Check(panels.Count==result.Specifications.Select(row=>row.Category.Split(new[]{" / "},StringSplitOptions.None)[0]).Distinct().Count()+1,"each top-level category and raw payload remain shared panels without an added summary");
        CheckCategoryRows(result,neo);
        Check(neo.Descendants("pre").Single().Value==result.RawJson,"complete raw JSON survives report HTML encoding without changes");
        Check(!panels.Any(panel=>CategoryLabel(panel)=="Basic summary")&&ValueCells(CategoryElement(neo,"Vehicle")).Any(cell=>CellLabel(cell)=="VIN"&&cell.Value==Vin)&&!neo.Descendants("summary").Any(summary=>System.Text.RegularExpressions.Regex.IsMatch(summary.Value,@"\(\d+ fields\)")),"MarketCheck omits Basic summary while retaining the provider VIN in Vehicle without generated field counters");
        CheckFourColumnShape(neo);
        var hostile=NeoVinResult.Create(Vin,Fixture.Replace("Toyota","<script>alert(1)</script>").Replace("Safety","<img src=x onerror=alert(1)>").Replace("Seats","<b>Seats</b>"));
        var hostileDocument=ReportDocument(NeoVinHtmlRenderer.Render(hostile));
        Check(!hostileDocument.Descendants("script").Any()&&!hostileDocument.Descendants("img").Any()&&hostileDocument.Descendants("pre").Single().Value==hostile.RawJson,"shared header, category, fields and complete payload encode hostile provider text");
    }
    static void CheckCategoryRows(NeoVinResult result,XDocument report){
        foreach(var group in result.Specifications.GroupBy(row=>row.Category)){
            var category=CategoryElement(report,group.Key);
            var tables=category.Descendants("table").Where(table=>table.Ancestors("details").First(e=>!HasClass(e,"avd-item-group")&&!HasClass(e,"avd-field-group")&&!HasClass(e,"avd-description-item"))==category).ToList();
            var cells=tables.SelectMany(ValueCells).ToList();
            var values=RepeatedValues(category).Where(value=>value.Ancestors("details").First(e=>!HasClass(e,"avd-item-group")&&!HasClass(e,"avd-field-group")&&!HasClass(e,"avd-description-item"))==category);
            var displayed=cells.Select(cell=>FullCellLabel(cell)+"\n"+ComparableValue(cell.Value)).Concat(values.Select(value=>CategoryLabel(value.Ancestors("details").First(e=>HasClass(e,"avd-item-group")))+"\n"+ComparableValue(RepeatedValue(value))));
            Check(tables.All(table=>HasClass(table,"avd-table")&&HasClass(table,"avd-table--pairs")&&HasClass(table,"avd-table--neovin"))&&displayed.OrderBy(value=>value,StringComparer.Ordinal).SequenceEqual(group.Select(row=>row.Field+"\n"+ComparableValue(string.IsNullOrWhiteSpace(row.Value)?"—":row.Value)).OrderBy(value=>value,StringComparer.Ordinal)),"tables and value lists retain every field and value exactly once in "+group.Key);
        }
    }
    static void CheckCategoryHierarchy(){
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",["observations"]=42,
            ["features"]=new JObject{
                ["description"]="Feature metadata",
                ["STANDARD"]=new JArray(
                    new JObject{["category"]="Safety",["feature_type"]="Airbags",["description"]="Shared fact"},
                    new JObject{["category"]="Interior",["feature_type"]="Seats",["description"]="Cloth"},
                    new JObject{["category"]="Safety",["feature_type"]="Airbags",["description"]="Second fact"},
                    new JObject{["category"]="Safety",["feature_type"]="Airbags",["description"]="Shared fact"}),
                ["OPTIONAL"]=new JArray(new JObject{["category"]="Safety",["feature_type"]="Airbags",["description"]="Shared fact"})},
            ["installed_equipment"]=new JObject{
                ["description"]="Equipment metadata",
                ["STANDARD"]=new JArray(
                    new JObject{["category"]="Interior",["item"]="Seats",["attribute"]="Heating",["location"]="driver",["value"]="standard"},
                    new JObject{["category"]="Safety",["item"]="Brakes",["attribute"]="ABS",["value"]="standard"}),
                ["OPTIONAL"]=new JArray(new JObject{["category"]="Interior",["item"]="Seats",["attribute"]="Cooling",["location"]="driver",["value"]="optional"})},
            ["options"]=new JArray("One","Two"),["slash/category"]=new JObject{["value"]="An unspaced slash stays in one label"}
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        var originalRows=result.ToJsonObject()["specifications"].DeepClone();
        var html=NeoVinHtmlRenderer.Render(result);var report=ReportDocument(html);
        var content=report.Descendants("div").Single(e=>HasClass(e,"avd-style__content"));
        var top=content.Elements("details").Select(CategoryLabel).ToList();
        Check(top.SequenceEqual(new[]{"Vehicle","Features","Installed Equipment","Options","Slash/Category","Complete MarketCheck response"}),"repeated category prefixes appear once in their first-seen order without an added Basic summary or UI counters");
        var features=CategoryElement(report,"Features");var equipment=CategoryElement(report,"Installed Equipment");
        Check(features.Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Interior","Safety"})&&CategoryElement(report,"Features / Safety").Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Standard","Optional"}),"Features sorts category children alphabetically while keeping Standard before Optional");
        Check(equipment.Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Interior","Safety"})&&CategoryElement(report,"Installed Equipment / Interior").Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Standard","Optional"}),"Installed Equipment has independent nested Standard and Optional leaves");
        Check(report.Descendants("details").Where(e=>HasClass(e,"avd-tree")&&!HasClass(e,"avd-item-group")).All(e=>!CategoryLabel(e).Contains(" / ")),"nested disclosure labels omit their repeated parent prefixes");
        CheckCategoryRows(result,report);
        Check(report.Descendants("td").Count(cell=>cell.Value=="Shared fact")+RepeatedValues(report).Count(value=>RepeatedValue(value)=="Shared fact")==3&&report.Descendants("td").Any(cell=>cell.Value=="42"),"identical facts on the same and different paths and provider numeric values are never deduplicated");
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(originalRows,result.ToJsonObject()["specifications"])&&report.Descendants("footer").Single().Value.Contains("2021-04-05 06:07 UTC"),"hierarchical rendering preserves raw JSON, API specification metadata and the original decode time");
    }
    static XElement CategoryElement(XDocument report,string path){
        var content=report.Descendants("div").Single(e=>HasClass(e,"avd-style__content"));XElement category=null;
        foreach(var name in path.Split(new[]{" / "},StringSplitOptions.None)){
            category=content.Elements("details").Single(e=>!HasClass(e,"avd-item-group")&&CategoryLabel(e)==name);
            content=category.Element("div");
        }
        return category;
    }
    static void CheckRepeatedItems(){
        // These eight features and six equipment records use the cached provider's actual shape.
        var featureTypes=new[]{"Suspension Type","Suspension Type","Suspension Type","Suspension Type","Stabilizer Bar","Stabilizer Bar","Wheel Dependence","Wheel Dependence"};
        var descriptions=new[]{"Strut Suspension-Front","Strut Suspension-Rear","Coil Suspension-Front","Coil Suspension-Rear","Front Stabilizer Bar","Rear Stabilizer Bar","Independent Suspension-Front","Independent Suspension-Rear"};
        var features=new JArray(featureTypes.Select((type,index)=>new JObject{["category"]="Suspension",["feature_type"]=type,["description"]=descriptions[index]}));
        features[0]["provider_detail"]="Original suspension detail";
        features[0]["evidence"]=new JObject{["confidence"]=0.98,["quoted'field"]=new JArray("Retained evidence")};
        features.Add(new JObject{["category"]="Comfort",["feature_type"]="Power Windows",["description"]="Front power windows"});
        features.Add(new JObject{["category"]="Comfort",["feature_type"]="Power Windows",["description"]="Rear power windows"});
        features.Add(new JObject{["category"]="Comfort",["feature_type"]="Suspension Type",["description"]="Separate category fact"});
        features.Add(new JObject{["category"]="Comfort",["feature_type"]="Unique Type",["description"]="Single record",["provider_detail"]="Single record extra metadata"});
        var equipment=new JArray();
        foreach(var attribute in new[]{"type","stabilizer bar","wheel dependence"})
            foreach(var location in new[]{"front","rear"})
                equipment.Add(new JObject{["category"]="Suspension",["item"]="Suspension",["attribute"]=attribute,["location"]=location,["value"]=attribute=="type"?"strut":attribute=="stabilizer bar"?"yes":"independent"});
        equipment[0]["confidence"]=0.91;
        foreach(var location in new[]{"front","rear"})
            equipment.Add(new JObject{["category"]="Brakes",["item"]="Brakes",["attribute"]="type",["location"]=location,["value"]="disc"});
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",
            ["features"]=new JObject{["STANDARD"]=features,["OPTIONAL"]=new JArray(
                new JObject{["category"]="Suspension",["feature_type"]="Suspension Type",["description"]="Optional front suspension"},
                new JObject{["category"]="Suspension",["feature_type"]="Suspension Type",["description"]="Optional rear suspension"})},
            ["installed_equipment"]=new JObject{["STANDARD"]=equipment}
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        result.Specifications.Add(new NeoVinSpecification{Category="Unmapped",Field="Missing path",Value="Missing metadata stays visible"});
        result.Specifications.Add(new NeoVinSpecification{Category="Unmapped",Field="Unknown path",Value="Unknown metadata stays visible",Path="does_not_exist"});
        result.Specifications.Add(new NeoVinSpecification{Category="Unmapped",Field="Invalid path",Value="Malformed metadata stays visible",Path="features["});
        var originalRows=result.ToJsonObject()["specifications"].DeepClone();
        var html=NeoVinHtmlRenderer.Render(result);var report=ReportDocument(html);
        var standard=CategoryElement(report,"Features / Suspension / Standard");
        var groups=standard.Element("div").Elements("details").Where(e=>HasClass(e,"avd-item-group")).ToList();
        Check(groups.Select(CategoryLabel).SequenceEqual(new[]{"Suspension Type","Stabilizer Bar","Wheel Dependence"}),"repeated feature types become one child disclosure each in first-seen order");
        Check(groups.SelectMany(RepeatedValues).Select(RepeatedValue).SequenceEqual(descriptions),"all eight front and rear feature descriptions stay distinct and ordered within their value lists");
        Check(groups[0].Descendants("td").Any(cell=>cell.Value=="Original suspension detail")&&groups[0].Descendants("td").Any(cell=>cell.Value=="0.98")&&groups[0].Descendants("td").Any(cell=>cell.Value=="Retained evidence"),"extra descendant metadata follows its source feature including quoted JSON property paths");
        var installed=CategoryElement(report,"Installed Equipment / Suspension / Standard").Element("div").Elements("details").Where(e=>HasClass(e,"avd-item-group")).ToList();
        Check(installed.Select(CategoryLabel).SequenceEqual(new[]{"Suspension · type","Suspension · stabilizer bar","Suspension · wheel dependence"})&&installed.All(group=>ValueCells(group).Any(cell=>CellLabel(cell).EndsWith("(front)"))&&ValueCells(group).Any(cell=>CellLabel(cell).EndsWith("(rear)"))),"equipment groups exact item and attribute while front and rear remain explicit rows");
        Check(installed[0].Descendants("td").Any(cell=>cell.Value=="0.91"),"equipment confidence metadata stays with its owning item group");
        Check(CategoryElement(report,"Features / Comfort / Standard").Element("div").Elements("details").Where(e=>HasClass(e,"avd-item-group")).Select(CategoryLabel).SequenceEqual(new[]{"Power Windows"})&&CategoryElement(report,"Installed Equipment / Brakes / Standard").Element("div").Elements("details").Where(e=>HasClass(e,"avd-item-group")).Select(CategoryLabel).SequenceEqual(new[]{"Brakes · type"}),"repeated item grouping applies across feature and equipment types without treating single records as repeated items");
        Check(RepeatedValues(CategoryElement(report,"Features / Suspension / Optional").Element("div").Elements("details").Single()).Select(RepeatedValue).SequenceEqual(new[]{"Optional front suspension","Optional rear suspension"}),"repeated item groups stay isolated by complete category and Standard or Optional context");
        CheckCategoryRows(result,report);
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(originalRows,result.ToJsonObject()["specifications"])&&!report.Descendants("summary").Any(summary=>System.Text.RegularExpressions.Regex.IsMatch(summary.Value,@"\(\d+ fields\)")),"item grouping preserves exact raw JSON, every original field value and path, and omits UI counters");
    }
    static void CheckRedundantSpecificationValues(){
        const string type="Steering Wheel Function";
        var descriptions=new[]{"Height Adjustment Steering Wheel","Steering Wheel Telescopic Adjustment","Electric Adjustment Steering Wheel","Heated Steering Wheel","Multi Function Steering Wheel","Easy Entry Steering Wheel","Heated Steering Wheel"};
        var standard=new JArray(descriptions.Select(description=>new JObject{["category"]="Interior",["feature_type"]=type,["description"]=description}));
        standard[0]["provider_detail"]="Height control detail";
        standard[0]["confidence"]=0.98;
        standard[6]["provider_detail"]="Second heated wheel detail";
        standard[6]["identifier"]=new JObject{["value"]="1234567890"};
        standard.Add(new JObject{["category"]="Comfort",["feature_type"]=type,["description"]="Comfort function one"});
        standard.Add(new JObject{["category"]="Comfort",["feature_type"]=type,["description"]="Comfort function two"});
        standard.Add(new JObject{["category"]="Interior",["feature_type"]="Unique Feature",["description"]="Single feature remains visible"});
        standard.Add(new JObject{["category"]="Safety",["feature_type"]="<img src=x onerror=alert(1)>",["description"]="<b>Encoded feature value</b>"});
        standard.Add(new JObject{["category"]="Safety",["feature_type"]="<img src=x onerror=alert(1)>",["description"]="<script>Encoded second value</script>"});
        standard.Add(new JObject{["category"]="Dimensions",["feature_type"]="Load Limit",["description"]="12345.6700"});
        standard.Add(new JObject{["category"]="Dimensions",["feature_type"]="Load Limit",["description"]="-1234.5000"});
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",
            ["features"]=new JObject{
                ["STANDARD"]=standard,
                ["OPTIONAL"]=new JArray(
                    new JObject{["category"]="Interior",["feature_type"]=type,["description"]="Optional heated wheel"},
                    new JObject{["category"]="Interior",["feature_type"]=type,["description"]="Optional easy entry wheel"})},
            ["installed_equipment"]=new JObject{["STANDARD"]=new JArray(
                new JObject{["category"]="Interior",["item"]="Steering Controls",["attribute"]="Steering Controls",["value"]="heated"},
                new JObject{["category"]="Interior",["item"]="Steering Controls",["attribute"]="Steering Controls",["value"]="multi-function"})}
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        var original=result.ToJsonObject().DeepClone();
        var html=NeoVinHtmlRenderer.Render(result);var report=ReportDocument(html);
        CheckFourColumnShape(report);
        CheckCategoryRows(result,report);
        var interior=CategoryElement(report,"Features / Interior / Standard");
        var wheel=interior.Element("div").Elements("details").Single(group=>HasClass(group,"avd-item-group")&&CategoryLabel(group)==type);
        Check(RepeatedValues(wheel).Select(RepeatedValue).SequenceEqual(descriptions),"repeated steering-wheel specifications become a value list retaining all six functions and a duplicate in provider order");
        Check(!wheel.Descendants("th").Any(cell=>cell.Value==type)&&wheel.Element("div").Elements("ul").Single(list=>HasClass(list,"avd-repeated-values")).Elements("li").Count()==descriptions.Length,"repeated feature values omit redundant Specification and Value tables under their existing feature heading");
        var entries=RepeatedValues(wheel).ToList();
        Check(ValueCells(entries[0]).Any(cell=>cell.Value=="Height control detail")&&ValueCells(entries[0]).Any(cell=>cell.Value=="0.98"&&HasClass(cell,"avd-numeric"))&&!ValueCells(entries[0]).Any(cell=>cell.Value=="Second heated wheel detail")&&ValueCells(entries[6]).Any(cell=>cell.Value=="Second heated wheel detail")&&ValueCells(entries[6]).Any(cell=>cell.Value=="1234567890"),"extra metadata stays attached to its exact source feature even when description values repeat");
        Check(!wheel.Descendants("details").Any(group=>HasClass(group,"avd-field-group")&&CategoryLabel(group)==type),"expanded value metadata omits a redundant copy of the enclosing feature heading");
        Check(RepeatedValues(CategoryElement(report,"Features / Comfort / Standard")).Select(RepeatedValue).SequenceEqual(new[]{"Comfort function one","Comfort function two"})&&RepeatedValues(CategoryElement(report,"Features / Interior / Optional")).Select(RepeatedValue).SequenceEqual(new[]{"Optional heated wheel","Optional easy entry wheel"}),"matching feature titles stay independent by source category and Standard or Optional context");
        Check(ValueCells(interior).Any(cell=>CellLabel(cell)=="Unique Feature"&&cell.Value=="Single feature remains visible"),"single feature records keep their existing readable table presentation");
        Check(!report.Descendants("img").Any()&&!report.Descendants("script").Any()&&!report.Descendants("b").Any(element=>element.Value=="Encoded feature value")&&RepeatedValues(CategoryElement(report,"Features / Safety / Standard")).Select(RepeatedValue).SequenceEqual(new[]{"<b>Encoded feature value</b>","<script>Encoded second value</script>"}),"repeated feature headings and values encode hostile provider text");
        Check(RepeatedValues(CategoryElement(report,"Features / Dimensions / Standard")).Select(RepeatedValue).SequenceEqual(new[]{"12,345.6700","-1,234.5000"}),"value-only feature lists retain numeric grouping, signs and decimal precision");
        var controls=CategoryElement(report,"Installed Equipment / Interior / Standard").Element("div").Elements("details").Single(group=>HasClass(group,"avd-item-group")&&CategoryLabel(group)=="Steering Controls");
        Check(RepeatedValues(controls).Select(RepeatedValue).SequenceEqual(new[]{"heated","multi-function"})&&!controls.Descendants("table").Any(),"equipment rows whose labels only repeat their heading use the same value-only list presentation");
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(original,result.ToJsonObject()),"repeated value lists leave raw JSON, all API rows and the decoded timestamp unchanged");
    }
    static void CheckFourColumnNumbers(){
        var raw="{\"vin\":\""+Vin+"\",\"year\":2009,\"make\":\"Toyota\",\"model\":\"Camry\",\"mileage\":12345,\"msrp\":\"12345.6700\",\"adjustment\":\"-1234.5000\",\"confidence\":0.98,\"amount\":1234.5,\"code\":\"00123\",\"identifier\":\"1234567890\",\"serial_number\":\"12345678901234567890\",\"precise_amount\":\"123456789012345678901234567890.12345678901234567890\",\"displacement\":\"2.4 L\",\"specs\":{\"single\":10},\"installed_equipment\":{\"STANDARD\":[{\"category\":\"Dimensions\",\"item\":\"Weight\",\"attribute\":\"Weight\",\"location\":\"front\",\"value\":\"1234.50\"}]}}";
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        var original=result.ToJsonObject().DeepClone();
        var culture=Thread.CurrentThread.CurrentCulture;
        string html;
        try{Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");html=NeoVinHtmlRenderer.Render(result);}
        finally{Thread.CurrentThread.CurrentCulture=culture;}
        var report=ReportDocument(html);
        CheckFourColumnShape(report);
        CheckCategoryRows(result,report);
        var cells=ValueCells(CategoryElement(report,"Vehicle")).ToDictionary(CellLabel,cell=>cell);
        Check(cells["Mileage"].Value=="12,345"&&cells["MSRP"].Value=="12,345.6700"&&cells["Adjustment"].Value=="-1,234.5000"&&cells["Confidence"].Value=="0.98","numeric display adds US grouping while preserving signs and decimal precision independently of host culture");
        Check(new[]{"Mileage","MSRP","Adjustment","Confidence","Amount","Precise Amount","Year","Code","Identifier","Serial Number"}.All(label=>HasClass(cells[label],"avd-numeric")),"all numeric values use the right-aligned numeric cell hook, including identifier digits");
        Check(cells["Year"].Value=="2009"&&cells["Code"].Value=="00123"&&cells["Identifier"].Value=="1234567890"&&cells["Serial Number"].Value=="12345678901234567890"&&cells["VIN"].Value==Vin,"years, leading zeros, identifiers, serial numbers and VINs retain their exact text");
        Check(cells["Precise Amount"].Value=="123,456,789,012,345,678,901,234,567,890.12345678901234567890","formatting never rounds long integer or fractional digit sequences");
        Check(cells["Displacement"].Value=="2.4 L"&&!HasClass(cells["Displacement"],"avd-numeric"),"values with units remain verbatim text");
        var equipment=ValueCells(CategoryElement(report,"Installed Equipment / Dimensions / Standard")).Single();
        Check(equipment.Value=="1,234.50"&&HasClass(equipment,"avd-numeric"),"equipment numeric values receive the same formatting as vehicle fields");
        var single=CategoryElement(report,"Specs").Descendants("tbody").Elements("tr").Single().Elements().ToList();
        Check(single.Count==4&&single.Skip(2).All(cell=>cell.Name=="td"&&cell.Value==string.Empty&&(string)cell.Attribute("aria-hidden")=="true"),"an odd final specification keeps four cells without inventing an extra field");
        Check(report.Descendants("pre").Single().Value==raw&&JToken.DeepEquals(original,result.ToJsonObject()),"four-column numeric formatting leaves the raw payload, API rows and original timestamp untouched");
        NeoVinResult localized;
        JToken localizedOriginal;
        XDocument localizedReport;
        try{
            Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");
            localized=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
            localizedOriginal=localized.ToJsonObject().DeepClone();
            localizedReport=ReportDocument(NeoVinHtmlRenderer.Render(localized));
        }finally{Thread.CurrentThread.CurrentCulture=culture;}
        var localizedCells=ValueCells(CategoryElement(localizedReport,"Vehicle")).ToDictionary(CellLabel,cell=>cell);
        Check(localizedCells["Confidence"].Value=="0.98"&&localizedCells["Amount"].Value=="1,234.5"&&new[]{"Confidence","Amount"}.All(label=>HasClass(localizedCells[label],"avd-numeric")),"typed JSON decimals normalize to US numeric display when decoding and rendering under a comma-decimal culture");
        Check(localizedCells["MSRP"].Value=="12,345.6700"&&localizedReport.Descendants("pre").Single().Value==raw&&JToken.DeepEquals(localizedOriginal,localized.ToJsonObject()),"culture normalization preserves string decimal precision, raw JSON and the original API specification values");
    }
    static void CheckFourColumnShape(XDocument report){
        var tables=report.Descendants("table").ToList();
        Check(tables.Count>0&&tables.All(table=>HasClass(table,"avd-table--pairs")&&HasClass(table,"avd-table--neovin")&&table.Element("thead")==null&&table.Element("tbody").Elements("tr").All(row=>row.Elements().Count()==4&&row.Elements("th").All(cell=>(string)cell.Attribute("scope")=="row"))),"every MarketCheck field table omits column headers while retaining four body cells and scoped row labels");
    }
    static void CheckFieldPrefixGroups(){
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",
            ["warranty"]=new JObject{
                ["total"]=new JObject{["duration"]=48,["distance"]=50000},
                ["powertrain"]=new JObject{["duration"]=72,["distance"]=70000},
                ["anti_corrosion"]=new JObject{["duration"]=60,["distance"]=999999},
                ["roadside_assistance"]=new JObject{["duration"]=48}},
            ["service_plans"]=new JObject{["total"]=new JObject{["duration"]=12,["distance"]=10000}},
            ["packages"]=new JArray(
                new JObject{["name"]="Shared package",["limits"]=new JObject{["duration"]=24,["distance"]=50000},["notes"]=null},
                new JObject{["name"]="Shared package",["limits"]=new JObject{["duration"]=36,["distance"]=50000},["notes"]=null}),
            ["inspection"]=new JObject{["<img src=x onerror=alert(1)>"]=new JObject{["<script>field</script>"]="<b>Original value</b>",["detail"]="Kept"},["slash/name"]="Literal slash label",["identifier"]=new JObject{["value"]="1234567890",["type"]="stock"}}
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        result.Specifications.Add(new NeoVinSpecification{Category="Warranty",Field="Total / Notes",Value="Nonadjacent coverage note"});
        var original=result.ToJsonObject().DeepClone();
        var html=NeoVinHtmlRenderer.Render(result);var report=ReportDocument(html);
        CheckFourColumnShape(report);
        CheckCategoryRows(result,report);
        var warranty=CategoryElement(report,"Warranty");
        var warrantyGroups=warranty.Element("div").Elements("details").Where(e=>HasClass(e,"avd-field-group")).ToList();
        Check(warrantyGroups.Select(CategoryLabel).SequenceEqual(new[]{"Total","Powertrain","Anti Corrosion","Roadside Assistance"}),"warranty field prefixes become single headings in their first-seen order");
        var total=FieldGroup(warranty,"Total");
        Check(ValueCells(total).Select(CellLabel).SequenceEqual(new[]{"Duration","Distance","Notes"})&&ValueCells(total).Select(cell=>cell.Value).SequenceEqual(new[]{"48","50,000","Nonadjacent coverage note"}),"Total groups both limits and a later matching prefix with local labels and retained numeric formatting");
        Check(ValueCells(FieldGroup(warranty,"Powertrain")).Select(cell=>cell.Value).SequenceEqual(new[]{"72","70,000"})&&ValueCells(FieldGroup(warranty,"Anti Corrosion")).Select(cell=>cell.Value).SequenceEqual(new[]{"60","999,999"}),"Powertrain and Anti Corrosion retain their own duration and distance pairs");
        Check(ValueCells(FieldGroup(warranty,"Roadside Assistance")).Single().Value=="48"&&!ValueCells(FieldGroup(warranty,"Roadside Assistance")).Any(cell=>CellLabel(cell)=="Distance"),"missing roadside distance is not invented by prefix grouping");
        Check(ValueCells(FieldGroup(CategoryElement(report,"Service Plans"),"Total")).Select(cell=>cell.Value).SequenceEqual(new[]{"12","10,000"}),"matching field prefixes in different categories keep independent coverage values");
        var packages=CategoryElement(report,"Packages");
        Check(packages.Element("div").Elements("details").Where(e=>HasClass(e,"avd-field-group")).Select(CategoryLabel).SequenceEqual(new[]{"Item 1","Item 2"}),"numbered object records get separate Item headings instead of merged matching field names");
        var first=FieldGroup(packages,"Item 1 / Limits");var second=FieldGroup(packages,"Item 2 / Limits");
        Check(ValueCells(first).Select(CellLabel).SequenceEqual(new[]{"Duration","Distance"})&&ValueCells(first).Select(cell=>cell.Value).SequenceEqual(new[]{"24","50,000"})&&ValueCells(second).Select(cell=>cell.Value).SequenceEqual(new[]{"36","50,000"}),"recursive Item and Limits groups preserve duplicate values and their original record associations");
        Check(ValueCells(packages).Count(cell=>cell.Value=="Shared package")==2&&ValueCells(packages).Count(cell=>cell.Value=="Not provided")==2,"duplicate descriptions and explicit null fields remain visible for both numbered records");
        Check(!report.Descendants("img").Any()&&!report.Descendants("script").Any()&&ValueCells(CategoryElement(report,"Inspection")).Any(cell=>CellLabel(cell)=="Slash/Name"&&cell.Value=="Literal slash label"),"hostile field prefixes are encoded and unspaced slashes stay literal labels");
        var identifier=ValueCells(FieldGroup(CategoryElement(report,"Inspection"),"Identifier")).Single(cell=>CellLabel(cell)=="Value");
        Check(identifier.Value=="1234567890"&&HasClass(identifier,"avd-numeric")&&FullCellLabel(identifier)=="Identifier / Value","local Value labels retain full identifier context for exact numeric text and right alignment");
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(report.Descendants("details").Where(e=>HasClass(e,"avd-field-group")).All(e=>!CategoryLabel(e).Contains(" / "))&&WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(original,result.ToJsonObject()),"recursive field grouping removes repeated display prefixes without changing raw JSON, API rows or decode time");
    }
    static void CheckCategoryDescriptions(){
        const string safety="Safety & Driver Assist";
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",
            ["generic_features"]=new JObject{
                ["STANDARD"]=new JArray(
                    new JObject{["category"]=safety,["description"]="Parking Distance System"},
                    new JObject{["category"]="Infotainment",["description"]="Premium Speakers"},
                    new JObject{["category"]="Exterior",["description"]="Roof Rails"},
                    new JObject{["category"]=safety,["description"]="Parking Assistance System"},
                    new JObject{["category"]="Interior",["description"]="Heated Seats"},
                    new JObject{["category"]=safety,["description"]="360 View Parking Device"},
                    new JObject{["category"]=safety,["description"]="Parking Distance System"},
                    new JObject{["category"]="<img src=x onerror=alert(1)>",["description"]="<b>Encoded description</b>"}),
                ["OPTIONAL"]=new JArray(new JObject{["category"]=safety,["description"]="Optional Parking Sensor"})},
            ["packages"]=new JObject{
                ["primary"]=new JArray(new JObject{["category"]=safety,["description"]="Primary package parking"}),
                ["secondary"]=new JArray(new JObject{["category"]=safety,["description"]="Secondary package parking"})},
            ["additional_features"]=new JArray(new JObject{["category"]=safety,["description"]="Separate source category"}),
            ["single_feature"]=new JObject{["category"]=safety,["description"]="Named object parking feature"},
            ["feature_details"]=new JArray(new JObject{
                ["category"]=safety,["description"]="Rear Parking Camera",["confidence"]=0.98,
                ["provider_detail"]="Original camera detail",["limits"]=new JObject{["distance"]="12345.6700"},
                ["identifier"]=new JObject{["value"]="1234567890"},["notes"]=null})
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        var original=result.ToJsonObject().DeepClone();
        var html=NeoVinHtmlRenderer.Render(result);var report=ReportDocument(html);
        CheckFourColumnShape(report);
        var standard=FieldGroup(CategoryElement(report,"Generic Features"),"Standard");
        var safetyGroup=DescriptionGroup(standard,safety);
        Check(standard.Element("div").Elements("details").Where(e=>HasClass(e,"avd-description-group")).Select(CategoryLabel).SequenceEqual(new[]{"<img src=x onerror=alert(1)>","Exterior","Infotainment","Interior",safety}),"category/description headings form one alphabetical stack by category value despite provider interleaving");
        Check(DescriptionValues(DescriptionGroup(standard,"Exterior")).SequenceEqual(new[]{"Roof Rails"})&&DescriptionValues(DescriptionGroup(standard,"Interior")).SequenceEqual(new[]{"Heated Seats"}),"alphabetical category ordering keeps each exterior and interior description under its original category");
        Check(DescriptionValues(safetyGroup).SequenceEqual(new[]{"Parking Distance System","Parking Assistance System","360 View Parking Device","Parking Distance System"}),"all three parking descriptions and repeated descriptions remain listed in provider order");
        Check(DescriptionValues(DescriptionGroup(standard,"Infotainment")).SequenceEqual(new[]{"Premium Speakers"})&&!standard.Descendants("details").Any(e=>HasClass(e,"avd-field-group")&&System.Text.RegularExpressions.Regex.IsMatch(CategoryLabel(e),@"^Item \d+$"))&&!ValueCells(standard).Any(),"plain category descriptions replace numbered Item headings and Category/Description tables with lists");
        Check(DescriptionValues(DescriptionGroup(FieldGroup(CategoryElement(report,"Generic Features"),"Optional"),safety)).SequenceEqual(new[]{"Optional Parking Sensor"}),"Standard and Optional descriptions remain independent even when category names match");
        var packages=CategoryElement(report,"Packages");
        Check(DescriptionValues(DescriptionGroup(FieldGroup(packages,"Primary"),safety)).SequenceEqual(new[]{"Primary package parking"})&&DescriptionValues(DescriptionGroup(FieldGroup(packages,"Secondary"),safety)).SequenceEqual(new[]{"Secondary package parking"}),"matching categories in sibling arrays keep separate parent group scopes");
        Check(DescriptionValues(DescriptionGroup(CategoryElement(report,"Additional Features"),safety)).SequenceEqual(new[]{"Separate source category"}),"matching description categories in different top-level sources never merge");
        Check(DescriptionValues(DescriptionGroup(CategoryElement(report,"Single Feature"),safety)).SequenceEqual(new[]{"Named object parking feature"}),"category/description grouping also applies to a named object without an Item prefix");
        var details=DescriptionGroup(CategoryElement(report,"Feature Details"),safety);
        var camera=details.Descendants("details").Single(e=>CategoryLabel(e)=="Rear Parking Camera");
        Check(ValueCells(camera).Any(cell=>CellLabel(cell)=="Confidence"&&cell.Value=="0.98"&&HasClass(cell,"avd-numeric"))&&ValueCells(camera).Any(cell=>cell.Value=="Original camera detail")&&ValueCells(camera).Any(cell=>CellLabel(cell)=="Distance"&&cell.Value=="12,345.6700")&&ValueCells(camera).Any(cell=>CellLabel(cell)=="Value"&&cell.Value=="1234567890"&&HasClass(cell,"avd-numeric"))&&ValueCells(camera).Any(cell=>CellLabel(cell)=="Notes"&&cell.Value=="Not provided"),"extra fields remain attached to their description with nested metadata, numeric precision and identifiers intact");
        Check(!report.Descendants("img").Any()&&!report.Descendants("b").Any(e=>e.Value=="Encoded description")&&DescriptionValues(DescriptionGroup(standard,"<img src=x onerror=alert(1)>")).Single()=="<b>Encoded description</b>","category headings and plain descriptions encode hostile provider text");
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(original,result.ToJsonObject()),"description regrouping leaves raw JSON, original API rows and decoded timestamp unchanged");

        var fallbackRaw=new JObject{
            ["vin"]=Vin,["year"]=2009,["fallbacks"]=new JArray(
                new JObject{["description"]="Missing category"},
                new JObject{["category"]=" ",["description"]="Blank category"},
                new JObject{["category"]=42,["description"]="Numeric category"},
                new JObject{["category"]=new JObject{["name"]="Nested"},["description"]="Object category"},
                new JObject{["category"]=safety},
                new JObject{["category"]=safety,["description"]=" "},
                new JObject{["category"]=safety,["description"]=1234},
                new JObject{["category"]=safety,["description"]=new JObject{["text"]="Nested description"}})
        }.ToString();
        var fallback=NeoVinResult.Create(Vin,fallbackRaw);var fallbackReport=ReportDocument(NeoVinHtmlRenderer.Render(fallback));
        var fallbackCategory=CategoryElement(fallbackReport,"Fallbacks");
        Check(!fallbackCategory.Descendants("details").Any(e=>HasClass(e,"avd-description-group"))&&fallbackCategory.Element("div").Elements("details").Where(e=>HasClass(e,"avd-field-group")).Select(CategoryLabel).SequenceEqual(Enumerable.Range(1,8).Select(index=>"Item "+index)),"missing, blank and nonstring category or description values retain numbered record tables");
        CheckCategoryRows(fallback,fallbackReport);
    }
    static void CheckHiddenAuditFields(){
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",
            ["created"]="AUDIT_TOP_CREATED",["updated"]="AUDIT_TOP_UPDATED",
            ["Created"]=new JObject{["by"]="AUDIT_CREATED_OWNER",["details"]=new JObject{["value"]="AUDIT_CREATED_CHILD"}},
            ["Updated"]=new JObject{["by"]="AUDIT_UPDATED_OWNER"},
            ["engine"]=new JObject{["created_at"]="AUDIT_ENGINE_CREATED",["updatedAt"]="AUDIT_ENGINE_UPDATED",["last_updated"]="AUDIT_ENGINE_LAST_UPDATED",["createdBy"]="AUDIT_ENGINE_CREATOR",["horsepower"]=158,["notes"]="Created and Updated remain ordinary description words"},
            ["features"]=new JObject{["STANDARD"]=new JArray(
                new JObject{["category"]="Safety",["feature_type"]="Steering Wheel Function",["description"]="Updated steering wheel controls",["created_at"]="AUDIT_FEATURE_CREATED",["updated"]=new JObject{["by"]="AUDIT_FEATURE_UPDATED"}},
                new JObject{["category"]="Safety",["feature_type"]="Steering Wheel Function",["description"]="Heated Steering Wheel",["updatedAt"]="AUDIT_SECOND_FEATURE_UPDATED"})},
            ["generic_features"]=new JArray(new JObject{["category"]="Safety",["description"]="Updated Parking Device",["created_at"]="AUDIT_GENERIC_CREATED",["updatedAt"]="AUDIT_GENERIC_UPDATED"})
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        result.Specifications.Add(new NeoVinSpecification{Category="Unmapped",Field="Created At",Value="AUDIT_UNRESOLVED_CREATED",Path="missing"});
        result.Specifications.Add(new NeoVinSpecification{Category="Updated / History",Field="Value",Value="AUDIT_UNRESOLVED_UPDATED"});
        var original=result.ToJsonObject().DeepClone();
        var report=ReportDocument(NeoVinHtmlRenderer.Render(result));
        var visible=new XElement(report.Root.Element("section"));visible.Descendants("pre").Remove();
        Check(!visible.Value.Contains("AUDIT_"),"Created and Updated fields and all nested audit metadata stay out of the formatted report");
        var omitted=new[]{"Basic summary","Created","Updated","Created At","Updated At","Last Updated","Created By","Unmapped"};
        Check(!report.Descendants("details").Any(details=>omitted.Contains(CategoryLabel(details)))&&!report.Descendants("tbody").Descendants("th").Any(cell=>omitted.Contains(cell.Value)),"omitted audit-only groups and Basic summary leave no empty headings or field labels");
        Check(ValueCells(CategoryElement(report,"Vehicle")).Any(cell=>CellLabel(cell)=="VIN"&&cell.Value==Vin)&&ValueCells(CategoryElement(report,"Engine")).Any(cell=>CellLabel(cell)=="Horsepower"&&cell.Value=="158"),"removing generated summary and audit fields retains provider VIN and vehicle specifications");
        Check(visible.Value.Contains("Updated steering wheel controls")&&visible.Value.Contains("Updated Parking Device")&&visible.Value.Contains("Created and Updated remain ordinary description words"),"audit filtering classifies source property names rather than words inside description values");
        Check(!report.Descendants("details").Any(details=>HasClass(details,"avd-description-item")),"description entries with only hidden audit metadata remain plain values without empty expansion controls");
        var rawHtml=System.Text.RegularExpressions.Regex.Match(NeoVinHtmlRenderer.Render(result),"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(original,result.ToJsonObject()),"hiding audit fields leaves the complete raw response, original API rows and decoded timestamp unchanged");
    }
    static void CheckNamedCodeGroups(){
        var raw=new JObject{
            ["vin"]=Vin,["year"]=2009,["make"]="Toyota",["model"]="Camry",
            ["options"]=new JArray(
                new JObject{["code"]="F02",["name"]="Rear Sonar System",["msrp"]=250,["type"]="O",["confidence"]="High",["verified"]=null,["rule"]="Unspecified",["sale_price"]=250,["ref_msrp"]=250},
                new JObject{["code"]="F03",["name"]="Back Door Glass Package",["msrp"]=190,["type"]="P",["confidence"]="High",["verified"]=null,["rule"]="Unspecified",["sale_price"]=190,["ref_msrp"]=190},
                new JObject{["code"]="00123",["name"]="Rear Sonar System",["msrp"]="12345.6700",["limits"]=new JObject{["distance"]=12000},["updated_at"]="AUDIT_NAMED_OPTION"},
                new JObject{["code"]=1234,["name"]="Numeric Code Option",["msrp"]=1400}),
            ["option_groups"]=new JObject{
                ["STANDARD"]=new JArray(new JObject{["code"]="STD",["name"]="Shared Package",["msrp"]=100}),
                ["OPTIONAL"]=new JArray(new JObject{["code"]="OPT",["name"]="Shared Package",["msrp"]=200})},
            ["bundles"]=new JObject{
                ["primary"]=new JArray(new JObject{["code"]="PRIMARY",["name"]="Shared Package"}),
                ["secondary"]=new JArray(new JObject{["code"]="SECONDARY",["name"]="Shared Package"})},
            ["single_option"]=new JObject{["code"]="SINGLE",["name"]="Interior Bundle",["confidence"]="Confirmed"},
            ["encoded_options"]=new JArray(new JObject{["code"]="<img src=x onerror=alert(1)>",["name"]="<script>Named package</script>",["description"]="<b>Retained description</b>"})
        }.ToString();
        var result=NeoVinResult.Create(Vin,raw,new DateTime(2021,4,5,6,7,8,DateTimeKind.Utc));
        var original=result.ToJsonObject().DeepClone();
        var html=NeoVinHtmlRenderer.Render(result);var report=ReportDocument(html);
        CheckFourColumnShape(report);
        var options=CategoryElement(report,"Options");var named=NamedItems(options).ToList();
        Check(named.Select(CategoryLabel).SequenceEqual(new[]{"Rear Sonar System","Back Door Glass Package","Rear Sonar System","Numeric Code Option"})&&!options.Descendants("details").Any(group=>System.Text.RegularExpressions.Regex.IsMatch(CategoryLabel(group),@"^Item \d+$")),"Code and Name option records use their Name as the heading instead of numbered Item labels");
        Check(named.Count==4&&named.Select(group=>ValueCells(group).Single(cell=>CellLabel(cell)=="Code").Value).SequenceEqual(new[]{"F02","F03","00123","1234"}),"duplicate names keep separate source records and every Code remains a table row");
        Check(named.All(group=>!ValueCells(group).Any(cell=>CellLabel(cell)=="Name")),"the promoted Name is not repeated in its own field table");
        var sonar=ValueCells(named[0]).ToDictionary(CellLabel,cell=>cell.Value);
        var glass=ValueCells(named[1]).ToDictionary(CellLabel,cell=>cell.Value);
        Check(sonar["Code"]=="F02"&&sonar["MSRP"]=="250"&&sonar["Type"]=="O"&&sonar["Confidence"]=="High"&&sonar["Verified"]=="Not provided"&&sonar["Rule"]=="Unspecified"&&sonar["Sale Price"]=="250"&&sonar["Ref MSRP"]=="250","Rear Sonar System retains all original option details beneath its name heading");
        Check(glass["Code"]=="F03"&&glass["MSRP"]=="190"&&glass["Type"]=="P"&&glass["Confidence"]=="High"&&glass["Verified"]=="Not provided"&&glass["Rule"]=="Unspecified"&&glass["Sale Price"]=="190"&&glass["Ref MSRP"]=="190","Back Door Glass Package keeps its own code, prices and metadata");
        Check(ValueCells(named[2]).Any(cell=>CellLabel(cell)=="Code"&&cell.Value=="00123"&&HasClass(cell,"avd-numeric"))&&ValueCells(named[2]).Any(cell=>CellLabel(cell)=="MSRP"&&cell.Value=="12,345.6700")&&ValueCells(named[2]).Any(cell=>CellLabel(cell)=="Distance"&&cell.Value=="12,000"),"name headings preserve exact numeric codes, decimal price precision and nested option fields");
        Check(ValueCells(named[3]).Any(cell=>CellLabel(cell)=="Code"&&cell.Value=="1234"&&HasClass(cell,"avd-numeric"))&&ValueCells(named[3]).Any(cell=>CellLabel(cell)=="MSRP"&&cell.Value=="1,400"),"integer option codes can name a record without becoming grouped price-like numbers");
        var optionGroups=CategoryElement(report,"Option Groups");
        Check(ValueCells(NamedItems(FieldGroup(optionGroups,"Standard")).Single()).Single(cell=>CellLabel(cell)=="Code").Value=="STD"&&ValueCells(NamedItems(FieldGroup(optionGroups,"Optional")).Single()).Single(cell=>CellLabel(cell)=="Code").Value=="OPT","same-name options stay separate between Standard and Optional sections");
        var bundles=CategoryElement(report,"Bundles");
        Check(ValueCells(NamedItems(FieldGroup(bundles,"Primary")).Single()).Single(cell=>CellLabel(cell)=="Code").Value=="PRIMARY"&&ValueCells(NamedItems(FieldGroup(bundles,"Secondary")).Single()).Single(cell=>CellLabel(cell)=="Code").Value=="SECONDARY","matching named option records in sibling collections retain their original parent scopes");
        var single=NamedItems(CategoryElement(report,"Single Option")).Single();
        Check(CategoryLabel(single)=="Interior Bundle"&&ValueCells(single).Any(cell=>CellLabel(cell)=="Code"&&cell.Value=="SINGLE"),"Code and Name grouping also works for a named object outside an array");
        var encoded=NamedItems(CategoryElement(report,"Encoded Options")).Single();
        Check(CategoryLabel(encoded)=="<script>Named package</script>"&&!report.Descendants("script").Any()&&!report.Descendants("img").Any()&&ValueCells(encoded).Any(cell=>CellLabel(cell)=="Code"&&cell.Value=="<img src=x onerror=alert(1)>")&&ValueCells(encoded).Any(cell=>cell.Value=="<b>Retained description</b>"),"promoted option names, codes and other values remain HTML encoded");
        var visible=new XElement(report.Root.Element("section"));visible.Descendants("pre").Remove();
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(!visible.Value.Contains("AUDIT_NAMED_OPTION")&&WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(original,result.ToJsonObject()),"option heading changes retain audit filtering, the complete raw payload, API specifications and original decode time");

        var fallbackRaw=new JObject{
            ["vin"]=Vin,["year"]=2009,["fallback_options"]=new JArray(
                new JObject{["code"]="MISSING_NAME"},
                new JObject{["code"]="BLANK_NAME",["name"]=" "},
                new JObject{["code"]="NUMBER_NAME",["name"]=42},
                new JObject{["code"]="OBJECT_NAME",["name"]=new JObject{["text"]="Nested name"}},
                new JObject{["name"]="Missing Code"},
                new JObject{["code"]=" ",["name"]="Blank Code"},
                new JObject{["code"]=new JArray("X"),["name"]="Array Code"},
                new JObject{["code"]="OTHER_OWNER",["nested"]=new JObject{["name"]="Different source object"}})
        }.ToString();
        var fallback=NeoVinResult.Create(Vin,fallbackRaw);var fallbackReport=ReportDocument(NeoVinHtmlRenderer.Render(fallback));
        var fallbackOptions=CategoryElement(fallbackReport,"Fallback Options");
        Check(!NamedItems(fallbackOptions).Any()&&fallbackOptions.Element("div").Elements("details").Where(group=>HasClass(group,"avd-field-group")).Select(CategoryLabel).SequenceEqual(Enumerable.Range(1,8).Select(index=>"Item "+index)),"missing, blank, complex and mismatched-source Code/Name pairs keep their original numbered field groups");
        CheckCategoryRows(fallback,fallbackReport);
    }
    static IEnumerable<XElement> NamedItems(XContainer scope){return scope.Descendants("details").Where(group=>HasClass(group,"avd-named-item-group"));}
    static XElement DescriptionGroup(XElement scope,string name){return scope.Descendants("details").Single(e=>HasClass(e,"avd-description-group")&&CategoryLabel(e)==name);}
    static IEnumerable<string> DescriptionValues(XElement group){return group.Descendants("ul").Where(e=>HasClass(e,"avd-description-list")).SelectMany(e=>e.Elements("li")).Select(e=>e.Value);}
    static XElement FieldGroup(XElement category,string path){
        var content=category.Element("div");XElement group=null;
        foreach(var name in path.Split(new[]{" / "},StringSplitOptions.None)){
            group=content.Elements("details").Single(e=>HasClass(e,"avd-field-group")&&CategoryLabel(e)==name);
            content=group.Element("div");
        }
        return group;
    }
    static IEnumerable<XElement> ValueCells(XContainer container){return container.Descendants("tbody").Elements("tr").SelectMany(row=>row.Elements("th").Where(cell=>(string)cell.Attribute("scope")=="row").Select(cell=>cell.ElementsAfterSelf("td").First()));}
    static IEnumerable<XElement> RepeatedValues(XContainer container){return container.Descendants("ul").Where(list=>HasClass(list,"avd-repeated-values")).SelectMany(list=>list.Elements("li"));}
    static string RepeatedValue(XElement value){var details=value.Elements("details").FirstOrDefault(element=>HasClass(element,"avd-description-item"));return details==null?value.Value:CategoryLabel(details);}
    static string CellLabel(XElement cell){return cell.ElementsBeforeSelf().Last().Value;}
    static string FullCellLabel(XElement cell){
        var list=cell.Ancestors("ul").FirstOrDefault(element=>HasClass(element,"avd-repeated-values"));
        var prefix=list==null?Enumerable.Empty<string>():new[]{CategoryLabel(list.Ancestors("details").First(element=>HasClass(element,"avd-item-group")))};
        return string.Join(" / ",prefix.Concat(cell.Ancestors("details").Where(e=>HasClass(e,"avd-field-group")).Reverse().Select(CategoryLabel)).Concat(new[]{CellLabel(cell)}));
    }
    static string ComparableValue(string value){return System.Text.RegularExpressions.Regex.IsMatch(value,@"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$")?value.Replace(",",string.Empty):value;}
    static string CategoryLabel(XElement category){var summary=category.Element("summary");var title=summary.Descendants("span").FirstOrDefault(e=>HasClass(e,"avd-panel__title"));return title==null?summary.Value:title.Value;}
    static XDocument ReportDocument(string html){return XDocument.Parse("<root>"+html.Replace("&minus;","&#8722;").Replace(" open>"," open=\"open\">")+"</root>");}
    static bool HasClass(XElement element,string name){return element!=null&&((string)element.Attribute("class")??"").Split(' ').Contains(name);}
    sealed class Handler:HttpMessageHandler {public HttpResponseMessage Response;public Uri Uri;public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Uri=request.RequestUri;Calls++;return Task.FromResult(Response);}}
    sealed class WaitingStream:MemoryStream {public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return 0;}}
}
