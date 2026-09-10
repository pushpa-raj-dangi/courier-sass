export class SessionExpiredError extends Error {
  constructor() {
    super("Session expired");
    this.name = "SessionExpiredError";
  }
}

export class TrialExpiredError extends Error {
  constructor() {
    super("Trial expired");
    this.name = "TrialExpiredError";
  }
}

export async function clientFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (response.status === 401) {
    throw new SessionExpiredError();
  }
  if (response.status === 402) {
    throw new TrialExpiredError();
  }
  if (!response.ok) {
    let bodyText: string | null = null;
    try {
      const body = await response.json();
      if (body !== null) {
        bodyText = JSON.stringify(body);
      }
    } catch {
      // body wasn't JSON — fall back to the generic message below
    }
    throw new Error(bodyText ?? `Request to ${path} failed with ${response.status}`);
  }
  return response.json();
}
