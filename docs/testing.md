# Verification

Run the application tests with `dotnet test Tests`. They use SQLite and a fake Sonos HTTP transport, not speakers on the developer's LAN. The schema and representative GraphQL results use reviewed approval files. SQL Server rowversion is omitted in the SQLite fixture; run generated migrations against LocalDB to check the real SQL Server schema.

Windows application and test builds enable app-local ICU. Leave NLS and invariant globalization disabled: those modes disable .NET's cross-platform time-zone ID support. Tests cover IANA IDs, Windows IDs, aliases, seasonal offsets, daylight-saving transitions, and full scheduler execution. On an older Windows server, verify native `TimeZoneInfo` lookup using the published application's runtime configuration and dependencies so the ICU files are tested as deployed. In the browser, confirm a new schedule captures the browser's zone without an editable zone box and editing a saved schedule preserves its zone.

Covered behavior includes anonymous dashboard access, administrator authorization, immutable Entra grants and bootstrap, display-name synchronization from validated tokens, duplicate-grant rejection, last-administrator protection, identifier-based audit entries, schedule creation/editing/pause/delete, unexpected GraphQL exceptions, timezone/DST/midnight/grace-window scheduling, duplicate execution suppression, cached and fallback discovery, offline rooms, recursive NAS selections, metadata preservation, coordinator delegation, idle-only playback, and service URL confinement.

Authentication tests use synthetic identities and placeholder GUIDs; they do not contact Entra or require real credentials in the repository. Signed test tokens exercise the template's bearer handler and `DbAuthService`, verify that existing grants receive updated display names, and ensure an ungranted identity cannot create its own grant. Audit assertions keep the actor tied to immutable IDs rather than a mutable name. Check the real sign-in separately against a private test registration: confirm PKCE sign-in, the ID-token audience and tenant issuer, administrator denial for an ungranted account, and immediate loss of administrator access after its grant is removed. Confirm that the administrator list shows the signed-in user's name. With `Sonos:RequireSignIn=false`, anonymous room and schedule access should work; with it enabled, those API calls must require a valid token. Confirm that admin operations require a database grant in either mode. Verify `/api/oauth/config` returns only public browser settings and retains the application's IIS path prefix. A local installation with an empty `KeyVaultName` and no `AZURE_CLIENT_ID` must start without contacting Azure for credentials.

OAuth tests use a fake managed identity and HTTP transport to verify authorization-code and refresh-token exchanges without Azure calls. WebSocket tests cover token-validation events, allowed browser origins, rejection of cross-site anonymous connections, and loss of administrator access after grant revocation or token expiry on an existing connection.

The retained console reference also has a deterministic protocol harness:

```powershell
dotnet run --project ConsoleApp2.Tests
```

Run `dotnet format --no-restore --verify-no-changes` and `npm run build` in `ReactApp` (or publish the server, which builds the client). The generated schema must be approved before client codegen can validate operations. Review received approval files before accepting them.

For SQL startup recovery, use a disposable LocalDB database and a `DbConnectionInterceptor` that throws `TimeoutException` on its first two asynchronous opens. Verify `DatabaseStartupService.MigrateAsync` applies the generated migrations, then confirm an ordinary context still supports an explicit transaction without global retries. Cancel during a simulated connection failure to verify startup stops retrying. This scenario was checked against LocalDB during deployment verification; the normal test suite remains independent of LocalDB.

For a real installation, verify:

- Discovery of a known subnet, then rediscovery after its original seed is powered off.
- Pause/play/volume and adding/removing rooms while changes are also made in the Sonos app.
- Browsing the actual NAS shares and playing both tracks and nested folders.
- A near-future schedule with idle rooms and currently playing rooms, including a mixed selection.
- Phone and iPad portrait/landscape layouts, keyboard use, and scrollable dialogs.
- IIS schedules firing while no browser is open, and correct recovery after reboot/recycle.
- Microsoft sign-in and sign-out on the deployed HTTPS origin, including a child application path when configured.

Hardware and browser checks require the target network and working browser automation or manual inspection; a protocol simulation cannot establish device-firmware compatibility or visual layout correctness.
