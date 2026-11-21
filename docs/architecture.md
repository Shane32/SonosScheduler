# Design decisions

The template's React / GraphQL.NET / EF Core structure is retained. `AppDb` contains the SQL Server model and generated migrations, `AppGraphQL` contains the schema and input mappings, and `AppServices` owns Sonos protocol operations, scheduling, administrator credentials, audit logging, and background workers. EF is used directly; there is no repository layer.

## Networks and rooms

A **network group** is an administrator-configured IPv4 CIDR subnet. A **playback group** is the current Sonos grouping of rooms. They are deliberately separate concepts. The database is the source for normal navigation and cached status; live topology is resolved before playback commands, so changes from the Sonos app are respected.

Discovery runs on startup and hourly, or on demand with Refresh speakers. It tries the configured seed and cached speaker addresses first. A successful topology response supplies the rest of the household. If no cached speaker responds, up to 24 probes run concurrently, with a one-second probe deadline, until the first valid speaker supplies topology. Scans support /16 through /32; smaller ranges refresh faster. Only visible controllable rooms in the configured subnet are persisted. Bonded satellites and bridges are not independent rooms. Unreachable rooms remain in the database and are marked offline so schedules retain their room selections.

Each network group currently represents one Sonos household. Separate S1/S2 households should use distinct configured ranges. Overlapping subnet groups are permitted but discouraged because the same physical room could then be controlled through two independent groups.

Speaker descriptions and service URLs must stay on a literal IPv4 address in the configured subnet at HTTP TCP 1400. Service redirects and proxies are disabled. Browsers contact only the application server; the server needs network routes and firewall access to every configured speaker subnet.

Cached status updates during hourly/manual discovery, explicit status refresh, and application playback actions. The browser rereads database data every 30 seconds. There are no UPnP subscriptions in this version, so external Sonos activity may require Update status or Refresh speakers to appear.

## Music and grouping

Only Sonos-indexed NAS audio with `x-file-cifs:` resource URIs is queued. Shared folders (`S:`) and the indexed library (`A:`) are browsed on a reachable speaker with ContentDirectory paging. The application server does not mount NAS shares; configure and index them in Sonos first. Track metadata is preserved when adding items to a queue.

Folder selection is recursive, cycle-aware, and bounded to 32 levels, 100,000 browsed entries, and 10,000 queued tracks. Exceeding a limit fails before playback rather than silently dropping content. Children sort by title (ordinal, case-insensitive), then object ID, recursively. Library edits can change this order and therefore the day-of-year selection.

| Selection | Behavior |
| --- | --- |
| Play the selection | Queue the track or every NAS track under the selected folder. |
| Random track | Uniformly choose one descendant track. |
| Track by day of year | Choose one descendant by progress through the local calendar year. |
| Random subfolder | Uniformly choose a nonempty immediate child folder; queue its full subtree. |
| Subfolder by day of year | Choose a nonempty immediate child folder by calendar progress; queue its subtree. |

If no nonempty child folders exist, folder modes use the selected folder. Calendar progress is `(dayOfYear - 1) / (daysInYear - 1)`, including leap years: January 1 selects the first item and December 31 the last. Scheduled selection uses the schedule's local date; immediate playback uses the server's local date.

Selections append to the existing coordinator queue and start at the first newly added track. This preserves the prior queue. A direct Play/Pause applies to the selected room's live coordinator; volume belongs to the individual room.

Schedules check live coordinator state, including `TRANSITIONING`, before deciding that a room is idle. An unknown or unavailable room is excluded from idle-only execution. A coordinator leaving unselected rooms delegates coordination before detaching, so other rooms can retain their playback. Selected rooms are detached and regrouped, individual non-null volumes are set, and the prepared selection starts. Operations on a subnet are serialized within the process. Sonos operations are not transactional: a device outage can leave a partially applied group or queue, which is recorded as a failure. Physical controls and other Sonos clients can still race an operation.

## Scheduling

Schedules store an explicit time zone, local `HH:mm`, a weekday mask (Sunday = 1), enabled state, optional UTC pause deadline, date exclusions, music selection, and room/volume settings. A null volume means keep the room's current value. All LAN visitors can edit schedules; the audit actor is the signed-in administrator name or request IP. This follows the requested LAN trust model rather than pretending anonymous visitors have verified identities.

The scheduler polls every 15 seconds. It considers occurrences in a five-minute grace window, including the previous local date around midnight. Edits take effect prospectively using `UpdatedAt`. Date exclusions use the schedule's local calendar. Spring-forward times that do not exist are skipped; a repeated fall-back time runs only at its earlier UTC occurrence.

Before device operations, a run row is committed with a unique `(ScheduleId, ScheduledFor)` index. This gives **at-most-once attempts**, including across process restarts. A claim is never automatically retried after an uncertain network result or crash; retrying could restart music unexpectedly. Downtime longer than five minutes is not replayed. Administrators can inspect trigger and completion/failure logs. This is not an exactly-once guarantee for physical playback. Run a single active application instance; IIS web gardens and overlapping application workers are unsupported for device control.

## Access, schema, and logs

LAN use requires no account. Administrator sign-in uses locally stored PBKDF2 password hashes and an eight-hour, HttpOnly, SameSite Strict cookie. The first administrator comes from server configuration only when the administrator table is empty. Administrators add/remove other administrators, with a transaction protecting the last account. Cookie validation checks the database on every request, so removing an account revokes its sessions.

Administrative fields are protected in GraphQL, not just hidden by the UI. JSON POSTs require the `X-Sonos-Request: 1` header and a same-origin request. No cross-origin API access is enabled. Sign-in is rate-limited per remote IP. IIS supplies the network access boundary; this application intentionally trusts anyone who can reach the LAN site for room and schedule control.

Database entities use `EfObjectGraphType`, loader-backed navigation lists, root Relay connections, and grouped `DIObjectGraphBase` mutations. AutoMapper handles input properties; server-owned fields and relationship identity are handled explicitly. Client operations live only in `.queries.ts`; real codegen creates the bridge documents and production persisted documents from the approved server schema. The server accepts full documents as well as persisted IDs; authorization remains the access boundary.

Schedule edits, triggers, results, administrator changes, discovery, and unexpected GraphQL exceptions go to `Logs`. Expected input errors do not become exception logs. Exception logging uses a fresh service scope so a failed request unit of work does not prevent log storage. Daily maintenance deletes log and execution rows older than 30 days. Database/logging failure still goes to the host logger.

The historical initial migration is retained so existing template databases can upgrade; the EF-generated Sonos migration drops the sample blog tables. Back up an existing database before upgrading. SQLite tests omit SQL Server rowversion behavior; migration application is also checked against SQL LocalDB.

## UI

Bootstrap is compiled from Sass in `ReactApp/src/styles.scss`. Edit `$grid-breakpoints` before the Bootstrap import to tailor phone/tablet layouts. The room/status columns stack with the playback controls on narrow screens; tables scroll within their cards; editor dialogs scroll and preserve keyboard focus through React Bootstrap. The client handles loading, offline, empty, permission, and operation-failure states.

Protocol parsing and baseline control behavior derive from the retained `ConsoleApp2` reference. No streaming accounts, streaming catalog, or cloud Sonos API are used.
