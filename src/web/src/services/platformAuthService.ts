import { getUpstreamApiUrl } from "@/lib/env";

export interface PlatformAuthTokens {
  accessToken: string;
  refreshToken: string;
  userId: string;
}

export const platformAuthService = {
  async login(email: string, password: string): Promise<PlatformAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/platform/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("LOGIN_FAILED");
    }
    const data = await response.json();
    return { accessToken: data.accessToken, refreshToken: data.refreshToken, userId: data.userId };
  },

  async refresh(refreshToken: string): Promise<PlatformAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/platform/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("REFRESH_FAILED");
    }
    const data = await response.json();
    return { accessToken: data.accessToken, refreshToken: data.refreshToken, userId: data.userId };
  },
};
