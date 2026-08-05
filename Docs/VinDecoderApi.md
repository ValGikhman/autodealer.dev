# VIN Decoder HTML API

The VIN decoder endpoint sends a VIN to DataOne Software and returns a complete,
responsive HTML fragment. The fragment includes every matching vehicle style,
summary panels, and the complete DataOne record for each style.

All panels are collapsed initially and use native HTML `details` elements. No
Bootstrap JavaScript, jQuery, icon font, or other client dependency is required.

## Endpoint

When `ApiSecurity:Enabled` is `true`, every request must also send:

```http
Authorization: Bearer ad_live_KEY_ID.SECRET
```

The credential is issued during account creation and is displayed in full only
once. Keep it in a server-side secret manager, never in browser JavaScript.

```http
GET /api/service/vin/{vin}/html
Accept: text/html
```

Example:

```http
GET /api/service/vin/1HGCM82633A004352/html
Accept: text/html
```

A successful request returns:

```http
HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8
Cache-Control: no-store
```

The response is an embeddable HTML fragment containing scoped CSS. It is not a
complete document with `html`, `head`, or `body` elements.

## JSON and XML endpoints

Use the format-specific endpoints when vehicle data will be processed by an
application instead of rendered directly:

```http
GET /api/service/vin/{vin}/json
GET /api/service/vin/{vin}/xml
```

Both endpoints use the same response object and include:

- The normalized VIN.
- The UTC decode timestamp.
- The complete DataOne response, including every style, repeated record,
  element, and attribute.

### JSON request

```bash
curl --fail-with-body \
  --header "Accept: application/json" \
  "https://localhost:44398/api/service/vin/1HGCM82633A004352/json"
```

Example response shape:

```json
{
  "vin": "1HGCM82633A004352",
  "decodedAtUtc": "2026-08-02T14:30:00.0000000Z",
  "data": {
    "decoder_messages": {
      "service_provider": "DataOne Software"
    },
    "query_responses": {
      "query_response": {
        "us_market_data": {
          "us_styles": {
            "@count": "2",
            "style": [
              { "@name": "LX", "basic_data": {} },
              { "@name": "EX", "basic_data": {} }
            ]
          }
        }
      }
    }
  }
}
```

XML attributes use an `@` prefix in JSON. Repeated XML elements are represented
as JSON arrays.

Browser example:

```javascript
const vin = "1HGCM82633A004352";
const response = await fetch(
    `/api/service/vin/${encodeURIComponent(vin)}/json`,
    { headers: { Accept: "application/json" }, cache: "no-store" }
);

const result = await response.json();
if (!response.ok) throw new Error(result.message || "VIN decode failed.");

console.log(result.vin);
console.log(result.decodedAtUtc);
console.log(result.data);
```

### XML request

```bash
curl --fail-with-body \
  --header "Accept: application/xml" \
  "https://localhost:44398/api/service/vin/1HGCM82633A004352/xml"
```

Example response shape:

```xml
<vin_decode_response>
  <vin>1HGCM82633A004352</vin>
  <decoded_at_utc>2026-08-02T14:30:00.0000000Z</decoded_at_utc>
  <data>
    <decoded_data>
      <decoder_messages>...</decoder_messages>
      <query_responses>...</query_responses>
    </decoded_data>
  </data>
</vin_decode_response>
```

C# deserialization can use `XDocument` when the complete, evolving DataOne
schema needs to be retained:

```csharp
using System.Net.Http;
using System.Xml.Linq;

var xml = await httpClient.GetStringAsync(
    baseUrl + "/api/service/vin/1HGCM82633A004352/xml");
var response = XDocument.Parse(xml);
var dataOne = response.Root.Element("data").Element("decoded_data");
```

Successful JSON and XML responses use `application/json` and `application/xml`
respectively. Errors use the requested endpoint's format as well:

```json
{ "message": "VIN must contain exactly 17 letters or digits and cannot contain I, O, or Q." }
```

```xml
<error>
  <message>VIN must contain exactly 17 letters or digits and cannot contain I, O, or Q.</message>
</error>
```

## VIN validation

A VIN must:

- Contain exactly 17 characters.
- Contain only letters and digits.
- Not contain the letters `I`, `O`, or `Q`.

Input is trimmed and converted to uppercase by the server. Lowercase VINs are
therefore accepted.

## Browser JavaScript

For customer sites, keep the API credential in a server-side proxy and let the
browser call that same-origin proxy. The included widget reads the VIN and URL
template from a div and renders the returned HTML fragment:

```html
<div id="vehicle-report"
     data-autodealer-vin-report
     data-api-url="/vehicles/vin-report?vin={vin}"
     data-vin="1HGCM82633A004352"
     data-loading-text="Loading vehicle details...">
</div>

<script src="https://api.autodealer.dev/Scripts/autodealer-vin-report.js"></script>
```

The customer can also download this script and serve it with their own
versioned site assets.

The literal `{vin}` placeholder is required. The widget validates and
URL-encodes the VIN, requests `text/html`, handles loading and error states, and
emits `autodealer:loading`, `autodealer:loaded`, and `autodealer:error` events.
Change a report after the page loads with:

```javascript
const report = document.querySelector("#vehicle-report");
await AutoDealerVinReport.load(report, "1HGCM82633A004352");
```

The full customer guide and working example are available at:

```text
/Documentation/VinHtml
```

The following lower-level example is appropriate only when the consuming page
has a same-origin server proxy. Point the request at the proxy rather than
placing a production bearer key in JavaScript:

```html
<form id="vin-form">
    <label for="vin">VIN</label>
    <input
        id="vin"
        name="vin"
        maxlength="17"
        autocomplete="off"
        placeholder="1HGCM82633A004352"
        required>
    <button type="submit">Decode VIN</button>
</form>

<p id="vin-status" role="status"></p>
<div id="vin-results"></div>

<script>
    const form = document.querySelector("#vin-form");
    const input = document.querySelector("#vin");
    const status = document.querySelector("#vin-status");
    const results = document.querySelector("#vin-results");

    form.addEventListener("submit", async event => {
        event.preventDefault();

        const vin = input.value.trim().toUpperCase();
        status.textContent = "Decoding VIN…";
        results.replaceChildren();

        try {
            const response = await fetch(
                `/vehicles/vin-report?vin=${encodeURIComponent(vin)}`,
                {
                    method: "GET",
                    headers: { Accept: "text/html" },
                    cache: "no-store"
                }
            );

            if (!response.ok) {
                const contentType = response.headers.get("content-type") || "";
                const error = contentType.includes("json")
                    ? await response.json()
                    : { message: await response.text() };

                throw new Error(error.message || `VIN decode failed (${response.status}).`);
            }

            results.innerHTML = await response.text();
            status.textContent = "VIN decoded successfully.";
        }
        catch (error) {
            status.textContent = error.message || "Unable to decode this VIN.";
        }
    });
</script>
```

DataOne values are HTML-encoded by the server before the fragment is generated.
The returned fragment contains no executable scripts.

## jQuery

This example also calls the customer's server-side proxy:

```javascript
function decodeVin(vin) {
    const normalizedVin = (vin || "").trim().toUpperCase();

    return $.ajax({
        url: `/vehicles/vin-report?vin=${encodeURIComponent(normalizedVin)}`,
        method: "GET",
        dataType: "html",
        cache: false
    })
    .done(html => {
        $("#vin-results").html(html);
    })
    .fail(xhr => {
        const message = xhr.responseJSON?.message || "Unable to decode this VIN.";
        $("#vin-error").text(message).show();
    });
}
```

## Display the result in an iframe

For a completely isolated presentation, point an iframe directly at the
endpoint:

```html
<iframe
    title="Decoded vehicle details"
    src="/api/service/vin/1HGCM82633A004352/html"
    style="width:100%; min-height:900px; border:0">
</iframe>
```

Because the endpoint returns a fragment rather than a complete page, embedding
the HTML into a normal page is usually preferable.

## curl

```bash
curl --fail-with-body \
  --header "Accept: text/html" \
  "https://localhost:44398/api/service/vin/1HGCM82633A004352/html"
```

Save the generated fragment to a file:

```bash
curl --fail-with-body \
  --header "Accept: text/html" \
  --output decoded-vin.html \
  "https://localhost:44398/api/service/vin/1HGCM82633A004352/html"
```

Replace the host and port with the deployed application address.

## PowerShell

```powershell
$vin = "1HGCM82633A004352"
$uri = "https://localhost:44398/api/service/vin/$vin/html"

$response = Invoke-WebRequest `
    -Uri $uri `
    -Headers @{ Accept = "text/html" }

$html = $response.Content
```

## C# HttpClient

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public static async Task<string> GetVinHtmlAsync(
    HttpClient client,
    string baseUrl,
    string vin)
{
    var normalizedVin = (vin ?? string.Empty).Trim().ToUpperInvariant();
    var uri = string.Format(
        "{0}/api/service/vin/{1}/html",
        baseUrl.TrimEnd('/'),
        Uri.EscapeDataString(normalizedVin));

    using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using (var response = await client.SendAsync(request))
        {
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    string.Format("VIN decoder returned {0}: {1}",
                        (int)response.StatusCode,
                        body));

            return body;
        }
    }
}
```

## Response contents

The HTML includes:

- Every US-market style returned by DataOne.
- All engines and transmissions.
- Every EPA fuel-efficiency record.
- DataOne pricing and basic vehicle information.
- Tire information.
- All standard specification categories and values.
- Standard and optional equipment trees.
- A complete recursive record for every style, including colors, warranties,
  identifiers, crash-test data, green scores, OEM options, and other fields
  returned by DataOne.
- Unknown or newly introduced DataOne elements and attributes.

The complete record prevents data from being lost when DataOne adds fields that
the summary panels do not yet recognize.

## Error responses

Errors are returned as JSON and include a `message` property.

| Status | Meaning |
| --- | --- |
| `400 Bad Request` | The VIN is not a valid 17-character VIN. |
| `404 Not Found` | The endpoint route was not matched. |
| `422 Unprocessable Entity` | DataOne returned a decoder error or no matching US style. |
| `502 Bad Gateway` | DataOne is unavailable or returned an unexpected response. |
| `500 Internal Server Error` | The VIN decoder service is not configured. |

Example error:

```json
{
  "message": "The VIN could not be decoded: Invalid VIN."
}
```

## Server configuration

The endpoint reads these application settings on the server:

```xml
<appSettings>
  <add key="DataOne:AccessKey" value="YOUR_ACCESS_KEY" />
  <add key="DataOne:SecretAccessKey" value="YOUR_SECRET_KEY" />
</appSettings>
```

Do not expose either credential in browser JavaScript, HTML, source control, or
client applications. In production, inject these settings through the hosting
environment or a protected configuration provider.

## Operational notes

- Each endpoint request makes a DataOne decoder request and may count against
  the account's usage allowance.
- The endpoint sends `Cache-Control: no-store`; implement a server-side VIN cache
  if repeated decoding should be avoided.
- Browser calls are same-origin by default. Cross-origin clients require an
  intentional CORS policy; this project does not currently enable CORS.
- Protect public deployments with authentication, authorization, and rate
  limiting to prevent unauthorized DataOne usage.
- The existing raw endpoint, `GET /api/service/{vin}`, remains available for
  backward compatibility. The `/html` endpoint is the recommended endpoint for
  rendering vehicle details.
