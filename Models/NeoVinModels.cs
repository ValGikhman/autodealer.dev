using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace autodealer.dev.Models {
    public sealed class NeoVinRequest {
        [Required,RegularExpression("^[A-HJ-NPR-Z0-9]{17}$",ErrorMessage="Enter a valid 17-character VIN (no I, O or Q).")]
        public string Vin {get;set;}
        public void NormalizeAndValidate(){Vin=NormalizeVin(Vin);}
        public static string NormalizeVin(string vin){var normalized=(vin??"").Trim().ToUpperInvariant();if(!Regex.IsMatch(normalized,"^[A-HJ-NPR-Z0-9]{17}$"))throw new ArgumentException("Enter a valid 17-character VIN (no I, O or Q).");return normalized;}
    }

    public sealed class NeoVinSpecification {
        public string Category {get;set;}
        public string Field {get;set;}
        public string Value {get;set;}
        public string Path {get;set;}
    }

    public sealed class NeoVinResult {
        public string Vin {get;private set;}
        public DateTime DecodedAtUtc {get;private set;}
        public string RawJson {get;private set;}
        public JObject Data {get;private set;}
        public List<NeoVinSpecification> Specifications {get;private set;}
        public List<NeoVinSpecification> SpecificationRows {get{return Specifications;}}
        public string VehicleTitle {get{return string.Join(" ",new[]{Text(Data["year"]),Text(Data["make"]),Text(Data["model"]),Text(Data["trim"])}.Where(s=>!string.IsNullOrWhiteSpace(s)));}}

        public static NeoVinResult Create(string vin,string json){
            vin=NeoVinRequest.NormalizeVin(vin);JObject data;
            try{data=JObject.Parse(json??"");}catch(JsonException){throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck returned an invalid VIN specification response.");}
            var responseVin=Text(data["vin"]);
            if(!string.IsNullOrWhiteSpace(responseVin)&&!string.Equals(responseVin,vin,StringComparison.OrdinalIgnoreCase))throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck returned specifications for a different VIN.");
            if(data["year"]==null&&data["make"]==null&&data["model"]==null&&data["specs"]==null&&data["features"]==null||data["code"]!=null&&data["message"]!=null)throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck returned no VIN specifications.");
            var rows=new List<NeoVinSpecification>();
            foreach(var property in data.Properties()){
                var category=property.Value is JContainer?Label(property.Name):"Vehicle";
                Flatten(property.Value,category,property.Value is JContainer?"":Label(property.Name),rows);
            }
            return new NeoVinResult{Vin=vin,DecodedAtUtc=DateTime.UtcNow,RawJson=json,Data=data,Specifications=rows};
        }
        public JObject ToJsonObject(){return new JObject{["vin"]=Vin,["decodedAtUtc"]=DecodedAtUtc.ToString("o"),["data"]=Data.DeepClone(),["specifications"]=JArray.FromObject(Specifications)};}
        public static string Label(string name){
            if(string.IsNullOrWhiteSpace(name))return "Value";
            var label=Regex.Replace(name,@"([a-z0-9])([A-Z])","$1 $2").Replace('_',' ').Trim();
            label=CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(label.ToLowerInvariant());
            return Regex.Replace(label,@"\b(Vin|Msrp|Mpg|Mph|Mmc|Usb|Abs|Awd|Fwd|Rwd|Cd|Dvd|Hd|Led)\b",m=>m.Value.ToUpperInvariant());
        }
        private static string Text(JToken token){if(token==null||token.Type==JTokenType.Null)return "";return token is JValue?token.ToString():token.ToString(Formatting.None);}
        private static void Row(List<NeoVinSpecification> rows,string category,string field,string value,JToken token){rows.Add(new NeoVinSpecification{Category=category,Field=string.IsNullOrWhiteSpace(field)?"Value":field,Value=value,Path=token.Path});}
        private static void Flatten(JToken token,string category,string field,List<NeoVinSpecification> rows){
            var obj=token as JObject;
            if(obj!=null){
                if(!obj.Properties().Any()){Row(rows,category,field,"{}",token);return;}
                // Preserve provider feature/equipment categories while making each
                // specification a readable row rather than several metadata rows.
                var feature=obj["feature_type"]!=null&&obj["description"]!=null;
                var equipment=obj["item"]!=null&&obj["attribute"]!=null&&obj["value"]!=null;
                if(feature||equipment){
                    var categoryName=Text(obj["category"]);var rowCategory=category+(categoryName.Length>0?" / "+categoryName:"");
                    var prefix=Regex.Replace(field,@"(?:^| / )Item \d+$","");
                    if(prefix.Length>0)rowCategory+=" / "+prefix;
                    var item=feature?Text(obj["feature_type"]):Text(obj["item"]);
                    if(equipment){var attribute=Text(obj["attribute"]);if(!attribute.Equals(item,StringComparison.OrdinalIgnoreCase))item+=" · "+attribute;var location=Text(obj["location"]);if(location.Length>0)item+=" ("+location+")";}
                    Row(rows,rowCategory,item,Text(obj[feature?"description":"value"]),token);
                    var known=feature?new[]{"category","feature_type","description"}:new[]{"category","item","attribute","location","value"};
                    foreach(var extra in obj.Properties().Where(p=>!known.Contains(p.Name)))Flatten(extra.Value,rowCategory,item+" / "+Label(extra.Name),rows);
                    return;
                }
                foreach(var property in obj.Properties())Flatten(property.Value,category,(field.Length==0?"":field+" / ")+Label(property.Name),rows);
                return;
            }
            var array=token as JArray;
            if(array!=null){if(array.Count==0){Row(rows,category,field,"[]",token);return;}for(var index=0;index<array.Count;index++)Flatten(array[index],category,(field.Length==0?"":field+" / ")+"Item "+(index+1),rows);return;}
            var text=token.Type==JTokenType.Null?"Not provided":token.Type==JTokenType.Boolean?((bool)token?"Yes":"No"):Text(token);
            Row(rows,category,field,text,token);
        }
    }
    public sealed class NeoVinException:Exception {
        public HttpStatusCode StatusCode {get;private set;}
        public NeoVinException(HttpStatusCode status,string message):base(message){StatusCode=status;}
    }
}
