# Sonos console controller

A .NET 10 console app using direct local HTTP/SOAP calls, with no NuGet dependencies or cloud account credentials.

## Run

Install the .NET 10 SDK, then run from the repository root:

```powershell
dotnet run --project ConsoleApp2
```

Select a speaker, then view status, browse the music library, search by song title, or browse the shared folders. Choose a numbered folder to open it or a numbered song to play it. Use `N`/`P` for pages, `B` to go back, or `Q`/Ctrl+C to exit. Pause and resume are also available.

Status shows the playback state, title, artist, album, elapsed time, duration, track URI, and the selected speaker's volume/mute state.

## Configuration

Edit `ConsoleApp2/appsettings.json` before running. It is copied beside the executable on build and publish. By default, the app reads the copy beside the executable. To use a separate file:

```powershell
dotnet run --project ConsoleApp2 -- --settings C:\path\to\appsettings.json
```

| Setting under `Sonos` | Default | Purpose |
| --- | --- | --- |
| `SpeakerAddresses` | `[]` | Optional seed IPs/hostnames, e.g. `["192.168.1.50"]`. When supplied, these replace multicast discovery; topology reveals other speakers. An HTTP URL with a non-default port is also accepted. Seeds can be combined with `DiscoverySubnet`. |
| `DiscoverySubnet` | `null` | Optional IPv4 CIDR range to scan instead of SSDP, e.g. `"192.168.50.0/24"`. Null or blank keeps default discovery. Prefixes `/16` through `/32` are supported. |
| `SubnetScanTimeoutSeconds` | `2` | Timeout for each subnet probe (1–30 seconds), also bounded by `HttpTimeoutSeconds`. |
| `SubnetScanConcurrency` | `64` | Maximum simultaneous subnet probes (1–256). |
| `DiscoveryTimeoutSeconds` | `5` | How long to collect discovery replies (1–60 seconds). |
| `DiscoveryInterfaceAddress` | `null` | Optional IPv4 address of **this computer's** LAN adapter, useful when a VPN or multiple adapters interfere. Used only for automatic discovery. |
| `HttpTimeoutSeconds` | `10` | Per-request timeout (1–120 seconds). |
| `LibraryPageSize` | `25` | Results requested per page (1–200). The app handles speakers returning fewer results. |

The computer must be able to reach the speakers. Default SSDP discovery uses multicast UDP `239.255.255.250:1900`; speaker HTTP normally uses TCP `1400`. If discovery fails, configure a speaker IP or subnet. Sleeping/offline speakers, guest Wi-Fi isolation, firewall rules, and separate VLANs can prevent access.

For example, to find speakers on another subnet, set these fields under `Sonos` in `appsettings.json`:

```json
"SpeakerAddresses": [],
"DiscoverySubnet": "192.168.50.0/24"
```

With a subnet configured, the app sends HTTP requests to `/xml/device_description.xml` on TCP port `1400` for each usable address. It does not require multicast or ping replies. The operating system must have a route to that subnet and its firewall must allow TCP `1400`; setting a subnet does not create network routes. `DiscoveryInterfaceAddress` only selects the interface for SSDP and does not control subnet scanning.

Host bits in a CIDR value are normalized (`192.168.50.123/24` scans `192.168.50.0/24`). Network and broadcast addresses are skipped except in `/31` and `/32` ranges. Failed/non-Sonos probes are quiet; progress and a summary are displayed. Larger ranges take longer: a `/24` has 254 candidates, while a `/16` has 65,534. Use Ctrl+C to cancel. Any configured seed speakers are loaded too, and their addresses are not probed again. As with default discovery, topology can reveal additional rooms outside the scanned range.

## Music library and groups

Configure and index your shared music folder using the Sonos app first. This app browses Sonos's existing library index and sends the returned URI and metadata back to Sonos. The speakers fetch the audio directly from your share using their existing share configuration; this console app needs neither the share path nor its username/password. If results are empty, check that Sonos itself can browse and play your library, and update its music index after adding songs. Song search uses Sonos's title search; browse artists/albums/folders for other ways to find a track.

Playback/status requests use the current group coordinator. If the selected speaker is grouped, playback, pause, and resume affect **all rooms in that group**, which are displayed in the console. Stereo/surround satellites and bridges are hidden from selection. Existing groups are retained.

Playing a song appends it to the existing group queue, switches the source to that queue, seeks to the added song, and starts playback. The existing queue and play mode are retained, so subsequent playback follows that queue's repeat/shuffle settings. These are several separate network requests: if one fails, earlier steps (such as adding the song) may already have succeeded. Check status before retrying. Changes to groups or queues by another controller during these requests may also interrupt playback.

This uses the local UPnP services, not the Sonos cloud API. The approach and protocol details can be compared with the maintained [SoCo library implementation](https://github.com/SoCo/SoCo/blob/master/soco/music_library.py) and [playback implementation](https://github.com/SoCo/SoCo/blob/master/soco/core.py). Local API availability depends on speaker firmware and network access.

## Validation

Build and run the dependency-free protocol checks from the repository root:

```powershell
dotnet build ConsoleApp2
dotnet run --project ConsoleApp2.Tests
```

The checks use a simulated HTTP handler: they verify discovery from seed addresses and CIDR subnets, subnet validation and address boundaries, scan concurrency/timeouts/cancellation, device/service parsing, coordinator routing, status, paged library responses, XML escaping, queue playback commands, and SOAP failures without contacting speakers or changing real playback. Real hardware playback still needs to be checked on your Sonos network.
