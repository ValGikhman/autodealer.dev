# Acquisition HTML API

## Browser request

```text
https://localhost:44398/api/service/acquisition/5J8TC2H84PL017341/html?mileage=99669&desiredProfit=3000&access_token=YOUR_ACCOUNT_API_TOKEN
```

Use a single slash before `api`. Substitute any valid 17-character VIN and integer mileage (0–2,000,000). Desired profit defaults to $3,000 and accepts 0–1,000,000 USD. The response is styled HTML displayed directly in the browser; no downloaded output file is required.

Use the complete registered AutoDealer credential (`ad_live_KEY_ID.SECRET`), not the OpenAI key or public demo credential. Existing vin:read scope, subscription validation, quota and request logging apply. For server requests, prefer `Authorization: Bearer <account token>` to query-string credentials. Query tokens can appear in history/server logs; use HTTPS. The OpenAI key remains server-side.

## Research workflow and configuration

1. Decode the VIN using NHTSA vPIC (cached 24 hours).
2. Send the full eight-part acquisition prompt, decoded identity, VIN, mileage, research date and desired profit to the Responses API.
3. Let the reasoning model run multiple web searches and follow up on individual listings, conflicting prices, unavailable pages and the exact subject VIN.
4. Require a structured report, retain links found in tool sources, exclude subject listings, duplicate VINs/URLs, placeholder dealers and unsourced listings, and render escaped HTML on the server.

The acquisition research model defaults to `gpt-6.1-sol` with medium reasoning and a maximum of 20 built-in tool calls per report. Override using `OpenAI:AcquisitionModel` with a model supporting Responses web search and structured output. There is no silent fallback to a less capable model after an error. This setting is separate from GTX's `OpenAI:ChatModel`, so story generation keeps its existing model.

Uses the same `OpenAI:ApiKey` and `OpenAI:ResponsesUrl` as GTX stories, with `store=false`. Locally, `OpenAI:SharedConfigPath=..\GTX\Web.config` follows the existing shared-development-config pattern. Production must provision the same protected OpenAI settings or an accessible protected shared configuration. No real credential is committed in source.

Reasoning web research can take several minutes and uses more API resources than the previous mini-model lookup workflow. The upstream HTTP timeout is six minutes. Configure ASP.NET `httpRuntime executionTimeout="420"` and reverse-proxy timeouts accordingly. The local ignored Web.config has that setting. ChatGPT's app and API web research can return different sources/results; the original ChatGPT model is not identified by the linked chat metadata. Identical conclusions are not guaranteed.

Official reference: https://developers.openai.com/api/docs/guides/tools-web-search

## Report layout

The visible report contains the vehicle context/research date, a six-column nearby dealer comparison table, and one compact auction bid/profit summary. The summary shows the mean-based resale assumption, suggested/maximum bids, estimated costs, investment and profit, plus downside profit when inputs exist. Missing prices or essential costs produce one brief calculation-unavailable message, never a competing numeric and Unavailable decision table.

No standalone average/median diagnostics, reasoning sections, detailed acquisition budgets, risk/verification lists or Research sources section are displayed. Actual source links stay in dealer rows. Clean title and average condition are assumptions, not verified history. One or two real listings still produce table rows and a conditional calculation when usable data exists.

Trim matching tolerates word order and equivalent package wording (for example SH-AWD A-SPEC ADVANCE versus A-Spec Advance Package SH-AWD), while preserving meaningful trim differences. Approximate/straight-line distance labels are parsed; unknown distances remain visible but do not enter the radius-limited average.

## Price and bid calculations

The primary pricing baseline is the arithmetic average of usable advertised prices in the displayed dealer comparison table. Research targets matching year/make/model within 250 miles, across all trims. Trim/mileage differences are descriptive, not hidden reasons to discard a displayed price. The server displays the sum, count and average in the hammer-bid summary and uses that average as the assumed resale price. Prices with no unambiguous cash amount are explicitly marked excluded; monthly payments are not cash prices.

Under clean-title/average-condition assumptions, complete costs and a profitable mean-based ceiling produce BID LOWER (or a model BUY). Missing history, inspection or current auction bid alone does not force PASS. PASS applies if no eligible average exists, essential costs are missing, or the ceiling cannot satisfy desired profit. The median remains a separate optional diagnostic, not the primary bid baseline.

Raw median calculations use close year/make/model/trim matches, separate title groups, prices/mileages/distances that can be parsed, and a 250-mile limit. Prefer ±20,000 miles; expand to ±40,000 only when needed. Unknown prices never become zero. One/two-listing medians are explicitly small observed-sample statistics, not market valuations. Three eligible listings are required for an indicative range. These are asking prices, not confirmed sales or automatic depreciation adjustments.

Conditional underwriting uses the matching-YMMS arithmetic average described above. Expanded year/trim comparisons remain contextual references and are not included in that baseline. Invalid or duplicate rows do not invalidate the remaining eligible evidence; narrative findings stay visible even if no supported bid is available.

All financial totals are computed by the server from numeric inputs:

```text
F(B) = fixedAuctionFeeUsd + auctionFeeRate * B
Other costs = transport + inspection + repairs + detailing + acquisition tax/title + holding + selling expenses
Maximum B = (expected selling price - desired profit - other costs - fixed auction fee) / (1 + auction fee rate)
Total investment = hammer bid + F(hammer bid) + other costs
Profit = expected selling price - total investment
Downside profit = downside selling price - total investment - additional downside repairs
```

Round maximum/recommended bids DOWN to $100; cap recommended bid at maximum. The rate is a fraction (`0.025` means 2.5%). Missing cost categories/assumptions, insufficient evidence or invalid fee inputs prevent a supported bid. Auction fees and the hammer bid must not be counted again in other costs. Auction fee models, acquisition-tax eligibility, title/history/condition and all budgets are provisional until verified; tiered fee schedules may require a different actual quote.

The reference example's $29,000 sale, $2,500 other costs, $400+2.5% fee and $3,000 profit produce a $22,500 rounded maximum, $25,962.50 investment and $3,037.50 profit. Those values are regression fixtures only and are not production valuation defaults.

## Supply your own dealer comparables

```http
POST /api/service/acquisition/{vin}/html
Authorization: Bearer YOUR_ACCOUNT_API_TOKEN
Content-Type: application/json
```

The POST route uses NHTSA plus deterministic calculations; it makes no OpenAI request and fetches no arbitrary supplied URLs. Caller-supplied data is labeled as such rather than independently verified. Supply 1–30 actual observations from the past 30 days (no more than five minutes in the future).

Request template — replace all example listing values with actual observed values; these are not market evidence:

```json
{
  "mileage": 99669,
  "desiredProfit": 3000,
  "comparables": [
    {
      "dealer": "ACTUAL DEALER NAME",
      "location": "City, State",
      "distanceMiles": 120,
      "year": 2023,
      "make": "Acura",
      "model": "RDX",
      "seriesTrim": "Actual listed package",
      "vehicle": "2023 Acura RDX — actual trim/drivetrain",
      "mileage": 90000,
      "askingPrice": 30000,
      "titleStatus": "unknown",
      "priceConditions": "Actual fees/discounts/conditions, or unknown",
      "listingUrl": "https://actual-dealer.example/actual-listing",
      "observedAtUtc": "2026-10-28T12:00:00Z"
    }
  ]
}
```

Comparable VIN is optional; include it when known for deduplication. The subject VIN is excluded. Title status is clean, unknown, salvage or rebuilt. Year/make/model/seriesTrim fields are optional, while vehicle description is required for matching.

An optional `underwriting` object accepts expectedSellingPriceUsd, recommendedBidUsd, fixedAuctionFeeUsd, auctionFeeRate, transportUsd, inspectionUsd, repairsUsd, detailingUsd, taxAndTitleUsd, holdingUsd, sellingExpensesUsd, downsideSellingPriceUsd, downsideExtraRepairsUsd, optional competitiveListingLowUsd/competitiveListingHighUsd/quickSaleLowUsd/quickSaleHighUsd, and assumptions (array of strings). Numeric unknowns are null, not guessed zeroes. Assumptions/amounts remain caller supplied.

## Validation

Invalid inputs: 400. Missing/invalid authentication: 401. Scope/subscription denial: 403. Quota: 429. Missing configuration/authentication disabled: 503. Research failure: 502 with a safe stage-specific code where available. Timeout: 504.

Run financial regression checks without paid API calls:

```powershell
./Scripts/Test-AcquisitionUnderwriting.ps1
```

These check the external reference's bid/profit/downside arithmetic, fee units, missing-cost rejection, desired-profit handling, expanded comparisons, evidence rejection and final decision. Live GET checks incur OpenAI usage; the POST route can test rendering/calculations with explicitly synthetic fixtures.
