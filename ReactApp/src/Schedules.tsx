import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery } from "@shane32/graphql";
import { Modal } from "react-bootstrap";
import { Clock } from "react-bootstrap-icons";
import * as Q from "./App.queries.g";
import type { SonosAddScheduleMutationVariables } from "./gql/graphql";
import type { NetworkGroup, RunAction, Schedule } from "./shared";
import { dateTime, days, modes } from "./shared";
import { LibraryPicker } from "./LibraryPicker";

export function Schedules({ group, busy, run }: { group: NetworkGroup; busy: boolean; run: RunAction }) {
  const [after, setAfter] = useState<string>();
  const query = useQuery(Q.SonosSchedulesDocument, { variables: { groupId: group.id, after } });
  const [editing, setEditing] = useState<Schedule | "new">();
  const [pauseDays, setPauseDays] = useState<Record<string, number>>({});
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 30000);
    return () => window.clearInterval(timer);
  }, []);
  const [remove] = useMutation(Q.SonosDeleteScheduleDocument);
  const [enable] = useMutation(Q.SonosEnableScheduleDocument);
  const [pause] = useMutation(Q.SonosPauseScheduleDocument);
  const change: RunAction = async (task, message) => {
    const ok = await run(task, message);
    if (ok) query.refetch();
    return ok;
  };
  return (
    <section>
      <div className="section-toolbar">
        <div>
          <h2 className="h4 mb-1">Listening routines</h2>
          <p className="text-body-secondary mb-0">Music that starts on your terms.</p>
        </div>
        <button className="btn btn-primary" disabled={!group.speakers.length || busy} onClick={() => setEditing("new")}>
          + New schedule
        </button>
      </div>
      {query.error && <div className="alert alert-danger">{query.error.message}</div>}
      {query.loading && <p role="status">Loading schedules…</p>}
      <div className="row g-4">
        {query.data?.schedules.items.map((s) => {
          const paused = !!s.disabledUntil && new Date(s.disabledUntil).getTime() > now;
          return (
            <div key={s.id} className="col-12 col-xl-6">
              <article className="card h-100">
                <div className="card-body p-lg-4">
                  <div className="d-flex justify-content-between gap-3">
                    <div>
                      <span className={`schedule-state ${s.enabled && !paused ? "enabled" : ""}`}>
                        {!s.enabled ? "Disabled" : paused ? "Temporarily paused" : "Enabled"}
                      </span>
                      <h3 className="h5 mt-2">{s.name}</h3>
                    </div>
                    <button className="btn btn-sm btn-outline-secondary align-self-start" onClick={() => setEditing(s)}>
                      Edit
                    </button>
                  </div>
                  <div className="schedule-time">
                    {s.localTime}
                    <small>{s.daysOfWeek === 127 ? "Every day" : days.filter((_, i) => s.daysOfWeek & (1 << i)).join(" · ")}</small>
                  </div>
                  <dl className="schedule-facts">
                    <div>
                      <dt>Music</dt>
                      <dd>{s.libraryTitle}</dd>
                    </div>
                    <div>
                      <dt>Selection</dt>
                      <dd>{modes.find((m) => m[0] === s.selectionMode)?.[1]}</dd>
                    </div>
                    <div>
                      <dt>Rooms</dt>
                      <dd>
                        {s.speakers
                          .filter((x) => x.enabled)
                          .map((x) => group.speakers.find((r) => r.id === x.speakerId)?.name ?? "Missing room")
                          .join(" · ")}
                      </dd>
                    </div>
                    <div>
                      <dt>If playing</dt>
                      <dd>{s.onlyIfIdle ? "Start in idle rooms only" : "Override current playback"}</dd>
                    </div>
                    <div>
                      <dt>Time zone</dt>
                      <dd>{s.timeZoneId}</dd>
                    </div>
                    <div>
                      <dt>Exceptions</dt>
                      <dd>{s.exceptions.length ? s.exceptions.map((x) => x.date).join(", ") : "None"}</dd>
                    </div>
                  </dl>
                  {paused && <div className="alert alert-warning py-2">Paused until {dateTime(s.disabledUntil)}</div>}
                  <div className="schedule-actions">
                    <button
                      className="btn btn-sm btn-outline-primary"
                      disabled={busy}
                      onClick={() =>
                        void change(() => enable({ variables: { id: s.id, enabled: !s.enabled || paused } }), "Schedule updated.")
                      }
                    >
                      {!s.enabled || paused ? "Enable now" : "Disable"}
                    </button>
                    <div className="input-group input-group-sm pause-control">
                      <select
                        className="form-select"
                        aria-label={`Pause duration for ${s.name}`}
                        value={pauseDays[s.id] ?? 3}
                        onChange={(e) => setPauseDays({ ...pauseDays, [s.id]: Number(e.target.value) })}
                      >
                        {[1, 3, 7, 14, 30].map((d) => (
                          <option value={d} key={d}>
                            {d} day{d === 1 ? "" : "s"}
                          </option>
                        ))}
                      </select>
                      <button
                        className="btn btn-outline-secondary"
                        disabled={busy}
                        onClick={() =>
                          void change(() => pause({ variables: { id: s.id, days: pauseDays[s.id] ?? 3 } }), "Schedule paused.")
                        }
                      >
                        Pause for
                      </button>
                    </div>
                    <button
                      className="btn btn-link btn-sm text-danger ms-auto"
                      disabled={busy}
                      onClick={() => {
                        if (confirm(`Delete schedule “${s.name}”?`))
                          void change(() => remove({ variables: { id: s.id } }), "Schedule deleted.");
                      }}
                    >
                      Delete
                    </button>
                  </div>
                </div>
              </article>
            </div>
          );
        })}
      </div>
      {!query.loading && query.data?.schedules.items.length === 0 && (
        <div className="card empty-state">
          <span className="empty-note" aria-hidden="true">
            <Clock />
          </span>
          <h3 className="h4">A soundtrack for your day</h3>
          <p>Set up morning music, opening time, or a new album every day.</p>
          {!group.speakers.length && <p>Discover speakers first to create a schedule.</p>}
        </div>
      )}
      {(after || query.data?.schedules.pageInfo.hasNextPage) && (
        <div className="d-flex gap-2 mt-3">
          <button className="btn btn-outline-secondary" disabled={!after} onClick={() => setAfter(undefined)}>
            First page
          </button>
          <button
            className="btn btn-outline-secondary"
            disabled={!query.data?.schedules.pageInfo.hasNextPage}
            onClick={() => setAfter(query.data?.schedules.pageInfo.endCursor ?? undefined)}
          >
            Next page
          </button>
        </div>
      )}
      {editing && (
        <ScheduleEditor
          group={group}
          schedule={editing === "new" ? undefined : editing}
          busy={busy}
          run={change}
          onClose={() => setEditing(undefined)}
        />
      )}
    </section>
  );
}

type ScheduleInput = SonosAddScheduleMutationVariables["input"];
function ScheduleEditor({
  group,
  schedule,
  busy,
  run,
  onClose,
}: {
  group: NetworkGroup;
  schedule?: Schedule;
  busy: boolean;
  run: RunAction;
  onClose: () => void;
}) {
  const [input, setInput] = useState<ScheduleInput>(() => ({
    groupId: group.id,
    name: schedule?.name ?? "",
    daysOfWeek: schedule?.daysOfWeek ?? 62,
    localTime: schedule?.localTime ?? "08:00",
    timeZoneId: schedule?.timeZoneId ?? Intl.DateTimeFormat().resolvedOptions().timeZone,
    enabled: schedule?.enabled ?? true,
    onlyIfIdle: schedule?.onlyIfIdle ?? true,
    libraryObjectId: schedule?.libraryObjectId ?? "",
    libraryTitle: schedule?.libraryTitle ?? "",
    selectionMode: schedule?.selectionMode ?? "PlayAll",
    speakers: group.speakers.map((s) => ({
      speakerId: s.id,
      enabled: schedule?.speakers.find((x) => x.speakerId === s.id)?.enabled ?? false,
      volume: schedule?.speakers.find((x) => x.speakerId === s.id)?.volume ?? null,
    })),
    exceptions: schedule?.exceptions.map((x) => ({ date: x.date, reason: x.reason })) ?? [],
  }));
  const [browse, setBrowse] = useState(false);
  const [error, setError] = useState("");
  const [add] = useMutation(Q.SonosAddScheduleDocument);
  const [update] = useMutation(Q.SonosUpdateScheduleDocument);
  const rooms = input.speakers as { speakerId: string; enabled: boolean; volume?: number | null }[];
  const exceptions = input.exceptions as { date: string; reason?: string | null }[];
  const [exceptionKeys, setExceptionKeys] = useState(() => exceptions.map((_, index) => index));
  const nextExceptionKeyRef = useRef(exceptions.length);
  const set = <TKey extends keyof ScheduleInput>(key: TKey, value: ScheduleInput[TKey]) => setInput((old) => ({ ...old, [key]: value }));
  return (
    <>
      <Modal show={!browse} onHide={onClose} size="lg" scrollable centered backdrop={busy ? "static" : true}>
        <Modal.Header closeButton={!busy}>
          <Modal.Title as="h2" className="h5">
            {schedule ? "Edit schedule" : "New schedule"}
          </Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <form
            id="schedule-form"
            onSubmit={(e) => {
              e.preventDefault();
              if (!input.daysOfWeek) {
                setError("Select at least one day.");
                return;
              }
              if (!input.libraryObjectId) {
                setError("Choose music from the library.");
                return;
              }
              if (!rooms.some((x) => x.enabled)) {
                setError("Enable at least one room.");
                return;
              }
              setError("");
              void run(
                () => (schedule ? update({ variables: { id: schedule.id, input } }) : add({ variables: { input } })),
                "Schedule saved.",
              ).then((ok) => {
                if (ok) onClose();
              });
            }}
          >
            {error && (
              <div className="alert alert-danger" role="alert">
                {error}
              </div>
            )}
            <label className="form-label" htmlFor="schedule-name">
              Name
            </label>
            <input
              id="schedule-name"
              required
              maxLength={100}
              className="form-control mb-4"
              placeholder="Morning music"
              value={input.name}
              onChange={(e) => set("name", e.target.value)}
            />
            <fieldset className="mb-4">
              <legend className="form-label">Days of the week</legend>
              <div className="weekday-picker">
                {days.map((d, i) => (
                  <div key={d}>
                    <input
                      className="btn-check"
                      type="checkbox"
                      id={`day-${i}`}
                      checked={!!(input.daysOfWeek & (1 << i))}
                      onChange={() => set("daysOfWeek", input.daysOfWeek ^ (1 << i))}
                    />
                    <label className="btn btn-outline-primary" htmlFor={`day-${i}`}>
                      {d}
                    </label>
                  </div>
                ))}
              </div>
            </fieldset>
            <div className="row g-3 mb-4">
              <div className="col-12 col-sm-4">
                <label className="form-label" htmlFor="start-time">
                  Start time
                </label>
                <input
                  id="start-time"
                  required
                  type="time"
                  step="60"
                  className="form-control"
                  value={input.localTime}
                  onChange={(e) => set("localTime", e.target.value)}
                />
              </div>
              <div className="col-12 col-sm-8">
                <label className="form-label" htmlFor="time-zone">
                  Time zone
                </label>
                <input
                  id="time-zone"
                  required
                  className="form-control"
                  value={input.timeZoneId}
                  onChange={(e) => set("timeZoneId", e.target.value)}
                />
                <small className="text-body-secondary">For example, America/New_York. Follows daylight saving time.</small>
              </div>
            </div>
            <section className="editor-section">
              <div className="section-toolbar">
                <h3 className="h6 mb-0">What to play</h3>
                <button className="btn btn-sm btn-outline-primary" type="button" onClick={() => setBrowse(true)}>
                  {input.libraryObjectId ? "Change music" : "Choose music"}
                </button>
              </div>
              {input.libraryObjectId ? (
                <div className="chosen-music">
                  <strong>{input.libraryTitle}</strong>
                  <small>{modes.find((x) => x[0] === input.selectionMode)?.[1]}</small>
                </div>
              ) : (
                <p className="text-body-secondary">Choose a NAS track or folder.</p>
              )}
            </section>
            <section className="editor-section">
              <h3 className="h6">Rooms & starting volume</h3>
              <p className="small text-body-secondary">Enabled rooms will play as a group.</p>
              {rooms.map((room, i) => (
                <div className="schedule-room" key={room.speakerId}>
                  <label className="form-check mb-0">
                    <input
                      className="form-check-input"
                      type="checkbox"
                      checked={room.enabled}
                      onChange={(e) =>
                        set(
                          "speakers",
                          rooms.map((r, n) => (n === i ? { ...r, enabled: e.target.checked } : r)),
                        )
                      }
                    />
                    <span className="form-check-label">{group.speakers.find((s) => s.id === room.speakerId)?.name}</span>
                  </label>
                  <label className="form-check small mb-0">
                    <input
                      className="form-check-input"
                      type="checkbox"
                      disabled={!room.enabled}
                      checked={room.volume == null}
                      onChange={(e) =>
                        set(
                          "speakers",
                          rooms.map((r, n) => (n === i ? { ...r, volume: e.target.checked ? null : 25 } : r)),
                        )
                      }
                    />
                    <span className="form-check-label">Current volume</span>
                  </label>
                  <div className="input-group input-group-sm">
                    <input
                      className="form-control"
                      type="number"
                      min="0"
                      max="100"
                      required={room.enabled && room.volume != null}
                      disabled={!room.enabled || room.volume == null}
                      aria-label={`Volume for ${group.speakers.find((s) => s.id === room.speakerId)?.name}`}
                      value={room.volume ?? ""}
                      onChange={(e) =>
                        set(
                          "speakers",
                          rooms.map((r, n) => (n === i ? { ...r, volume: Number(e.target.value) } : r)),
                        )
                      }
                    />
                    <span className="input-group-text">%</span>
                  </div>
                </div>
              ))}
            </section>
            <fieldset className="editor-section">
              <legend className="h6">If music is already playing</legend>
              <label className="form-check">
                <input
                  className="form-check-input"
                  name="idle"
                  type="radio"
                  checked={input.onlyIfIdle}
                  onChange={() => set("onlyIfIdle", true)}
                />
                <span className="form-check-label">Start in idle rooms only</span>
              </label>
              <label className="form-check">
                <input
                  className="form-check-input"
                  name="idle"
                  type="radio"
                  checked={!input.onlyIfIdle}
                  onChange={() => set("onlyIfIdle", false)}
                />
                <span className="form-check-label">Override playback in enabled rooms</span>
              </label>
            </fieldset>
            <section className="editor-section">
              <div className="section-toolbar">
                <div>
                  <h3 className="h6 mb-1">Date exceptions</h3>
                  <small className="text-body-secondary">Skip these dates in the schedule’s time zone.</small>
                </div>
                <button
                  className="btn btn-sm btn-outline-secondary"
                  type="button"
                  onClick={() => {
                    const key = nextExceptionKeyRef.current++;
                    setExceptionKeys((keys) => [...keys, key]);
                    set("exceptions", [...exceptions, { date: "", reason: "" }]);
                  }}
                >
                  + Add date
                </button>
              </div>
              {exceptions.map((exception, i) => (
                <div className="exception-row" key={exceptionKeys[i]}>
                  <input
                    className="form-control"
                    required
                    type="date"
                    aria-label={`Exception date ${i + 1}`}
                    value={exception.date}
                    onChange={(e) =>
                      set(
                        "exceptions",
                        exceptions.map((x, n) => (n === i ? { ...x, date: e.target.value } : x)),
                      )
                    }
                  />
                  <input
                    className="form-control"
                    maxLength={500}
                    placeholder="Reason (optional)"
                    aria-label={`Exception reason ${i + 1}`}
                    value={exception.reason ?? ""}
                    onChange={(e) =>
                      set(
                        "exceptions",
                        exceptions.map((x, n) => (n === i ? { ...x, reason: e.target.value } : x)),
                      )
                    }
                  />
                  <button
                    className="btn btn-outline-danger btn-sm"
                    type="button"
                    onClick={() => {
                      setExceptionKeys((keys) => keys.filter((_, n) => n !== i));
                      set(
                        "exceptions",
                        exceptions.filter((_, n) => n !== i),
                      );
                    }}
                  >
                    Remove
                  </button>
                </div>
              ))}
              {!exceptions.length && <p className="small text-body-secondary">No exceptions.</p>}
            </section>
            <label className="form-check form-switch mt-4">
              <input
                className="form-check-input"
                role="switch"
                type="checkbox"
                checked={input.enabled}
                onChange={(e) => set("enabled", e.target.checked)}
              />
              <span className="form-check-label">Schedule enabled</span>
            </label>
          </form>
        </Modal.Body>
        <Modal.Footer>
          <button className="btn btn-light" disabled={busy} onClick={onClose}>
            Cancel
          </button>
          <button className="btn btn-primary" form="schedule-form" type="submit" disabled={busy}>
            {busy ? "Saving…" : "Save schedule"}
          </button>
        </Modal.Footer>
      </Modal>
      {browse && (
        <LibraryPicker
          groupId={group.id}
          busy={busy}
          onClose={() => setBrowse(false)}
          onSelect={(item, mode) => {
            setInput((old) => ({ ...old, libraryObjectId: item.id, libraryTitle: item.title, selectionMode: mode }));
            setBrowse(false);
          }}
        />
      )}
    </>
  );
}
