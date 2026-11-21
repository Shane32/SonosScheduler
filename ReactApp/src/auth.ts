import { createContext, use } from "react";
import { AuthContext } from "@shane32/msoauth";

export interface AuthConfiguration {
  enabled: boolean;
  clientId: string;
  authority: string;
  scopes: string;
  redirectUri: string;
  logoutRedirectUri: string;
  requireSignIn: boolean;
}

export const AuthConfigurationContext = createContext<AuthConfiguration | null>(null);

export function useSonosAuth() {
  const config = use(AuthConfigurationContext);
  const auth = use(AuthContext);
  if (!config) throw new Error("Authentication configuration is unavailable.");
  return { config, manager: auth?.authManager, authenticated: auth?.authManager.isAuthenticated() ?? false };
}
