# Verification

Run the application tests with `dotnet test Tests`. They use SQLite and a fake Sonos HTTP transport, not speakers on the developer's LAN. The schema and representative GraphQL results use reviewed approval files. SQL Server rowversion is omitted in the SQLite fixture; run generated migrations against LocalDB to check the real SQL Server schema.

Covered behavior includes anonymous dashboard access, administrator authorization, schedule creation/editing/pause/delete, audit entries, unexpected GraphQL exceptions, timezone/DST/midnight/grace-window scheduling, duplicate execution suppression, cached and fallback discovery, offline rooms, recursive NAS selections, metadata preservation, coordinator delegation, idle-only playback, and service URL confinement.

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

Hardware and browser checks require the target network and working browser automation or manual inspection; a protocol simulation cannot establish device-firmware compatibility or visual layout correctness.
