using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace autodealer.dev.Services {
    public sealed class PriceResearchService {
        private const string Endpoint="https://api.marketcheck.com/v2/predict/car/us/marketcheck_price/comparables";
        private static readonly HttpClient SharedClient=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(60)};
        private readonly HttpClient client;private readonly string configuredKey;
        public PriceResearchService():this(SharedClient,null){}
        public PriceResearchService(HttpClient client,string apiKey){this.client=client??throw new ArgumentNullException("client");configuredKey=apiKey;}
        public async Task<PriceResearchResult> ResearchAsync(PriceResearchRequest input,CancellationToken token){
            if(input==null)throw new ArgumentException("Enter a VIN, mileage, ZIP code and radius.");input.NormalizeAndValidate();
            var key=configuredKey;
            if(string.IsNullOrWhiteSpace(key))key=ConfigurationManager.AppSettings["MarketCheck:ApiKey"];
            if(string.IsNullOrWhiteSpace(key))key=Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");
            if(string.IsNullOrWhiteSpace(key))throw new PriceResearchException(HttpStatusCode.ServiceUnavailable,"Price research is not configured. Set MarketCheck:ApiKey on the server.");
            key=key.Trim();
            var query="?api_key="+Uri.EscapeDataString(key)+"&vin="+Uri.EscapeDataString(input.Vin)+"&miles="+input.Miles.Value.ToString(CultureInfo.InvariantCulture)+"&dealer_type=independent&zip="+Uri.EscapeDataString(input.Zip)+"&radius="+input.Radius.ToString(CultureInfo.InvariantCulture);
            try{
              using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){
                timeout.CancelAfter(TimeSpan.FromSeconds(60));var requestToken=timeout.Token;
                using(var request=new HttpRequestMessage(HttpMethod.Get,Endpoint+query)){
                    request.Headers.Accept.ParseAdd("application/json");
                    using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,requestToken).ConfigureAwait(false)){
                        MarketCheckQuota.Capture(response.Headers);
                        if(!response.IsSuccessStatusCode)throw ProviderFailure(response.StatusCode);
                        if(response.Content.Headers.ContentLength>20000000)throw new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck returned an unexpectedly large response.");
                        string json;
                        using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var bytes=new MemoryStream()){
                            var buffer=new byte[16384];int read;
                            while((read=await stream.ReadAsync(buffer,0,buffer.Length,requestToken).ConfigureAwait(false))>0){if(bytes.Length+read>20000000)throw new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck returned an unexpectedly large response.");bytes.Write(buffer,0,read);}
                            json=Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\ufeff');
                        }
                        // Provider data is retained in full; a credential unexpectedly
                        // echoed by the provider must never reach a browser or download.
                        json=json.Replace(key,"[redacted]").Replace(Uri.EscapeDataString(key),"[redacted]");
                        return PriceResearchResult.Create(input,json);
                    }
                }
              }
            }catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new PriceResearchException(HttpStatusCode.GatewayTimeout,"MarketCheck did not respond in time. Try again.");}
            catch(HttpRequestException){throw new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck is temporarily unavailable. Try again.");}
            catch(IOException){throw new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck returned an incomplete response. Try again.");}
        }
        private static PriceResearchException ProviderFailure(HttpStatusCode status){
            if(status==HttpStatusCode.BadRequest)return new PriceResearchException(HttpStatusCode.BadRequest,"MarketCheck rejected the VIN, mileage or location parameters.");
            if(status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden)return new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck denied access. Check the server API key and subscribed package.");
            if((int)status==429)return new PriceResearchException((HttpStatusCode)429,"MarketCheck request limit reached. Try again later.");
            return new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck could not complete this price research request.");
        }
    }
}
