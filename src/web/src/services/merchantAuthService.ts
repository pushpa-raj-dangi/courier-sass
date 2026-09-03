import { getUpstreamApiUrl } from "@/lib/env";

export interface MerchantAuthTokens {
  accessToken: string;
  refreshToken: string;
  merchantUserId: string;
  merchantId: string;
  organizationId: string;
}

export interface MerchantMeResponse {
  merchantUserId: string;
  merchantId: string;
  organizationId: string;
  merchantDisplayName: string;
  email: string;
  fullName: string;
  role: "Owner" | "Staff";
  permissions: string[];
  kycStatus: "Pending" | "Approved" | "Rejected";
}

export const merchantAuthService = {
  async login(email: string, password: string): Promise<MerchantAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/merchant/auth/login`, {
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
      merchantUserId: data.merchantUserId,
      merchantId: data.merchantId,
      organizationId: data.organizationId,
    };
  },

  async refresh(refreshToken: string): Promise<MerchantAuthTokens> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/merchant/auth/refresh`, {
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
      merchantUserId: data.merchantUserId,
      merchantId: data.merchantId,
      organizationId: data.organizationId,
    };
  },

  async me(accessToken: string): Promise<MerchantMeResponse> {
    const response = await fetch(`${getUpstreamApiUrl()}/api/v1/merchant/auth/me`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: "no-store",
    });
    if (!response.ok) {
      throw new Error("ME_FAILED");
    }
    return response.json();
  },
};
