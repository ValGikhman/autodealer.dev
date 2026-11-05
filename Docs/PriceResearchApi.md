# Price research

Clients can also open **Price Research** from **My account** (`/account/priceResearch`). Their signed-in session authorizes the protected research POST and meters it against their own eligible API key and subscription, without revealing a credential. The Majordome workflow below continues to use its separate Admin access.

Sign in to Majordome as an **Admin** and open `/majordome/priceResearch`. Enter a VIN and odometer miles. ZIP defaults to **45040**, radius to **100 miles**; both are editable. When a server-side default account key is configured, leave the API key field blank to use it; an entered key overrides the default for that request. The saved key is never rendered or stored in the browser. The public demo VIN key is not enabled for paid MarketCheck research. Anonymous visitors are redirected to Majordome login with their return URL; signed-in non-admin accounts receive HTTP 403. Legacy `/priceResearch` and `/PriceResearch/Index` URLs remain Admin protected and redirect authorized admins to the canonical Majordome page.

The page sends one request to AutoDealer.dev and displays formatted price results. One **Comparables within radius** grid shows either **Current** or **Recent** competitor listings using its two-position switch. Current is the default for each new research result; switching to Recent uses the same response without making another API request. Both lists exclude the subject VIN, listings outside the entered radius and unknown or negative distances. The boundary distance is included. The MarketCheck prediction remains the provider prediction; the **Current asking-price mean** always uses the current comparable list with positive asking prices, even when Recent is selected.

The primary result uses the site's existing PepGrid with numeric sorting. All records in the selected set appear in one scrollable table; paging, search, column filter menus, column resizing, and Excel/PDF toolbar buttons are disabled. Percentage widths fit the columns to the panel; full headers and values wrap instead of truncating. The VIN column is compact, numeric values align right, and source links are labeled **View**. The entered radius is always applied to both lists; there is no separate scope selector. Switching between Current and Recent, or loading a new research result, resets the grid's sort and scroll position. The page shares charcoal, silver and red styling with the MarketCheck VIN decoder at `/majordome/vinDecoder`.

The auction analysis starts with the MarketCheck estimate as an editable resale-price assumption. It subtracts configured auction fees, transport, and minimum profit, then rounds the feasible hammer bid down to $100. An optional current hammer bid shows the estimated investment and profit at that bid. These calculations run on AutoDealer.dev without another MarketCheck request or API-key quota use. New research resets the assumptions to the new vehicle. The result shows no feasible bid when the budget cannot preserve the target profit.

Profit is before other expenses. Repairs, holding, taxes, selling expenses, and other unconfigured costs are excluded. The resale assumption is an estimate derived from advertised prices, not a confirmed sale price.

The Local market demand panel shows MarketCheck Market Days Supply for the VIN's exact used-vehicle build within the entered ZIP and radius. The figures are market-wide within that search scope, rather than limited to independent dealers. It shows active supply, inferred sales activity over the previous 45 days, and the lookup time. Zero observed sales leaves days supply unavailable. This measure does not change the bid formula.

## Server configuration

Set `MarketCheck:ApiKey` in server Web.config, or set `MARKETCHECK_API_KEY` in the server environment. The local credential is already configured in the excluded local Web.config. Keep `ApiSecurity:Enabled` true.

`Research:DemandEnabled` defaults to true. Set it to `false` to disable the demand lookup. `Research:DemandTimeoutSeconds` defaults to 12. When demand data is unavailable, pricing and bidding remain usable.

Set `Research:DefaultApiKey` in the private server Web.config to the AutoDealer.dev account key used by both Majordome research tools. Leave this setting empty to require a manually entered account key. The pages expose only whether a default is available; the key remains server-side and still passes normal account, scope, subscription and quota checks.

Configure the auction budget in the same server Web.config:

```xml
<add key="AuctionBid:AuctionFeeUsd" value="450" />
<add key="AuctionBid:TransportUsd" value="350" />
<add key="AuctionBid:MinimumProfitUsd" value="2000" />
```

For example, a $20,000 resale assumption and these settings produce a **$17,200 maximum hammer bid**, $18,000 total known investment, and $2,000 estimated profit before other expenses. Missing or invalid auction settings stop the calculation rather than substituting defaults. The admin-only, anti-forgery-protected form endpoint is `POST /majordome/priceResearch/bid`; it takes `ExpectedSellingPriceUsd` and optional `CurrentHammerBidUsd`.

## AutoDealer.dev endpoints

External API callers use `Authorization: Bearer <AutoDealer-account-key>` with existing `vin:read` access. The Majordome form can use its server-side default only for its protected POST with an Admin session and anti-forgery header. The client page uses its own verified account session for the same POST. Both use existing subscription and quota checks.

`POST /api/service/price-research`

```json
{"Vin":"4T1BE46K19U342407","Miles":73929,"Zip":"45040","Radius":100}
```

Returns `{html, rawJson, data, request, requestedAtUtc, demand}`. `html` is a self-styled report fragment and `rawJson` preserves the full price provider response. `data` contains that parsed response. The demand lookup runs alongside pricing and returns `ready`, `insufficient_data` or `unavailable`.

DataOne-style output endpoints:

- `GET /api/service/price-research/4T1BE46K19U342407/html?miles=73929&zip=45040&radius=100`
- `GET /api/service/price-research/4T1BE46K19U342407/json?miles=73929&zip=45040&radius=100`

Each makes one provider call. Mileage is required. ZIP must be five digits; whole-number mileage ranges from 0 to 2,000,000 and radius from 1 to 500 miles. Dealer type is fixed to `independent`. Caller-supplied provider URLs are not accepted.

Responses use no-store caching. Provider errors are reported without exposing credentials or upstream response bodies. The API's HTML report encodes text and permits only HTTP(S) listing links. The Price Research page displays its own results and comparable grid without a separate formatted-report preview.

## Verification

Run `Scripts/Test-PriceResearch.ps1` for full compilation, service/renderer fixtures, auction bid calculations, authenticated HTTP validation, and the actual MVC routes/Admin authorization for both research pages. The HTTP checks also verify that the retired market-report endpoints return 404. Add `-BuildLocal` to update the local API binary. Add `-Live` to make one real provider request with the configured server key; live checks do not create database keys or records.
