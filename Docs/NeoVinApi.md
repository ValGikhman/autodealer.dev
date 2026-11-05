# MarketCheck VIN decoder

Clients can open **VIN Decoder** from **My account** (`/account/vinDecoder`). The signed-in client session uses the account's own eligible VIN API key, subscription and quota without a key field. This is separate from the Admin workflow below.

Open `/majordome/vinDecoder` from Majordome's VIN decoder navigation link. The page requires an administrator session; older `/vinDecoder` and `/NeoVin` URLs redirect authenticated administrators to the canonical Majordome URL. This is a separate MarketCheck NeoVIN decoder; the existing public DataOne decoder remains at `/solutions/vin-decoder`.

Enter a VIN. When a server-side default account key is configured, leave the API key field blank to use it; an entered AutoDealer.dev account key overrides the default for that request. The saved account key is never rendered or stored in the browser. The MarketCheck provider key remains in server configuration (`MarketCheck:ApiKey` or `MARKETCHECK_API_KEY`), shared with Price Research. Normal API authentication, scopes, subscription checks and the Majordome-managed monthly quota apply. Using the default also requires the signed-in Admin session and the form's anti-forgery token.

The results header shows the decoded year, make, model and trim. Specifications appear in expandable category sections, following the DataOne report's disclosure pattern. Every returned specification, feature, option, equipment item and metadata field is retained, including nested records, arrays and fields not recognized as vehicle summaries. The VIN decoder does not load or use PepGrid.

Price Research is also restricted to Majordome at `/majordome/priceResearch`. Both tools share the site's charcoal, silver and red styling. Price Research uses one scrollable comparable grid with numeric sorting and a Current/Recent switch; paging, search and column filters are disabled.

## Endpoints

External API callers use `Authorization: Bearer <AutoDealer-account-key>` with `vin:read` access. The Majordome form can use its server-side default only for its protected POST with an Admin session and anti-forgery header. Do not send the MarketCheck provider key to these routes.

`POST /api/service/neovin`

```json
{"Vin":"4T1BE46K19U342407"}
```

Returns `{vin, decodedAtUtc, html, rawJson, data, specifications}`. Specification rows contain `{Category, Field, Value, Path}`; `Path` identifies the location in the original JSON.

- `GET /api/service/neovin/4T1BE46K19U342407/html` returns the formatted HTML fragment.
- `GET /api/service/neovin/4T1BE46K19U342407/json` returns the complete provider JSON.

Each API request makes one server-side call to `https://api.marketcheck.com/v2/decode/car/neovin/{VIN}/specs`. VINs are normalized and validated before provider access. Responses are not cached. Provider errors are sanitized and the key is never rendered or forwarded through redirects.

## Verification

`Scripts/Test-PriceResearch.ps1 -BuildLocal` compiles the full app and runs both research/decoder service, rendering and HTTP-authentication checks. `-LiveNeoVin` additionally makes one real NeoVIN provider request; `-Live` separately enables the existing real price-comparables check. Neither live test changes account keys or database records.
