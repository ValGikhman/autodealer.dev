# Monthly API quota

Only a signed-in Majordome administrator can change a key's monthly request limit:

1. Open **Majordome → Customers**.
2. Expand the customer and open the API-key editor.
3. Set **Monthly request quota → Unlimited** and save.

Choose **Use plan quota** to restore the subscription plan's normal limit. The setting applies to the selected API key; it does not change the plan or other keys' overrides. All existing and newly issued keys default to the plan quota.

The edit action retains admin authorization and antiforgery validation. There is no public API endpoint or configuration switch to enable unlimited usage. Requests cannot enable the setting through price research parameters.

Unlimited bypasses only the AutoDealer.dev monthly request-count check. Usage and errors continue to be recorded, and the admin grid shows the actual usage with an Unlimited limit. Active key/client checks, expiration, subscription validity and scopes remain enforced. MarketCheck/DataOne provider subscription limits and charges are separate. Usage totals remain aggregated by subscription as before.

## Deployment

Apply `Database/013_ApiKeyMonthlyQuota.sql` before deploying the new application binary. It adds only `dbo.ApiKeys.IsQuotaUnlimited`, a non-null bit defaulting to false; it does not create any Market Inventory tables or legacy stored procedures and does not upgrade existing keys automatically.

Run `Scripts/Test-ApiMonthlyQuota.ps1` to compile the application and verify quota behavior using a disposable local SQL database. Add `-BuildLocal` to update the local application binary after successful checks. No production database, actual customer key or provider API is used by these tests.
