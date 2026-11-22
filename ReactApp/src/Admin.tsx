import { useState } from "react";
import { useMutation, useQuery } from "@shane32/graphql";
import { Modal } from "react-bootstrap";
import * as Q from "./App.queries.g";
import type { NetworkGroup, RunAction } from "./shared";
import { dateTime } from "./shared";
import { useSonosAuth } from "./auth";
import { SignInButton, SignOutButton } from "./Authentication";

export function Admin({
  groups,
  isAdmin,
  name,
  busy,
  run,
  onSessionChange,
}: {
  groups: NetworkGroup[];
  isAdmin: boolean;
  name: string;
  busy: boolean;
  run: RunAction;
  onSessionChange: () => void;
}) {
  const [section, setSection] = useState("networks");
  const { config, authenticated } = useSonosAuth();
  const [editing, setEditing] = useState<NetworkGroup | "new">();
  const [objectId, setObjectId] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [after, setAfter] = useState<string>();
  const admins = useQuery(Q.SonosAdminsDocument, { skip: !isAdmin || section !== "admins" });
  const logs = useQuery(Q.SonosLogsDocument, { variables: { after }, skip: !isAdmin || section !== "logs" });
  const [addGroup] = useMutation(Q.SonosAddGroupDocument);
  const [updateGroup] = useMutation(Q.SonosUpdateGroupDocument);
  const [deleteGroup] = useMutation(Q.SonosDeleteGroupDocument);
  const [addAdmin] = useMutation(Q.SonosAddAdminDocument);
  const [deleteAdmin] = useMutation(Q.SonosDeleteAdminDocument);
  if (!isAdmin)
    return (
      <section className="card admin-login">
        <div className="card-body p-4 p-lg-5">
          <span className="eyebrow">ADMINISTRATOR ACCESS</span>
          <h2 className="h4">Manage your listening spaces.</h2>
          <p className="text-body-secondary">
            {authenticated
              ? "Your Microsoft account does not have administrator access. Ask an existing administrator to add your Entra user object ID."
              : "Sign in with your Microsoft work or school account to manage networks, administrators, and logs."}
            {!config.requireSignIn && " Everyone on your LAN can use speakers and schedules."}
          </p>
          {authenticated ? <SignOutButton /> : <SignInButton admin />}
          <p className="small text-body-secondary mt-4 mb-0">
            The first administrator is configured on the server using an Entra object ID. Microsoft handles sign-in.
          </p>
        </div>
      </section>
    );
  return (
    <section>
      <div className="section-toolbar">
        <nav className="d-flex flex-wrap gap-2" aria-label="Administration">
          {[
            ["networks", "Network groups"],
            ["admins", "Administrators"],
            ["logs", "Activity log"],
          ].map(([id, label]) => (
            <button
              key={id}
              className={`btn btn-sm ${id === section ? "btn-primary" : "btn-light"}`}
              aria-pressed={id === section}
              onClick={() => setSection(id)}
            >
              {label}
            </button>
          ))}
        </nav>
        <div className="d-flex align-items-center gap-3">
          <span className="small text-body-secondary">{name}</span>
          <SignOutButton />
        </div>
      </div>
      {section === "networks" && (
        <div className="card">
          <div className="card-body section-toolbar mb-0">
            <div>
              <h2 className="h5">Network groups</h2>
              <p className="small text-body-secondary mb-0">Each group is a subnet to scan. Discovery runs hourly.</p>
            </div>
            <button className="btn btn-primary" onClick={() => setEditing("new")}>
              + Add network
            </button>
          </div>
          <div className="table-responsive">
            <table className="table align-middle mb-0">
              <thead>
                <tr>
                  <th>Group</th>
                  <th>Subnet</th>
                  <th>Last discovery</th>
                  <th>
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {groups.map((g) => (
                  <tr key={g.id}>
                    <th scope="row">
                      {g.name}
                      <small className="d-block fw-normal text-body-secondary">{g.speakers.length} speakers</small>
                    </th>
                    <td>
                      <code>{g.subnet}</code>
                      {g.seedAddress && <small className="d-block">Seed: {g.seedAddress}</small>}
                    </td>
                    <td>{dateTime(g.lastRefreshedAt)}</td>
                    <td>
                      <div className="d-flex gap-2">
                        <button className="btn btn-sm btn-outline-secondary" onClick={() => setEditing(g)}>
                          Edit
                        </button>
                        <button
                          className="btn btn-sm btn-outline-danger"
                          disabled={busy}
                          onClick={() => {
                            if (confirm(`Delete network “${g.name}”? Delete its schedules first.`))
                              void run(() => deleteGroup({ variables: { id: g.id } }), "Network deleted.");
                          }}
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
                {!groups.length && (
                  <tr>
                    <td colSpan={4} className="p-4 text-body-secondary">
                      Add a network to get started.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
      {section === "admins" && (
        <div className="row g-4">
          <div className="col-12 col-lg-7">
            <div className="card">
              <div className="card-body">
                <h2 className="h5">Administrators</h2>
                {admins.error && <div className="alert alert-danger">{admins.error.message}</div>}
                {admins.loading && <p role="status">Loading administrators…</p>}
                {admins.data?.admins.items.map((a) => (
                  <div className="admin-row" key={a.id}>
                    <div className="text-break">
                      <strong>{a.displayName || "Name available after sign-in"}</strong>
                      <details className="small text-body-secondary mt-1">
                        <summary>Account details</summary>
                        <div>Object ID: {a.objectId}</div>
                        <div>Tenant ID: {a.tenantId}</div>
                      </details>
                    </div>
                    <button
                      className="btn btn-sm btn-outline-danger"
                      disabled={busy || admins.data!.admins.items.length < 2}
                      onClick={() => {
                        if (confirm(`Remove administrator ${a.displayName || "with this account"}?`))
                          void run(() => deleteAdmin({ variables: { id: a.id } }), "Administrator removed.").then((ok) => {
                            if (ok) {
                              admins.refetch();
                              onSessionChange();
                            }
                          });
                      }}
                    >
                      Remove
                    </button>
                  </div>
                ))}
                <p className="small text-body-secondary mt-3 mb-0">At least one administrator must remain.</p>
              </div>
            </div>
          </div>
          <div className="col-12 col-lg-5">
            <form
              className="card card-body"
              onSubmit={(e) => {
                e.preventDefault();
                void run(
                  () => addAdmin({ variables: { objectId: objectId.trim(), displayName: displayName.trim() } }),
                  "Administrator added.",
                ).then((ok) => {
                  if (ok) {
                    setObjectId("");
                    setDisplayName("");
                    admins.refetch();
                  }
                });
              }}
            >
              <h2 className="h5 mb-4">Add administrator</h2>
              <label className="form-label" htmlFor="new-admin-name">
                Display name
              </label>
              <input
                id="new-admin-name"
                className="form-control"
                required
                maxLength={200}
                pattern=".*\S.*"
                autoComplete="name"
                aria-describedby="admin-name-help"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
              />
              <p id="admin-name-help" className="form-text mb-3">
                Updated from their Microsoft account when they sign in.
              </p>
              <label className="form-label" htmlFor="new-admin">
                Entra user object ID
              </label>
              <input
                id="new-admin"
                className="form-control mb-3"
                required
                maxLength={36}
                minLength={36}
                pattern="[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"
                placeholder="00000000-0000-0000-0000-000000000000"
                aria-describedby="admin-object-help"
                value={objectId}
                onChange={(e) => setObjectId(e.target.value)}
              />
              <p id="admin-object-help" className="small text-body-secondary mb-4">
                Copy the user's Object ID from Microsoft Entra ID → Users → the user's Overview. Use an account in this application's
                tenant. This immutable ID identifies the Microsoft account; no local sign-in credentials are created.
              </p>
              <button className="btn btn-primary align-self-start" disabled={busy}>
                Add administrator
              </button>
            </form>
          </div>
        </div>
      )}
      {section === "logs" && (
        <div className="card">
          <div className="card-body section-toolbar mb-0">
            <div>
              <h2 className="h5">Activity log</h2>
              <p className="small text-body-secondary mb-0">
                Schedule changes, executions, discovery, and unexpected errors. Retained for 30 days.
              </p>
            </div>
            <button className="btn btn-outline-secondary btn-sm" onClick={() => logs.refetch()}>
              Refresh
            </button>
          </div>
          {logs.error && <div className="alert alert-danger m-3">{logs.error.message}</div>}
          {logs.loading && (
            <p className="p-3" role="status">
              Loading logs…
            </p>
          )}
          <div className="table-responsive">
            <table className="table log-table mb-0">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Category</th>
                  <th>Activity</th>
                  <th>Actor</th>
                </tr>
              </thead>
              <tbody>
                {logs.data?.logs.items.map((log) => (
                  <tr key={log.id}>
                    <td className="text-nowrap">{dateTime(log.timestamp)}</td>
                    <td>
                      <span
                        className={`badge ${log.level === "Error" ? "text-bg-danger" : log.level === "Warning" ? "text-bg-warning" : "text-bg-light"}`}
                      >
                        {log.level}
                      </span>
                      <small className="d-block mt-1">{log.category}</small>
                    </td>
                    <td>
                      {log.message}
                      {log.details && (
                        <details className="mt-2">
                          <summary>Details</summary>
                          <pre className="log-details">{log.details}</pre>
                        </details>
                      )}
                    </td>
                    <td>{log.actor}</td>
                  </tr>
                ))}
                {logs.data?.logs.items.length === 0 && (
                  <tr>
                    <td colSpan={4} className="p-4 text-body-secondary">
                      No log entries.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          <div className="d-flex gap-2 p-3">
            <button className="btn btn-sm btn-outline-secondary" disabled={!after} onClick={() => setAfter(undefined)}>
              Newest entries
            </button>
            <button
              className="btn btn-sm btn-outline-secondary"
              disabled={!logs.data?.logs.pageInfo.hasNextPage || logs.loading}
              onClick={() => setAfter(logs.data?.logs.pageInfo.endCursor ?? undefined)}
            >
              Older entries
            </button>
          </div>
        </div>
      )}
      {editing && (
        <GroupEditor
          group={editing === "new" ? undefined : editing}
          busy={busy}
          onClose={() => setEditing(undefined)}
          onSave={(input) =>
            void run(
              () => (editing === "new" ? addGroup({ variables: { input } }) : updateGroup({ variables: { id: editing.id, input } })),
              "Network saved. Use Refresh speakers to discover rooms.",
            ).then((ok) => {
              if (ok) setEditing(undefined);
            })
          }
        />
      )}
    </section>
  );
}

function GroupEditor({
  group,
  busy,
  onClose,
  onSave,
}: {
  group?: NetworkGroup;
  busy: boolean;
  onClose: () => void;
  onSave: (input: { name: string; subnet: string; seedAddress: string | null }) => void;
}) {
  const [name, setName] = useState(group?.name ?? "");
  const [subnet, setSubnet] = useState(group?.subnet ?? "");
  const [seed, setSeed] = useState(group?.seedAddress ?? "");
  return (
    <Modal show centered onHide={onClose}>
      <Modal.Header closeButton>
        <Modal.Title as="h2" className="h5">
          {group ? "Edit network" : "Add network"}
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <form
          id="group-form"
          onSubmit={(e) => {
            e.preventDefault();
            onSave({ name, subnet, seedAddress: seed.trim() || null });
          }}
        >
          <label className="form-label" htmlFor="network-name">
            Name
          </label>
          <input
            id="network-name"
            className="form-control mb-3"
            required
            maxLength={100}
            placeholder="Home"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <label className="form-label" htmlFor="network-subnet">
            IPv4 subnet (CIDR)
          </label>
          <input
            id="network-subnet"
            className="form-control"
            required
            placeholder="192.168.1.0/24"
            value={subnet}
            onChange={(e) => setSubnet(e.target.value)}
          />
          <p className="form-text mb-3">Use /16 through /32 on a network reachable from this server.</p>
          <label className="form-label" htmlFor="network-seed">
            Known speaker IP (optional)
          </label>
          <input
            id="network-seed"
            className="form-control"
            placeholder="192.168.1.25"
            value={seed}
            onChange={(e) => setSeed(e.target.value)}
          />
          <p className="small text-body-secondary mt-3 mb-0">
            Discovery asks a known speaker for the other rooms. If none responds, it scans the subnet until it finds one.
          </p>
        </form>
      </Modal.Body>
      <Modal.Footer>
        <button className="btn btn-light" onClick={onClose}>
          Cancel
        </button>
        <button className="btn btn-primary" type="submit" form="group-form" disabled={busy}>
          Save network
        </button>
      </Modal.Footer>
    </Modal>
  );
}
