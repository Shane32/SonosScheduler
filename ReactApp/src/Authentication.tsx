import { useEffect, useState } from "react";
import App from "./App";
import { useSonosAuth } from "./auth";

export function Authentication() {
  const { config, manager, authenticated } = useSonosAuth();
  const [path, setPath] = useState(window.location.pathname);
  useEffect(() => {
    const navigate = () => setPath(window.location.pathname);
    window.addEventListener("popstate", navigate);
    return () => window.removeEventListener("popstate", navigate);
  }, []);
  if (manager && path === config.redirectUri) return <AuthRedirect />;
  if (manager && path === config.logoutRedirectUri) return <AuthRedirect logout />;
  if (config.requireSignIn && !authenticated)
    return (
      <main className="container py-5">
        <section className="card card-body mx-auto admin-login">
          <h1 className="h3">Sonos Scheduler</h1>
          <p>Sign in with your Microsoft work or school account to use this application.</p>
          <SignInButton />
        </section>
      </main>
    );
  return <App />;
}

function AuthRedirect({ logout = false }: { logout?: boolean }) {
  const { config, manager } = useSonosAuth();
  const [error, setError] = useState<string>();
  useEffect(() => {
    // The timer lets StrictMode cancel its discarded setup before consuming the callback.
    const timer = window.setTimeout(() => {
      if (logout) manager!.handleLogoutRedirect();
      else
        void manager!
          .handleRedirect()
          .then(() => {
            if (window.location.pathname === config.redirectUri) window.location.replace(import.meta.env.BASE_URL);
          })
          .catch((e: unknown) => setError(e instanceof Error ? e.message : "Sign-in failed."));
    }, 0);
    return () => window.clearTimeout(timer);
  }, [config.redirectUri, manager, logout]);
  return (
    <main className="container py-5">
      {error ? (
        <div className="card card-body">
          <h1 className="h4">Could not complete Microsoft sign-in</h1>
          <p role="alert">{error}</p>
          <SignInButton />
          <a className="mt-3" href={import.meta.env.BASE_URL}>
            Return to Sonos Scheduler
          </a>
        </div>
      ) : (
        <p role="status">{logout ? "Completing sign-out…" : "Completing Microsoft sign-in…"}</p>
      )}
    </main>
  );
}

export function SignInButton({ admin = false }: { admin?: boolean }) {
  const { config, manager } = useSonosAuth();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  if (!config.enabled || !manager)
    return <p className="text-body-secondary mb-0">Microsoft Entra authentication has not been configured on the server.</p>;
  return (
    <div>
      {error && (
        <p className="text-danger" role="alert">
          {error}
        </p>
      )}
      <button
        className="btn btn-primary"
        disabled={busy}
        onClick={() => {
          setError(undefined);
          setBusy(true);
          void manager.login(`${import.meta.env.BASE_URL}${admin ? "#admin" : ""}`).catch((e: unknown) => {
            setError(e instanceof Error ? e.message : "Could not start Microsoft sign-in.");
            setBusy(false);
          });
        }}
      >
        {busy ? "Opening Microsoft sign-in…" : "Sign in with Microsoft"}
      </button>
    </div>
  );
}

export function SignOutButton() {
  const { manager } = useSonosAuth();
  const [error, setError] = useState<string>();
  return (
    <div>
      <button
        className="btn btn-sm btn-outline-secondary"
        onClick={() =>
          void manager!.logout(import.meta.env.BASE_URL).catch((e: unknown) => {
            setError(e instanceof Error ? e.message : "Could not complete Microsoft sign-out.");
          })
        }
      >
        Sign out
      </button>
      {error && (
        <p className="text-danger small mb-0" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
