import { useState } from "react";
import { useQuery } from "@shane32/graphql";
import { Modal } from "react-bootstrap";
import { FolderFill, MusicNoteBeamed } from "react-bootstrap-icons";
import * as Q from "./App.queries.g";
import type { SonosLibraryQuery } from "./gql/graphql";
import { modes } from "./shared";
export type LibraryItem = SonosLibraryQuery["library"]["items"][number];

export function LibraryPicker({
  groupId,
  busy,
  onClose,
  onSelect,
}: {
  groupId: string;
  busy: boolean;
  onClose: () => void;
  onSelect: (item: LibraryItem, mode: string) => void;
}) {
  const [path, setPath] = useState([{ id: "S:", title: "Shared folders" }]);
  const [start, setStart] = useState(0);
  const [selected, setSelected] = useState<LibraryItem>();
  const [mode, setMode] = useState("PlayAll");
  const folder = path[path.length - 1];
  const query = useQuery(Q.SonosLibraryDocument, { variables: { groupId, objectId: folder.id, start, count: 100 } });
  const result = query.data?.library;
  function root(id: string, title: string) {
    setPath([{ id, title }]);
    setStart(0);
    setSelected(undefined);
  }
  return (
    <Modal show onHide={onClose} size="xl" scrollable centered backdrop={busy ? "static" : true}>
      <Modal.Header closeButton={!busy}>
        <Modal.Title as="h2" className="h5">
          Choose from your music library
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <div className="library-layout">
          <div>
            <div className="d-flex gap-2 mb-3">
              <button
                className={`btn btn-sm ${path[0].id === "S:" ? "btn-primary" : "btn-outline-primary"}`}
                onClick={() => root("S:", "Shared folders")}
              >
                Shared folders
              </button>
              <button
                className={`btn btn-sm ${path[0].id === "A:" ? "btn-primary" : "btn-outline-primary"}`}
                onClick={() => root("A:", "My music library")}
              >
                My music library
              </button>
            </div>
            <nav aria-label="Library location" className="library-breadcrumb">
              {path.map((p, i) => (
                <button
                  key={p.id}
                  className="btn btn-link btn-sm"
                  disabled={i === path.length - 1}
                  onClick={() => {
                    setPath(path.slice(0, i + 1));
                    setStart(0);
                    setSelected(undefined);
                  }}
                >
                  {p.title}
                </button>
              ))}
            </nav>
            {path.length > 1 && (
              <button
                className="btn btn-sm btn-outline-primary mb-3"
                onClick={() => setSelected({ ...folder, isContainer: true, artist: "", album: "" })}
              >
                Select this folder
              </button>
            )}
            {query.error && (
              <div className="alert alert-danger" role="alert">
                {query.error.message}{" "}
                <button className="btn btn-sm btn-outline-danger" onClick={() => query.refetch()}>
                  Retry
                </button>
              </div>
            )}
            {query.loading ? (
              <p role="status">Reading your music library…</p>
            ) : (
              <div className="library-list">
                {result?.items.map((item) => (
                  <div key={item.id} className={`library-row ${selected?.id === item.id ? "selected" : ""}`}>
                    <span className="music-glyph" aria-hidden="true">
                      {item.isContainer ? <FolderFill /> : <MusicNoteBeamed />}
                    </span>
                    <div className="flex-grow-1 min-width-0">
                      {item.isContainer ? (
                        <button
                          className="folder-button"
                          onClick={() => {
                            setPath([...path, { id: item.id, title: item.title }]);
                            setStart(0);
                            setSelected(undefined);
                          }}
                        >
                          {item.title}
                          <small>Open folder</small>
                        </button>
                      ) : (
                        <>
                          <strong className="text-break">{item.title}</strong>
                          <small className="d-block text-body-secondary">{[item.artist, item.album].filter(Boolean).join(" · ")}</small>
                        </>
                      )}
                    </div>
                    <button
                      className={`btn btn-sm ${selected?.id === item.id ? "btn-primary" : "btn-outline-secondary"}`}
                      onClick={() => {
                        setSelected(item);
                        setMode("PlayAll");
                      }}
                      aria-label={`Select ${item.title}`}
                    >
                      {selected?.id === item.id ? "Selected" : "Select"}
                    </button>
                  </div>
                ))}
                {result?.items.length === 0 && <p className="p-4 mb-0 text-body-secondary">This folder is empty.</p>}
              </div>
            )}
            <div className="d-flex align-items-center justify-content-between flex-wrap gap-2 mt-3">
              <small>
                {result?.totalMatches
                  ? `${start + 1}–${Math.min(start + (result.numberReturned ?? 0), result.totalMatches)} of ${result.totalMatches}`
                  : "0 items"}
              </small>
              <div className="btn-group">
                <button
                  className="btn btn-sm btn-outline-secondary"
                  disabled={start === 0 || query.loading}
                  onClick={() => setStart(Math.max(0, start - 100))}
                >
                  Previous
                </button>
                <button
                  className="btn btn-sm btn-outline-secondary"
                  disabled={query.loading || start + (result?.numberReturned ?? 0) >= (result?.totalMatches ?? 0)}
                  onClick={() => setStart(start + (result?.numberReturned ?? 100))}
                >
                  Next
                </button>
              </div>
            </div>
          </div>
          <aside className="selection-panel">
            <span className="eyebrow">YOUR SELECTION</span>
            <h3 className="h5 text-break">{selected?.title ?? "Find your next soundtrack"}</h3>
            {selected?.isContainer ? (
              <fieldset>
                <legend className="h6 mt-4">How should it play?</legend>
                {modes.map(([value, label, description]) => (
                  <label key={value} className="mode-option">
                    <input
                      className="form-check-input"
                      type="radio"
                      name="selection"
                      checked={mode === value}
                      onChange={() => setMode(value)}
                    />
                    <span>
                      <strong>{label}</strong>
                      <small>{description}</small>
                    </span>
                  </label>
                ))}
              </fieldset>
            ) : (
              <p className="text-body-secondary">{selected ? "Play this track." : "Select a track or folder from your NAS library."}</p>
            )}
          </aside>
        </div>
      </Modal.Body>
      <Modal.Footer>
        <button className="btn btn-light" disabled={busy} onClick={onClose}>
          Cancel
        </button>
        <button
          className="btn btn-primary"
          disabled={!selected || busy}
          onClick={() => selected && onSelect(selected, selected.isContainer ? mode : "PlayAll")}
        >
          {busy ? "Working…" : "Use selection"}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
