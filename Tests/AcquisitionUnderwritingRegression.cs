using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using autodealer.dev.Services;
class Test {
 static JObject Budget() {return JObject.Parse(@"{expectedSellingPriceUsd:29000,recommendedBidUsd:21500,fixedAuctionFeeUsd:400,auctionFeeRate:0.025,transportUsd:350,inspectionUsd:150,repairsUsd:1200,detailingUsd:200,taxAndTitleUsd:100,holdingUsd:300,sellingExpensesUsd:200,downsideSellingPriceUsd:27000,downsideExtraRepairsUsd:1500,assumptions:['Illustrative costs only.']}");}
 static JObject Item(int price,int id,string distance="120 miles",int year=2023) {return JObject.FromObject(new {dealer="Fixture dealer",year=year,listingVin="",vehicle=year+" Acura RDX A-Spec Advance AWD",mileage="99669",priceUsd=price,askingPrice=price.ToString(),distance=distance,titleAndPriceConditions="unknown",url="https://dealer.test/"+id,selectionNotes="Fixture",adjustmentUsd=(decimal?)null,adjustmentBasis="Fixture"});}
 static void Main() {
  var result=AcquisitionUnderwriting.Calculate(Budget(),3000);
  if(result.MaximumBid!=22500||result.RecommendedBid!=21500||result.MaximumInvestment!=25962.50m||result.MaximumProfit!=3037.50m)throw new Exception("Reference arithmetic failed");
  var scenario=result.Render().ToString();
  if(!scenario.Contains("$4,062.50")||!scenario.Contains("$562.50")||!(scenario.Contains("($462.50)")||scenario.Contains("-$462.50")))throw new Exception("Profit/downside failed");
  var invalid=Budget();invalid["auctionFeeRate"]=2.5;if(AcquisitionUnderwriting.Calculate(invalid,3000)!=null)throw new Exception("Invalid fee accepted");
  invalid=Budget();invalid["repairsUsd"]=JValue.CreateNull();if(AcquisitionUnderwriting.Calculate(invalid,3000)!=null)throw new Exception("Missing cost invented");
  if(AcquisitionUnderwriting.Calculate(Budget(),5000).MaximumBid>=22500)throw new Exception("Profit input ignored");
  var identity=JObject.Parse("{VIN:'5J8TC2H84PL017341',ModelYear:'2023',Make:'ACURA',Model:'RDX',Trim:'A-Spec Advance'}");
  decimal distance;
  if(!AcquisitionMarketPrice.TryDistance(Item(28000,10,"Approx. 120 miles"),out distance)||distance!=120)throw new Exception("Approximate distance parsing failed");
  if(AcquisitionMarketPrice.WithinRadius(Item(28000,11,"Unknown"))||AcquisitionMarketPrice.OutsideRadius(Item(28000,11,"Unknown")))throw new Exception("Unknown distance misclassified");
  if(!AcquisitionMarketPrice.WithinRadius(Item(28000,12,"250 miles"))||AcquisitionMarketPrice.WithinRadius(Item(28000,13,"251 miles")))throw new Exception("250-mile radius boundary failed");
  var realIdentity=JObject.Parse("{ModelYear:'2023',Make:'ACURA',Model:'RDX',Trim:'SH-AWD A-SPEC ADVANCE'}");
  var reordered=Item(31900,14,"Approximately 120 miles straight-line; estimated");
  reordered["vehicle"]="2023 Acura RDX A-Spec Advance Package SH-AWD";
  if(AcquisitionMarketPrice.AverageSample(new List<JObject>{reordered},99669,realIdentity).Count!=1)throw new Exception("Trim order or qualified distance rejected the matching car");
  if(AcquisitionMarketPrice.TryDistance(Item(28000,15,"120–300 miles"),out distance))throw new Exception("Distance range silently converted to a valid radius");
  var unequal=new List<JObject>{Item(28000,1),Item(30000,2),Item(37000,3),Item(90000,4,"251 miles"),Item(100000,5,"50 miles",2022)};
  var sample=AcquisitionMarketPrice.AverageSample(unequal,99669,identity);
  if(sample.Count!=3||AcquisitionMarketPrice.Average(sample)!=31666.67m)throw new Exception("Arithmetic mean, radius or YMMS filtering failed");
  var prose=Item(30000,31);prose["priceUsd"]=JValue.CreateNull();prose["askingPrice"]="Internet price $31,900";
  decimal cash;if(!AcquisitionMarketPrice.TryPrice(prose,out cash)||cash!=31900)throw new Exception("Displayed cash price in prose was not parsed");
  prose["askingPrice"]="$29,995 / $30,443";if(AcquisitionMarketPrice.TryPrice(prose,out cash))throw new Exception("Conflicting advertised prices guessed");
  prose["askingPrice"]="$499 monthly payment";if(AcquisitionMarketPrice.TryPrice(prose,out cash))throw new Exception("Monthly payment averaged as cash price");
  var comps=new List<JObject>{Item(28000,1),Item(29000,2),Item(30000,3)};
  var report=new JObject{["recommendation"]="BID LOWER",["summary"]="Fixture",["underwriting"]=Budget(),["comparables"]=new JArray(comps),["sections"]=new JArray()};
  report["underwriting"]["expectedSellingPriceUsd"]=999999;
  var evidence=new JObject{["output"]=new JArray(new JObject{["sources"]=new JArray(comps.Select(x=>new JObject{["url"]=(string)x["url"]}))})};
  var html=AcquisitionReportDocument.ToArticle(report,evidence,99669,identity,false,3000);
  if(!html.Contains("$22,500.00")||!html.Contains("$29,000.00")||html.Contains("$999,999.00"))throw new Exception("Server mean was not used for the bid");
  var twoReport=(JObject)report.DeepClone();
  twoReport["comparables"]=new JArray(comps.Take(2));
  twoReport["comparables"][0]["distance"]="Approx. 120 miles";
  twoReport["comparables"][1]["distance"]="Unknown";
  var twoHtml=AcquisitionReportDocument.ToArticle(twoReport,evidence,99669,identity,false,3000);
  if(!twoHtml.Contains("2 nearby dealer comparisons")||!twoHtml.Contains("Dealer / listing"))throw new Exception("Small sample table hidden");
  var five=new List<JObject>{Item(32995,41),Item(26807,42,"Unknown",2022),Item(29995,43),Item(33487,44),Item(39348,45)};
  five[1]["mileage"]="Unknown";five[2]["vehicle"]="2023 Acura RDX Base AWD";
  var fiveReport=(JObject)report.DeepClone();fiveReport["comparables"]=new JArray(five);
  var fiveEvidence=new JObject{["output"]=new JArray(new JObject{["sources"]=new JArray(five.Select(x=>new JObject{["url"]=(string)x["url"]}))})};
  var fiveHtml=AcquisitionReportDocument.ToArticle(fiveReport,fiveEvidence,99669,identity,false,3000);
  if(!fiveHtml.Contains("$162,632.00 ÷ 5 = $32,526.40"))throw new Exception("Displayed prices were silently excluded from their average");
  report["recommendation"]="PASS";
  if(!AcquisitionReportDocument.ToArticle(report,evidence,99669,identity,false,3000).Contains("Auction bid summary"))throw new Exception("Profitable average-based ceiling blocked by model label");
  report["recommendation"]="BUY";
  if(!AcquisitionReportDocument.ToArticle(report,evidence,99669,identity,false,3000).Contains("Auction bid summary"))throw new Exception("Assumptions forced downgrade");
  report["underwriting"]["repairsUsd"]=JValue.CreateNull();
  if(!AcquisitionUnderwriting.MissingInputs(report["underwriting"] as JObject,3000).Contains("Repairs / maintenance reserve"))throw new Exception("Missing repair cost not diagnosed");
  var partial=AcquisitionReportDocument.ToArticle(report,evidence,99669,identity,false,3000);
  if(!partial.Contains("Hammer bid calculation needs:")||partial.Contains("Suggested auction bid"))throw new Exception("Partial budget hidden");
  Console.WriteLine("PASS: 250-mile YMMS mean, price-to-bid baseline, fee/profit/downside arithmetic, desired profit, missing inputs and title/condition assumptions.");
 }
}
