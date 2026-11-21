import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { GraphQLClient, GraphQLContext } from "@shane32/graphql";
import App from "./App";
import "./styles.scss";

const client = new GraphQLClient({
  url: "/api/graphql",
  sendDocumentIdAsQuery: true,
  defaultFetchPolicy: "no-cache",
  transformRequest: async (request) => ({
    ...request,
    credentials: "same-origin",
    headers: { ...request.headers, "X-Sonos-Request": "1" },
  }),
});
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <GraphQLContext value={{ client }}>
      <App />
    </GraphQLContext>
  </StrictMode>,
);
