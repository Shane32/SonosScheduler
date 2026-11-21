# Windows Server, IIS, and SQL Server Express

## Build

Use the .NET SDK selected by `global.json` and Node/npm compatible with `ReactApp/package.json`.

```powershell
dotnet tool restore
dotnet restore
dotnet test Tests --no-restore
dotnet format --no-restore --verify-no-changes
dotnet publish AppServer -c Release -o artifacts/iis
```

Publishing installs the client dependencies, runs GraphQL codegen and the production React build, and includes the SPA, compressed assets, persisted documents, and the generated IIS `web.config`. It does not deploy to a server. `-p:SkipClientBuild=true` is for backend-only testing and produces an incomplete web application unless assets are supplied separately.

## IIS setup

1. Install IIS with Application Initialization and WebSocket Protocol, and the .NET 11 Hosting Bundle matching this application. See Microsoft's [ASP.NET Core IIS guide](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-11.0).
2. Create a dedicated application pool with **No Managed Code**, x64 enabled, **Start Mode = AlwaysRunning**, **Idle Time-out = 0**, and **Maximum Worker Processes = 1**. Disable overlapping recycling; use planned maintenance for replacement. The background workers must stay running without browser traffic.
3. Create a site or convert a virtual directory into an IIS **application**, using the dedicated pool. Set **Preload Enabled = True**, enable Anonymous Authentication, and configure a LAN hostname binding and HTTPS. Restrict inbound access to the intended LAN with server/network firewall rules. Configure [IIS Application Initialization](https://learn.microsoft.com/en-us/iis/get-started/whats-new-in-iis-8/iis-80-application-initialization) so the pool starts the app after reboot/recycle.
4. Copy `artifacts/iis` to the site directory while the application is stopped or `app_offline.htm` is present. Keep environment-specific configuration outside the build artifact. The pool identity needs read/execute access to the published files. Configure the private environment settings described below on the application pool before starting it.
5. Ensure the server can reach every configured subnet on TCP 1400. The speakers need their own NAS access; the application browses the speakers' indexed libraries. No SSDP relay is required because discovery uses configured unicast subnets.

For a child application, publish with its URL prefix, including the trailing slash. For example, an application named `music` uses:

```powershell
dotnet publish AppServer -c Release -o artifacts/iis -p:AppBasePath=/music/
```

The default prefix is `/`. IIS supplies the application's path base to ASP.NET Core; the client build uses the same prefix for assets and API requests. Use a separate pool for each ASP.NET Core application.

## Publishing from a workstation

Publishing is local; no deployment workflow is required. Install the IIS **Management Service** role service before installing the current [Microsoft Web Deploy](https://www.iis.net/downloads/microsoft/web-deploy) package with its handler. Web Deploy 4 requires .NET Framework 4.7.2 or later; older Windows Server installations may need the supported 4.8 update and a planned restart first. This is separate from the application's .NET Hosting Bundle. Enable remote connections in IIS Management Service, select a certificate matching the server's publishing hostname, and start WMSVC automatically. Allow TCP 8172 only from authorized publishing workstations. Windows administrators can publish with their existing Windows credentials; delegate application access explicitly if using a non-administrator account.

Keep the real server address, application name, credentials, and certificate in an ignored `AppServer/Properties/PublishProfiles/*.pubxml` or `.local-deploy/` file. Trust a private publishing certificate obtained through an authenticated administration channel before publishing; do not disable TLS validation. Application-pool environment settings stay on the server across publishes. Stop the application or use Web Deploy's AppOffline rule while replacing files.

For publishing with the current Windows credentials, set the `WindowsAuthenticationEnabled` DWORD to `1` under `HKLM\SOFTWARE\Microsoft\WebManagement\Server`, restart WMSVC, and use `authType=NTLM` without a stored username or password. Confirm the publishing port's HTTP.sys certificate binding matches the selected certificate; changing the registry certificate hash alone may leave the original binding in place.

## Entra registration and private configuration

Use one Microsoft Entra app registration in the intended directory:

1. Select **Accounts in this organizational directory only**.
2. Add the **Single-page application** platform. Register the exact callback and sign-out return URLs: `https://<app-host>/<application-path>/oauth/callback` and `https://<app-host>/<application-path>/oauth/logout`. Add `http://localhost:5145/oauth/callback` and `http://localhost:5145/oauth/logout` for development.
3. The client requests `openid profile offline_access` and uses the ID token as its bearer credential, matching the template. The `profile` scope supplies the display-name claim; the server refreshes existing administrator labels from the validated token. An exposed API scope is not needed for this flow.
4. Use authorization code flow with PKCE. Do not create a client secret or enable the legacy implicit grant for this SPA. The app does not need Microsoft Graph directory-read permissions to maintain administrator grants.

See Microsoft's [SPA configuration](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-spa-app-configuration) and [authorization code flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow) documentation.

Configure these server settings:

| Key                                            | Purpose                                                                                                                                     |
| ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `Authentication:DefaultScheme`                 | `Bearer`, matching the template's JWT authentication scheme.                                                                                |
| `Authentication:Schemes:Bearer:Authority`      | `https://login.microsoftonline.com/<tenant-id>/v2.0`, using the directory GUID.                                                             |
| `Authentication:Schemes:Bearer:ValidAudience`  | Application/client GUID for the SPA.                                                                                                        |
| `Authentication:Schemes:Bearer:ValidateIssuer` | `true` for the configured single-tenant registration.                                                                                       |
| `Administrators:BootstrapObjectId`             | First administrator's user object GUID in that directory; used only while no grants exist.                                                  |
| `Sonos:RequireSignIn`                          | `false` preserves public LAN controls and schedules; `true` requires Entra authentication for them. Administration always requires a grant. |
| `KeyVaultName`                                 | Optional Azure Key Vault name; leave empty for a local installation.                                                                        |
| `AZURE_CLIENT_ID`                              | Optional user-assigned managed identity client GUID for the restored OAuth token proxy. Leave unset for direct SPA token exchange.          |

Find a user's object ID in **Entra ID → Users → the user → Overview → Object ID**, inside the configured directory. Guest users also have an object ID in that directory. Use this value for bootstrap and for subsequent administrator grants.

The browser receives its client ID, authority, scopes, and redirect URLs from `/api/oauth/config`, derived from these settings and the application's URL and path. These are public OAuth configuration, even when their deployment-specific values are kept outside Git. There is no client secret. The restored `OAuthController` can proxy token exchange using a managed identity assertion when `AZURE_CLIENT_ID` is configured; the Entra registration must trust that identity through a federated credential. Without it, this on-premises SPA exchanges its PKCE authorization code directly with Entra.

For development, keep the private values in [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets), outside the repository:

```powershell
dotnet user-secrets set "Authentication:Schemes:Bearer:Authority" "https://login.microsoftonline.com/<tenant-id>/v2.0" --project AppServer
dotnet user-secrets set "Authentication:Schemes:Bearer:ValidAudience" "<client-id>" --project AppServer
dotnet user-secrets set "Authentication:Schemes:Bearer:ValidateIssuer" "true" --project AppServer
dotnet user-secrets set "Administrators:BootstrapObjectId" "<admin-object-id>" --project AppServer
dotnet user-secrets set "Sonos:RequireSignIn" "false" --project AppServer
```

The SDK project has a generic User Secrets identifier; the settings themselves are stored in the current developer's profile. User Secrets are a development configuration store, not an encrypted production vault. Do not copy their file into the published application.

For IIS, use protected application-pool configuration/environment variables or another protected server configuration source. Set values on the dedicated pool, restrict access to the server configuration to administrators, and recycle the pool after changing them. Do not put private values into checked-in `appsettings.json`, `web.config`, frontend environment files, build artifacts, or a public publish profile. IIS environment-variable names use double underscores:

```text
Authentication__DefaultScheme=Bearer
Authentication__Schemes__Bearer__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0
Authentication__Schemes__Bearer__ValidAudience=<client-id>
Authentication__Schemes__Bearer__ValidateIssuer=true
Administrators__BootstrapObjectId=<admin-object-id>
Sonos__RequireSignIn=false
ConnectionStrings__AppDbContext=<private-connection-string>
ASPNETCORE_ENVIRONMENT=Production
```

HTTPS and the exact registered origin are required for a deployed sign-in flow. When changing a hostname or IIS application path, update the registered redirect URLs and rebuild with the matching `AppBasePath`.

The template's optional Key Vault provider is retained. `KeyVaultName` is read after the local JSON file, development User Secrets, and environment variables have been loaded. When configured, Key Vault secrets are loaded afterward and can override those settings; use double hyphens in vault secret names for configuration separators, such as `Authentication--Schemes--Bearer--ValidAudience`. Debug builds use `VisualStudioCredential`; Release builds use `DefaultAzureCredential`. Grant the selected identity permission to read the vault's secrets. An empty `KeyVaultName` skips the provider entirely, so a local IIS deployment needs no Azure runtime identity.

## Database and first administrator

Create `SonosScheduler` in SQL Server Express and grant the application pool's Windows identity access. For a local instance, a connection string can use:

```text
Server=.\SQLEXPRESS;Database=SonosScheduler;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;
```

Use a trusted SQL Server certificate and `TrustServerCertificate=false` when provisioned. The default checked-in connection string uses SQL LocalDB for development; LocalDB is not the IIS deployment database.

Store the connection string in User Secrets for development or the protected IIS configuration described above. Use integrated Windows authentication to SQL Server where possible.

Startup applies generated EF migrations and creates the configured Entra administrator grant only if no grants exist. The runtime SQL identity therefore needs schema migration permissions; alternatively apply the migration with a deployment identity before startup. Sign in with that Entra account, add another administrator by their user object ID in the same directory, and remove `Administrators:BootstrapObjectId` from configuration. An optional display name makes a new grant recognizable before its first sign-in; subsequent validated tokens refresh that label from Entra. There is no local password login or public first-user registration endpoint.

`AdministratorGrants` contains its row ID, tenant ID, user object ID, and human-readable display name. The display name is a label, not a login name or authorization key. The Entra migration drops the previous local administrator table and its credentials. Back up the database before upgrading; old local accounts cannot be used after the upgrade. Any retained private configuration or credential files from an earlier deployment should be removed after moving to Entra.

Migration startup retries transient SQL connection failures up to five times, including pre-login timeouts while SQL Express starts after a server reboot. IIS allows up to 300 seconds for application startup. Normal database operations keep their existing transaction behavior; startup retry does not replay speaker commands or schedule execution.

Back up the SQL database regularly. The database contains subnet configuration, discovered rooms, schedules, Entra administrator grants, and the last 30 days of activity. It contains no local usernames or passwords. Logs are purged daily, so the retention boundary can lag by up to a day. A failed scheduled attempt is not retried automatically; check logs and run playback manually when needed.

## Local development

Set the Entra settings with User Secrets as above. The default LocalDB connection string can be overridden there with `ConnectionStrings:AppDbContext`. In two terminals:

```powershell
cd ReactApp
npm ci --ignore-scripts
npm run dev
```

```powershell
dotnet run --project AppServer --launch-profile http
```

Open `http://localhost:5145`, which must be a registered SPA redirect URL. ASP.NET proxies the Vite server on port 5173, keeping client and API requests on the same origin. Sign in with Microsoft, configure a network in Admin, then refresh speakers. The app initially contains no demonstration rooms or music.

## Model/schema changes

Always generate migrations with EF tooling:

```powershell
dotnet ef migrations add ChangeName --project AppDb
dotnet ef database update --project AppDb
```

Do not hand-author migrations. After GraphQL changes, run the schema approval test, inspect its received file, approve the intended schema, and run `npm run codegen` in `ReactApp`. Keep production assets and their persisted-document file from the same build together.
