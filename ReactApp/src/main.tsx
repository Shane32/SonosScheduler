import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { GraphQLClient, GraphQLContext } from "@shane32/graphql";
import { AuthManager, AuthProvider } from "@shane32/msoauth";
import { Authentication } from "./Authentication";
import { AuthConfigurationContext } from "./auth";
import type { AuthConfiguration } from "./auth";
import "./styles.scss";

const root = createRoot(document.getElementById("root")!);
root.render(
  <p className="container py-5" role="status">
    Loading Sonos Scheduler…
  </p>,
);

async function start() {
  const response = await fetch(`${import.meta.env.BASE_URL}api/oauth/config`, { credentials: "same-origin", cache: "no-store" });
  if (!response.ok) throw new Error("Could not load sign-in configuration.");
  const config: AuthConfiguration = await response.json();
  class TemplateAuthManager extends AuthManager {
    protected override async getTokenEndpointUrl(grantType: string): Promise<string> {
      return config.tokenEndpoint ?? super.getTokenEndpointUrl(grantType);
    }
  }
  const manager = config.enabled
    ? new TemplateAuthManager({
        id: `sonos:${config.clientId}:${import.meta.env.BASE_URL}`,
        clientId: config.clientId,
        authority: config.authority,
        scopes: config.scopes,
        redirectUri: config.redirectUri,
        logoutRedirectUri: config.logoutRedirectUri,
        navigateCallback: (path) => {
          const destination = new URL(path, window.location.origin);
          const safePath =
            destination.origin === window.location.origin && destination.pathname.startsWith(import.meta.env.BASE_URL)
              ? `${destination.pathname}${destination.search}${destination.hash}`
              : import.meta.env.BASE_URL;
          window.history.replaceState({}, "", safePath);
          window.dispatchEvent(new PopStateEvent("popstate"));
        },
      })
    : undefined;
  async function getIdToken() {
    if (manager?.isAuthenticated()) {
      try {
        return await manager.getIdToken();
      } catch {
        manager.localLogout();
      }
    }
    return undefined;
  }
  const client = new GraphQLClient({
    url: `${import.meta.env.BASE_URL}api/graphql`,
    sendDocumentIdAsQuery: true,
    defaultFetchPolicy: "no-cache",
    transformRequest: async (request) => {
      const token = await getIdToken();
      return {
        ...request,
        credentials: "same-origin",
        headers: {
          ...request.headers,
          // eslint-disable-next-line @typescript-eslint/naming-convention
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
      };
    },
    generatePayload: async () => {
      const token = await getIdToken();
      // eslint-disable-next-line @typescript-eslint/naming-convention
      return token ? { Authorization: `Bearer ${token}` } : {};
    },
  });
  manager?.addEventListener("login", () => client.resetStore());
  manager?.addEventListener("logout", () => client.resetStore());
  const application = <Authentication />;
  root.render(
    <StrictMode>
      <GraphQLContext value={{ client }}>
        <AuthConfigurationContext value={config}>
          {manager ? <AuthProvider authManager={manager}>{application}</AuthProvider> : application}
        </AuthConfigurationContext>
      </GraphQLContext>
    </StrictMode>,
  );
}

void start().catch((e: unknown) =>
  root.render(
    <main className="container py-5">
      <h1 className="h3">Sonos Scheduler could not start</h1>
      <p role="alert">{e instanceof Error ? e.message : "An unexpected startup error occurred."}</p>
      <button className="btn btn-primary" onClick={() => window.location.reload()}>
        Retry
      </button>
    </main>,
  ),
);
