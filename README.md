# Sonos Scheduler

A local-network Sonos controller and scheduler built with React, Bootstrap/Sass, GraphQL.NET, and EF Core. Music comes from Sonos-indexed NAS libraries.

- Discover rooms by subnet, view status, control playback, and group rooms.
- Schedule music by weekday and time, with individual room volumes, idle-only playback, date exceptions, and temporary pauses.
- Browse folders or choose tracks/subfolders randomly or by progress through the year.
- Administer networks and administrators, and review 30 days of activity.

## Run locally

Install the SDK in `global.json`, Node/npm from `ReactApp/package.json`, and SQL Server LocalDB.

1. Configure the first admin with `Admin:BootstrapName` and `Admin:BootstrapPassword` in an ignored `AppServer/appsettings.Local.json` (12+ character password).
2. Run `npm ci --ignore-scripts` and `npm run dev` in `ReactApp`.
3. Run `dotnet run --project AppServer --launch-profile http` and open `http://localhost:5145`.
4. Sign in to Admin, add your speaker subnet, and refresh speakers.

LAN visitors can control rooms and schedules without signing in. Only administrators can configure networks, manage administrators, and read logs.

## Build and deploy

`dotnet publish AppServer -c Release -o artifacts/iis` builds and packages the server and client for IIS. Production uses SQL Server Express. IIS must keep the application running for schedules to fire.

See [deployment and configuration](docs/deployment.md) and [architecture and behavior](docs/architecture.md). Run `dotnet test Tests` and `dotnet format` when changing the application. Generate database migrations with `dotnet ef`; generate client operations with the existing GraphQL codegen.

`ConsoleApp2` remains as the standalone protocol reference.
