using autodealer.dev.Models;
using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace autodealer.dev.Services {
    public sealed class NeoVinService {
        private const string Endpoint="https://api.marketcheck.com/v2/decode/car/neovin/";
        private static readonly HttpClient SharedClient=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(60)};
        private readonly HttpClient client;private readonly string configuredKey;
        public NeoVinService():this(SharedClient,null){}
        public NeoVinService(HttpClient client,string apiKey){this.client=client??throw new ArgumentNullException("client");configuredKey=apiKey;}
        public async Task<NeoVinResult> DecodeAsync(string vin,CancellationToken token){
            vin=NeoVinRequest.NormalizeVin(vin);var key=configuredKey;
            if(string.IsNullOrWhiteSpace(key))key=ConfigurationManager.AppSettings["MarketCheck:ApiKey"];
            if(string.IsNullOrWhiteSpace(key))key=Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");
            if(string.IsNullOrWhiteSpace(key))throw new NeoVinException(HttpStatusCode.ServiceUnavailable,"MarketCheck VIN decoding is not configured. Set MarketCheck:ApiKey on the server.");
            key=key.Trim();
            try{
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){
                    timeout.CancelAfter(TimeSpan.FromSeconds(60));var ct=timeout.Token;
                    using(var request=new HttpRequestMessage(HttpMethod.Get,Endpoint+Uri.EscapeDataString(vin)+"/specs?api_key="+Uri.EscapeDataString(key))){
                        request.Headers.Accept.ParseAdd("application/json");
                        using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false)){
                            if(!response.IsSuccessStatusCode)throw ProviderFailure(response.StatusCode);
                            if(response.Content.Headers.ContentLength>20000000)throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck returned an unexpectedly large VIN specification response.");
                            string json;
                            using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var bytes=new MemoryStream()){
                                var buffer=new byte[16384];int length;
                                while((length=await stream.ReadAsync(buffer,0,buffer.Length,ct).ConfigureAwait(false))>0){if(bytes.Length+length>20000000)throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck returned an unexpectedly large VIN specification response.");bytes.Write(buffer,0,length);}
                                json=Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\ufeff');
                            }
                            json=json.Replace(key,"[redacted]").Replace(Uri.EscapeDataString(key),"[redacted]");
                            return NeoVinResult.Create(vin,json);
                        }
                    }
                }
            }catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new NeoVinException(HttpStatusCode.GatewayTimeout,"MarketCheck VIN decoding timed out. Try again.");}
            catch(HttpRequestException){throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck VIN decoding is temporarily unavailable. Try again.");}
            catch(IOException){throw new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck returned an incomplete VIN specification response. Try again.");}
        }
        private static NeoVinException ProviderFailure(HttpStatusCode status){
            if(status==HttpStatusCode.BadRequest||status==HttpStatusCode.NotFound||status==(HttpStatusCode)422)return new NeoVinException((HttpStatusCode)422,"MarketCheck could not decode this VIN. Check the VIN and try again.");
            if(status==HttpStatusCode.Unauthorized||status==HttpStatusCode.Forbidden)return new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck denied VIN decoding access. Check the server API key and subscribed package.");
            if((int)status==429)return new NeoVinException((HttpStatusCode)429,"MarketCheck request limit reached. Try again later.");
            return new NeoVinException(HttpStatusCode.BadGateway,"MarketCheck could not complete this VIN decoding request.");
        }
    }
}
