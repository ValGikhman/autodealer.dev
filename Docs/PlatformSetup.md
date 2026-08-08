# AutoDealer.dev platform setup

## 1. Create the database

Run `Database/001_AutoDealerPlatform.sql` in the target SQL Server database.
The migration creates clients, password credentials, plans, subscriptions,
tokenized payment profiles, API keys, detailed request logs, daily aggregates,
indexes, and constraints.

For local development, the `AutoDealer.dev` connection targets the
`AUTODEALER.DEV` catalog on `VALS-PC`. Set deployment-specific connection strings through a release
transform or a protected configuration provider. Do not commit production
credentials.

The application data model is `Data/autodealer_dev.dbml`. Its generated
`AutoDealerDataContext` is used by the account and API-access services. When
the schema changes, refresh the DBML in Visual Studio before updating service
queries; do not restore a separate handwritten entity mapping.

## 2. Configure API authentication

After the migration succeeds, set `ApiSecurity:Enabled` to `true`. All Web API
routes will then require `Authorization: Bearer ad_live_KEY_ID.SECRET`.

The secret is generated with a cryptographic random-number generator. Only its
SHA-256 digest is stored. Because the secret has 256 bits of entropy, it cannot
be recovered from the database; rotate the key if the original is lost.

`ApiAccessService` authenticates keys and counts each accepted request inside a
serializable LINQ-to-SQL transaction. The response adds
`X-Request-Id`, `X-RateLimit-Limit`, and `X-RateLimit-Remaining`. Completion
records status code, duration, and error totals. Archive detailed rows according
to your retention policy while retaining `ApiUsageDaily` for billing.

## 3. Configure credential email

Configure the `Smtp:*` application settings with secrets supplied by the host.
If SMTP is unavailable, registration still succeeds and the one-time credential
is displayed in the browser. For stronger delivery guarantees, replace direct
SMTP with an outbox table and background mail worker.

## 4. Configure payments safely

Use a PCI-compliant provider's hosted checkout or hosted fields. Browser card
fields must submit directly to that provider, which returns an opaque payment
method token. Post only that token as `Registration.PaymentMethodToken`.

The application intentionally does not store or decrypt primary account numbers
or CVVs. `PaymentProfiles` stores provider identifiers plus optional card brand,
last four digits, and expiry for display. Verify provider webhooks before using
them to activate, renew, pause, or cancel subscriptions.

## 5. Production checklist

- Rotate any DataOne credentials that previously appeared in configuration and
  inject replacements outside source control.
- Require HTTPS and HSTS at the application gateway or IIS.
- Protect SMTP, database, DataOne, and payment-provider secrets with the host's
  secret store or protected configuration.
- Set `customErrors` appropriately and disable compilation debugging.
- Add email verification, password reset, sign-in, CSRF protection for every
  browser mutation, and administrative key rotation before exposing a portal.
- Add a privacy policy, terms, retention schedule, webhook signature checks,
  audit logging, monitoring, backups, and an incident-response process.
- Put rate limiting at the edge as well as enforcing plan quota in the API access service.

## Search, sitemap, and Google measurement setup

Production publishing applies `Web.Release.config`, which enables indexing and
disables compilation debugging. Confirm these deployment values rather than
putting environment-specific IDs directly in a Razor view:

```xml
<add key="Seo:SiteUrl" value="https://autodealer.dev" />
<add key="Seo:AllowIndexing" value="true" />
<add key="GoogleAnalytics:MeasurementId" value="G-XXXXXXXXXX" />
<add key="GoogleSearchConsole:VerificationToken" value="verification-token" />
```

Keep `Seo:AllowIndexing=false` in local, staging, and preview deployments. The
application then emits `noindex,nofollow` and a blocking `robots.txt`. A Release
deployment serves the public sitemap at `https://autodealer.dev/sitemap.xml` and
references it from `https://autodealer.dev/robots.txt`.

For Google Analytics 4:

1. Sign in to Google Analytics and create or select the property owned by the
   business responsible for AutoDealer.dev.
2. In **Admin > Data streams**, create a Web stream for
   `https://autodealer.dev`, or select the existing stream if this site already
   has one.
3. Copy its Measurement ID (`G-...`) into the protected deployment value for
   `GoogleAnalytics:MeasurementId`.
4. Deploy, visit the production site, and use **Admin > Data streams > Test your
   website**, Tag Assistant, and the Realtime report to verify collection.

Do not reuse another site's Measurement ID merely because you control that
site. Reuse it only when both domains intentionally belong in the same GA4 web
journey and property; otherwise create a separate web stream so reporting is not
mixed. The layout does not load Google Analytics when the setting is blank or
does not match the `G-...` format.

For Google Search Console:

1. Add `autodealer.dev` as a Domain property and complete the recommended DNS
   TXT verification. If URL-prefix verification is used instead, place the
   provided HTML meta token in `GoogleSearchConsole:VerificationToken`.
2. Submit `https://autodealer.dev/sitemap.xml` under **Sitemaps**.
3. Inspect the home page and key solution pages with **URL Inspection**, then
   request indexing after the production deployment is confirmed.
4. Validate the home-page Organization markup and pricing Service markup with
   Google's Rich Results Test, and monitor Core Web Vitals after real-user data
   becomes available.

## Registration lifecycle

1. The dedicated MVC account controller applies model validation and anti-forgery protection.
2. The injected account service uses LINQ-to-SQL mapped entities inside a
   serializable transaction to create the client, password credential, trial
   subscription, and primary API key.
3. The full key is returned once and sent by email when SMTP is configured.
4. Subsequent API calls authenticate the key hash and enforce its scope and plan.
5. Request and daily usage rows provide per-client metering and billing inputs.

## Trial-expiration job

Run `Database/003_ClientEmailHistory.sql` and
`Database/004_TrialExpirationAutomation.sql`, then set `Billing:PaymentUrl` in
`Web.config` to the authenticated hosted-payment flow. Do not point it at a form
that posts raw card data to this application.

Run the job manually from the application directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Invoke-TrialExpiration.ps1
```

Schedule that command hourly or daily with Windows Task Scheduler under an
identity that can read the deployed configuration and update the application
database. The job serializes concurrent runs, changes newly expired `trialing`
subscriptions to `paused`, emails the customer, and records the rendered message
in `ClientEmailHistory`. Failed deliveries remain eligible for retry after 60
minutes. Use `-WhatIf` to list work without changing status or sending email,
and use `-ConnectionName` when the machine-based connection selection is not
appropriate.
