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

1. Install IIS, its Application Initialization feature, and the .NET 11 Hosting Bundle matching this application. See Microsoft's [ASP.NET Core IIS guide](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-11.0).
2. Create a dedicated application pool with **No Managed Code**, x64 enabled, **Start Mode = AlwaysRunning**, **Idle Time-out = 0**, and **Maximum Worker Processes = 1**. Disable overlapping recycling; use planned maintenance for replacement. The background workers must stay running without browser traffic.
3. Create the site with **Preload Enabled = True**, Anonymous Authentication enabled, a LAN hostname binding, and HTTPS. Restrict inbound access to the intended LAN with server/network firewall rules. Configure [IIS Application Initialization](https://learn.microsoft.com/en-us/iis/get-started/whats-new-in-iis-8/iis-80-application-initialization) so the pool starts the app after reboot/recycle. Publish to a dedicated site root, not an IIS virtual subdirectory.
4. Copy `artifacts/iis` to the site directory while the application is stopped or `app_offline.htm` is present. Keep environment-specific configuration outside the build artifact. The pool identity needs read/execute access to the published files and write access to its ASP.NET Core Data Protection key store. Enable **Load User Profile** or configure a persistent protected key store so cookie keys survive restarts.
5. Ensure the server can reach every configured subnet on TCP 1400. The speakers need their own NAS access; the application browses the speakers' indexed libraries. No SSDP relay is required because discovery uses configured unicast subnets.

## Database and first administrator

Create `SonosScheduler` in SQL Server Express and grant the application pool's Windows identity access. For a local instance, a connection string can use:

```text
Server=.\SQLEXPRESS;Database=SonosScheduler;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;
```

Use a trusted SQL Server certificate and `TrustServerCertificate=false` when provisioned. The default checked-in connection string uses SQL LocalDB for development; LocalDB is not the IIS deployment database.

Copy `AppServer/appsettings.Local.example.json` to `appsettings.Local.json` in the deployed directory, replace its bootstrap password with a unique 12–200 character password, and update the connection string. The local settings file is ignored by Git. Environment variables can instead set:

```text
ConnectionStrings__AppDbContext
Admin__BootstrapName
Admin__BootstrapPassword
ASPNETCORE_ENVIRONMENT=Production
```

Startup applies generated EF migrations and creates the configured administrator only if there are no administrators. The runtime SQL identity therefore needs schema migration permissions; alternatively apply the migration with a deployment identity before startup. After first sign-in, add a second administrator and remove the bootstrap password from configuration. There is no public first-user registration endpoint.

Back up the SQL database regularly. The database contains subnet configuration, discovered rooms, schedules, credentials, and the last 30 days of activity. Logs are purged daily, so the retention boundary can lag by up to a day. A failed scheduled attempt is not retried automatically; check logs and run playback manually when needed.

## Local development

Set bootstrap credentials in an ignored `AppServer/appsettings.Local.json` or environment variables. In two terminals:

```powershell
cd ReactApp
npm ci --ignore-scripts
npm run dev
```

```powershell
dotnet run --project AppServer --launch-profile http
```

Open `http://localhost:5145`. ASP.NET proxies the Vite server on port 5173, keeping API requests and admin cookies same-origin. Configure a network in Admin, then refresh speakers. The app initially contains no demonstration rooms or music.

## Model/schema changes

Always generate migrations with EF tooling:

```powershell
dotnet ef migrations add ChangeName --project AppDb
dotnet ef database update --project AppDb
```

Do not hand-author migrations. After GraphQL changes, run the schema approval test, inspect its received file, approve the intended schema, and run `npm run codegen` in `ReactApp`. Keep production assets and their persisted-document file from the same build together.
