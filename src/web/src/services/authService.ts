import { getUpstreamApiUrl } from "@/lib/env";

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  userId: string;
  organizationId: string;
}

export interface MeBranchGrant {
  branchId: string;
  branchName: string;
  expiresAt: string | null;
}

export interface MeResponse {
  userId: string;
  organizationId: string;
  organizationName: string;
  email: string;
  fullName: string;
  permissions: string[];
  hasAllBranchesScope: boolean;
  branches: MeBranchGrant[];
}

export interface RegisterOrganizationInput {
  organizationName: string;
  organizationSlug: string;
  ownerFullName: string;
  ownerEmail: string;
  ownerPassword: string;
}

export const authService = {
  async register(input: RegisterOrganizationInput): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(input),
      cache: "no-store",
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      throw new Error(body?.error?.message ?? "REGISTRATION_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },

  async login(email: string, password: string): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("LOGIN_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },

  async acceptInvite(token: string, password: string): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/accept-invite`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token, password }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("ACCEPT_INVITE_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },

  async refresh(refreshToken: string): Promise<AuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("REFRESH_FAILED");
    }
    const data = await response.json();
    return {
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      userId: data.userId,
      organizationId: data.organizationId,
    };
  },

  async me(accessToken: string): Promise<MeResponse> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("ME_FAILED");
    }
    return response.json();
  },
};
