using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using System;
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
        Check(panels.Count==result.Specifications.Select(row=>row.Category.Split(new[]{" / "},StringSplitOptions.None)[0]).Distinct().Count()+2,"summary, each top-level category and raw payload remain shared panels");
        CheckCategoryRows(result,neo);
        Check(neo.Descendants("pre").Single().Value==result.RawJson,"complete raw JSON survives report HTML encoding without changes");
        Check(panels.First().Descendants("tr").Any(row=>row.Element("th").Value=="VIN#"&&row.Element("td").Value==Vin)&&!panels.First().Descendants("tr").Any(row=>row.Element("th").Value=="Specifications")&&!neo.Descendants("summary").Any(summary=>System.Text.RegularExpressions.Regex.IsMatch(summary.Value,@"\(\d+ fields\)")),"shared summary retains VIN without generated specification or field counters");
        var hostile=NeoVinResult.Create(Vin,Fixture.Replace("Toyota","<script>alert(1)</script>").Replace("Safety","<img src=x onerror=alert(1)>").Replace("Seats","<b>Seats</b>"));
        var hostileDocument=ReportDocument(NeoVinHtmlRenderer.Render(hostile));
        Check(!hostileDocument.Descendants("script").Any()&&!hostileDocument.Descendants("img").Any()&&hostileDocument.Descendants("pre").Single().Value==hostile.RawJson,"shared header, category, fields and complete payload encode hostile provider text");
    }
    static void CheckCategoryRows(NeoVinResult result,XDocument report){
        foreach(var group in result.Specifications.GroupBy(row=>row.Category)){
            var category=CategoryElement(report,group.Key);
            var tables=category.Descendants("table").Where(table=>table.Ancestors("details").First(e=>!HasClass(e,"avd-item-group"))==category).ToList();
            var rows=tables.SelectMany(table=>table.Element("tbody").Elements("tr")).ToList();
            Check(tables.All(table=>HasClass(table,"avd-table")&&HasClass(table,"avd-table--keyvalue"))&&rows.Select(row=>row.Element("th").Value+"\n"+row.Element("td").Value).OrderBy(value=>value,StringComparer.Ordinal).SequenceEqual(group.Select(row=>row.Field+"\n"+(string.IsNullOrWhiteSpace(row.Value)?"—":row.Value)).OrderBy(value=>value,StringComparer.Ordinal)),"shared tables retain every field and value exactly once in "+group.Key);
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
        Check(top.SequenceEqual(new[]{"Basic summary","Vehicle","Features","Installed Equipment","Options","Slash/Category","Complete MarketCheck response"}),"repeated category prefixes appear once in their first-seen order without UI counters");
        var features=CategoryElement(report,"Features");var equipment=CategoryElement(report,"Installed Equipment");
        Check(features.Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Safety","Interior"})&&CategoryElement(report,"Features / Safety").Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Standard","Optional"}),"Features groups Safety and Interior under one parent with local leaf labels");
        Check(equipment.Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Interior","Safety"})&&CategoryElement(report,"Installed Equipment / Interior").Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Standard","Optional"}),"Installed Equipment has independent nested Standard and Optional leaves");
        Check(report.Descendants("details").Where(e=>HasClass(e,"avd-tree")&&!HasClass(e,"avd-item-group")).All(e=>!CategoryLabel(e).Contains(" / ")),"nested disclosure labels omit their repeated parent prefixes");
        CheckCategoryRows(result,report);
        Check(report.Descendants("td").Count(cell=>cell.Value=="Shared fact")==3&&report.Descendants("td").Any(cell=>cell.Value=="42"),"identical facts on the same and different paths and provider numeric values are never deduplicated");
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
        Check(groups.SelectMany(group=>group.Descendants("tbody").Elements("tr").Where(row=>featureTypes.Contains(row.Element("th").Value)).Select(row=>row.Element("td").Value)).SequenceEqual(descriptions),"all eight front and rear feature descriptions stay distinct and ordered within their types");
        Check(groups[0].Descendants("td").Any(cell=>cell.Value=="Original suspension detail")&&groups[0].Descendants("td").Any(cell=>cell.Value=="0.98")&&groups[0].Descendants("td").Any(cell=>cell.Value=="Retained evidence"),"extra descendant metadata follows its source feature including quoted JSON property paths");
        var installed=CategoryElement(report,"Installed Equipment / Suspension / Standard").Element("div").Elements("details").Where(e=>HasClass(e,"avd-item-group")).ToList();
        Check(installed.Select(CategoryLabel).SequenceEqual(new[]{"Suspension · type","Suspension · stabilizer bar","Suspension · wheel dependence"})&&installed.All(group=>group.Descendants("tbody").Elements("tr").Any(row=>row.Element("th").Value.EndsWith("(front)"))&&group.Descendants("tbody").Elements("tr").Any(row=>row.Element("th").Value.EndsWith("(rear)"))),"equipment groups exact item and attribute while front and rear remain explicit rows");
        Check(installed[0].Descendants("td").Any(cell=>cell.Value=="0.91"),"equipment confidence metadata stays with its owning item group");
        Check(CategoryElement(report,"Features / Comfort / Standard").Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Power Windows"})&&CategoryElement(report,"Installed Equipment / Brakes / Standard").Element("div").Elements("details").Select(CategoryLabel).SequenceEqual(new[]{"Brakes · type"}),"grouping applies generically to other feature and equipment types while single records remain ordinary rows");
        Check(CategoryElement(report,"Features / Suspension / Optional").Element("div").Elements("details").Single().Descendants("td").Select(cell=>cell.Value).SequenceEqual(new[]{"Optional front suspension","Optional rear suspension"}),"repeated item groups stay isolated by complete category and Standard or Optional context");
        CheckCategoryRows(result,report);
        var rawHtml=System.Text.RegularExpressions.Regex.Match(html,"<pre class=\"avd-report__raw\">([\\s\\S]*?)</pre>").Groups[1].Value;
        Check(WebUtility.HtmlDecode(rawHtml)==raw&&JToken.DeepEquals(originalRows,result.ToJsonObject()["specifications"])&&!report.Descendants("summary").Any(summary=>System.Text.RegularExpressions.Regex.IsMatch(summary.Value,@"\(\d+ fields\)")),"item grouping preserves exact raw JSON, every original field value and path, and omits UI counters");
    }
    static string CategoryLabel(XElement category){var summary=category.Element("summary");var title=summary.Descendants("span").FirstOrDefault(e=>HasClass(e,"avd-panel__title"));return title==null?summary.Value:title.Value;}
    static XDocument ReportDocument(string html){return XDocument.Parse("<root>"+html.Replace("&minus;","&#8722;").Replace(" open>"," open=\"open\">")+"</root>");}
    static bool HasClass(XElement element,string name){return element!=null&&((string)element.Attribute("class")??"").Split(' ').Contains(name);}
    sealed class Handler:HttpMessageHandler {public HttpResponseMessage Response;public Uri Uri;public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Uri=request.RequestUri;Calls++;return Task.FromResult(Response);}}
    sealed class WaitingStream:MemoryStream {public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return 0;}}
}
