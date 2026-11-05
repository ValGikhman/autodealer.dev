# Admin API credentials

Run Database/012_ApiKeyProtectedCredential.sql before deploying this change. It adds a nullable encrypted credential column to dbo.ApiKeys; no existing credentials change. The admin API-key column and edit dialog now display the complete credential for keys created after deployment. Existing hash-only keys display a replacement message; their secrets cannot be recovered.

Both registration and additional-key issuance protect the full credential with ASP.NET MachineKey before storing it. Authentication continues using SHA-256 SecretHash. Only existing AdminAuthorize endpoints return full credentials and responses disable caching. Keep the application's machineKey stable across deployments, backups/restores and all web-farm nodes; protect its configuration. Losing/changing that key makes stored credentials unreadable. Do not commit actual machineKey values. An administrator with access to this page can copy the complete API credential.

Issue a new API key for legacy credentials after verifying the account subscription is active and unexpired. Verify the replacement in a request before revoking the previous key. This change does not extend subscriptions or change API access checks.

For legacy keys, the admin display also checks the customer's saved credential email history. A candidate is displayed only when its complete key ID matches and its secret's SHA-256 matches the stored SecretHash. This does not change or activate any key, extend subscriptions, or send mail. Recovery is possible only when the original credential email was retained. Missing or mismatched candidates still require replacement.
