import { useEffect, useState } from "react";
import { useMutation, useQuery } from "@shane32/graphql";
import { Alert, Modal } from "react-bootstrap";
import { MusicNoteBeamed, PauseFill, PlayFill, Soundwave } from "react-bootstrap-icons";
import * as Q from "./App.queries.g";
import type { NetworkGroup, RunAction, Speaker } from "./shared";
import { dateTime, stateLabel } from "./shared";
import { LibraryPicker } from "./LibraryPicker";
import { Schedules } from "./Schedules";
import { Admin } from "./Admin";
import { useSonosAuth } from "./auth";
import { SignOutButton } from "./Authentication";

export default function App() {
  const [tab, setTab] = useState(() => (window.location.hash === "#admin" ? "admin" : "rooms"));
  const { authenticated } = useSonosAuth();
  const [groupId, setGroupId] = useState("");
  const [speakerId, setSpeakerId] = useState("");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<{ error: boolean; text: string }>();
  const query = useQuery(Q.SonosDashboardDocument);
  const { refetch } = query;
  const [refresh] = useMutation(Q.SonosRefreshGroupDocument);
  useEffect(() => {
    const timer = window.setInterval(() => refetch(), 30000);
    return () => window.clearInterval(timer);
  }, [refetch]);
  const groups = query.data?.groups.items ?? [];
  const group = groups.find((x) => x.id === groupId) ?? groups[0];
  const speaker = group?.speakers.find((x) => x.id === speakerId) ?? group?.speakers[0];
  const run: RunAction = async (task, message) => {
    setBusy(true);
    setNotice(undefined);
    try {
      await task();
      query.refetch();
      if (message) setNotice({ error: false, text: message });
      return true;
    } catch (error) {
      setNotice({ error: true, text: error instanceof Error ? error.message : "The request failed." });
      return false;
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <header className="topbar">
        <div className="container-fluid app-width d-flex align-items-center justify-content-between gap-3">
          <a
            className="brand"
            href="#"
            onClick={(e) => {
              e.preventDefault();
              setTab("rooms");
            }}
          >
            <span className="brand-icon" aria-hidden="true">
              <Soundwave />
            </span>
            <span>
              Sonos <strong>Scheduler</strong>
            </span>
          </a>
          <div className="d-flex flex-wrap align-items-center justify-content-end gap-2">
            <span className="network-label">Local listening</span>
            {authenticated && <SignOutButton />}
          </div>
        </div>
      </header>
      <main className="container-fluid app-width py-4 py-lg-5">
        <div className="page-intro">
          <span className="eyebrow">YOUR MUSIC, THROUGHOUT YOUR DAY</span>
          <h1>{tab === "rooms" ? "Every room. In harmony." : tab === "schedules" ? "A rhythm for your day." : "Administration"}</h1>
          <p className="text-body-secondary">
            {tab === "rooms"
              ? "Bring your music library to the spaces you love."
              : tab === "schedules"
                ? "Choose the music, the rooms, and the moment."
                : "Manage your networks, administrators, and activity."}
          </p>
        </div>
        <nav className="main-nav" aria-label="Main navigation">
          {[
            ["rooms", "Rooms & music"],
            ["schedules", "Schedules"],
            ["admin", "Admin"],
          ].map(([id, name]) => (
            <button
              key={id}
              className={tab === id ? "active" : ""}
              aria-current={tab === id ? "page" : undefined}
              onClick={() => setTab(id)}
            >
              {name}
            </button>
          ))}
        </nav>
        {notice && (
          <Alert
            className="operation-notice"
            variant={notice.error ? "danger" : "success"}
            dismissible
            onClose={() => setNotice(undefined)}
            role="alert"
          >
            {notice.text}
          </Alert>
        )}
        {query.error && (
          <Alert variant="danger">
            Could not load the application. {query.error.message}{" "}
            <button className="btn btn-sm btn-outline-danger ms-2" onClick={() => query.refetch()}>
              Retry
            </button>
          </Alert>
        )}
        {query.loading && !query.data && <p role="status">Loading your listening spaces…</p>}
        {tab === "admin" ? (
          <Admin
            groups={groups}
            isAdmin={query.data?.session.isAdmin ?? false}
            name={query.data?.session.name ?? ""}
            run={run}
            busy={busy}
            onSessionChange={() => query.refetch()}
          />
        ) : (
          <>
            <section className="network-toolbar" aria-label="Network group">
              <div>
                <label htmlFor="group" className="form-label">
                  Network group
                </label>
                <select
                  id="group"
                  className="form-select"
                  value={group?.id ?? ""}
                  onChange={(e) => {
                    setGroupId(e.target.value);
                    setSpeakerId("");
                  }}
                >
                  <option value="" disabled>
                    Select a network
                  </option>
                  {groups.map((g) => (
                    <option key={g.id} value={g.id}>
                      {g.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="network-summary">
                {group && (
                  <>
                    <strong>
                      {group.speakers.filter((s) => s.isOnline).length}{" "}
                      <span className="fw-normal text-body-secondary">/ {group.speakers.length} rooms online</span>
                    </strong>
                    <small>Discovery updated {dateTime(group.lastRefreshedAt)}</small>
                  </>
                )}
              </div>
              <button
                className="btn btn-outline-primary"
                disabled={!group || busy}
                onClick={() => group && void run(() => refresh({ variables: { id: group.id } }), "Discovery completed.")}
              >
                {busy ? "Working…" : "Refresh speakers"}
              </button>
            </section>
            {group?.lastError && <Alert variant="warning">{group.lastError}</Alert>}
            {!group && !query.loading ? (
              <div className="card empty-state">
                <span className="empty-note" aria-hidden="true">
                  <MusicNoteBeamed />
                </span>
                <h2 className="h4">Your first listening space</h2>
                <p>Add a network in Admin, then discover the Sonos speakers on it.</p>
                <button className="btn btn-primary" onClick={() => setTab("admin")}>
                  Open Admin
                </button>
              </div>
            ) : (
              group &&
              (tab === "rooms" ? (
                <div className="row g-4">
                  <div className="col-12 col-md-5 col-xl-4">
                    <section className="card">
                      <div className="card-body">
                        <span className="eyebrow">CONNECTED SPACES</span>
                        <h2 className="h5 mb-0">
                          Speakers <span className="text-body-secondary ms-2">{group.speakers.length}</span>
                        </h2>
                      </div>
                      <div className="room-table-heading">
                        <span>Room</span>
                        <span>Status</span>
                      </div>
                      {group.speakers.map((s) => (
                        <button
                          key={s.id}
                          className={`room-row ${s.id === speaker?.id ? "selected" : ""}`}
                          aria-pressed={s.id === speaker?.id}
                          onClick={() => setSpeakerId(s.id)}
                        >
                          <span>
                            <strong>{s.name}</strong>
                            <small>{s.address}</small>
                          </span>
                          <span className={`state-label ${s.isOnline && s.state === "PLAYING" ? "playing" : ""}`}>{stateLabel(s)}</span>
                        </button>
                      ))}
                      {!group.speakers.length && <p className="p-4 text-body-secondary">Refresh speakers to discover this network.</p>}
                    </section>
                  </div>
                  <div className="col-12 col-md-7 col-xl-8">
                    {speaker && <SpeakerPanel key={speaker.id} speaker={speaker} group={group} busy={busy} run={run} />}
                  </div>
                </div>
              ) : (
                <Schedules key={group.id} group={group} busy={busy} run={run} />
              ))
            )}
          </>
        )}
        <footer className="app-footer">
          <strong>Sonos Scheduler</strong>
          <span>Music from your NAS. Made for your local network.</span>
        </footer>
      </main>
    </>
  );
}

function SpeakerPanel({ speaker, group, busy, run }: { speaker: Speaker; group: NetworkGroup; busy: boolean; run: RunAction }) {
  const [volumeSelection, setVolumeSelection] = useState<{ observed: number; value: number }>();
  if (volumeSelection && volumeSelection.observed !== speaker.volume) setVolumeSelection(undefined);
  const volume = volumeSelection?.observed === speaker.volume ? volumeSelection.value : speaker.volume;
  const [grouping, setGrouping] = useState(false);
  const [browse, setBrowse] = useState(false);
  const [members, setMembers] = useState<string[]>([]);
  const [control] = useMutation(Q.SonosControlDocument);
  const [refresh] = useMutation(Q.SonosRefreshSpeakerDocument);
  const [saveMembers] = useMutation(Q.SonosSetMembersDocument);
  const [play] = useMutation(Q.SonosPlayDocument);
  const current = group.speakers.filter(
    (x) => x.sonosId === speaker.coordinatorId || x.coordinatorId === speaker.coordinatorId || x.id === speaker.id,
  );
  return (
    <>
      <section className="card now-playing">
        <div className="card-body p-lg-4">
          <div className="d-flex justify-content-between gap-3">
            <div>
              <span className="eyebrow">NOW LISTENING IN</span>
              <h2 className="h3">{speaker.name}</h2>
            </div>
            <span className="state-label align-self-start text-capitalize">{stateLabel(speaker)}</span>
          </div>
          <div className="track-display">
            <div className="album-art" aria-hidden="true">
              <i />
              <i />
              <i />
              <i />
              <i />
            </div>
            <div className="min-width-0">
              <h3 className="h4 text-break">{speaker.title || "Ready when you are"}</h3>
              <p className="mb-1 text-body-secondary">{speaker.artist || "Choose something from your music library"}</p>
              <small className="text-body-secondary">{speaker.album}</small>
            </div>
          </div>
          <div className="d-flex gap-2 flex-wrap">
            <button
              className="btn btn-primary playback-button"
              disabled={busy || !speaker.isOnline}
              onClick={() =>
                void run(() => control({ variables: { id: speaker.id, action: speaker.state === "PLAYING" ? "pause" : "play" } }))
              }
            >
              {speaker.state === "PLAYING" ? (
                <>
                  <PauseFill aria-hidden="true" /> Pause
                </>
              ) : (
                <>
                  <PlayFill aria-hidden="true" /> Play
                </>
              )}
            </button>
            <button className="btn btn-outline-primary" disabled={busy || !speaker.isOnline} onClick={() => setBrowse(true)}>
              Browse music library
            </button>
          </div>
          <form
            className="volume-control"
            onSubmit={(e) => {
              e.preventDefault();
              void run(() => control({ variables: { id: speaker.id, action: "volume", volume } }));
            }}
          >
            <label htmlFor="volume" className="form-label mb-0">
              Volume
            </label>
            <input
              id="volume"
              className="form-range"
              type="range"
              min="0"
              max="100"
              value={volume}
              onChange={(e) => setVolumeSelection({ observed: speaker.volume, value: Number(e.target.value) })}
              disabled={!speaker.isOnline || busy}
            />
            <output htmlFor="volume">{volume}%</output>
            <button className="btn btn-sm btn-outline-secondary" disabled={busy || !speaker.isOnline}>
              Apply
            </button>
          </form>
          {speaker.muted && <p className="small text-warning-emphasis">This room is muted in Sonos.</p>}
          <div className="d-flex justify-content-between flex-wrap gap-2 small text-body-secondary">
            <span>Status updated {dateTime(speaker.statusUpdatedAt)}</span>
            <button
              className="btn btn-link btn-sm p-0"
              disabled={busy}
              onClick={() => void run(() => refresh({ variables: { id: speaker.id } }), "Status updated.")}
            >
              Update status
            </button>
          </div>
        </div>
      </section>
      <section className="card mt-4">
        <div className="card-body p-lg-4">
          <div className="d-flex justify-content-between flex-wrap gap-3">
            <div>
              <span className="eyebrow">LISTEN TOGETHER</span>
              <h2 className="h5">Playback group</h2>
              <p className="text-body-secondary mb-0">{current.map((x) => x.name).join(" · ")}</p>
            </div>
            <button
              className="btn btn-outline-primary align-self-center"
              disabled={busy || !speaker.isOnline}
              onClick={() => {
                setMembers(current.map((x) => x.id));
                setGrouping(true);
              }}
            >
              Edit rooms
            </button>
          </div>
        </div>
      </section>
      <Modal show={grouping} onHide={() => setGrouping(false)} centered>
        <Modal.Header closeButton>
          <Modal.Title as="h2" className="h5">
            Play together with {speaker.name}
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="text-body-secondary">Selected rooms will form a playback group led by {speaker.name}.</p>
          {group.speakers.map((s) => (
            <label key={s.id} className="room-check">
              <input
                className="form-check-input"
                type="checkbox"
                checked={s.id === speaker.id || members.includes(s.id)}
                disabled={s.id === speaker.id || !s.isOnline}
                onChange={(e) => setMembers((m) => (e.target.checked ? [...m, s.id] : m.filter((id) => id !== s.id)))}
              />
              <span>{s.name}</span>
              <small className="text-body-secondary ms-auto">{!s.isOnline ? "Offline" : s.id === speaker.id ? "Lead room" : ""}</small>
            </label>
          ))}
        </Modal.Body>
        <Modal.Footer>
          <button className="btn btn-light" onClick={() => setGrouping(false)}>
            Cancel
          </button>
          <button
            className="btn btn-primary"
            disabled={busy}
            onClick={() =>
              void run(
                () => saveMembers({ variables: { coordinatorId: speaker.id, speakerIds: members } }),
                "Playback group updated.",
              ).then((ok) => {
                if (ok) setGrouping(false);
              })
            }
          >
            Apply group
          </button>
        </Modal.Footer>
      </Modal>
      {browse && (
        <LibraryPicker
          groupId={group.id}
          busy={busy}
          onClose={() => setBrowse(false)}
          onSelect={(item, selectionMode) =>
            void run(() => play({ variables: { id: speaker.id, objectId: item.id, selectionMode } }), "Playback started.").then((ok) => {
              if (ok) setBrowse(false);
            })
          }
        />
      )}
    </>
  );
}
