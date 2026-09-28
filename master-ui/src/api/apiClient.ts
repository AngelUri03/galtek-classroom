export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;

  constructor(status: number, message: string, code?: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
  }
}

type ApiErrorBody = {
  code?: unknown;
  message?: unknown;
};

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    method: "GET",
    headers: {
      Accept: "application/json"
    },
    signal
  });

  if (!response.ok) {
    throw await apiErrorFromResponse(response);
  }

  return (await response.json()) as T;
}

export async function postJson<T>(path: string, body: object, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal
  });

  if (!response.ok) {
    throw await apiErrorFromResponse(response);
  }

  return (await response.json()) as T;
}

export async function putJson<T>(path: string, body: object, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    method: "PUT",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(body),
    signal
  });
  if (!response.ok) throw await apiErrorFromResponse(response);
  return (await response.json()) as T;
}

export async function deleteJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { method: "DELETE", headers: { Accept: "application/json" }, signal });
  if (!response.ok) throw await apiErrorFromResponse(response);
  return (await response.json()) as T;
}

export async function sendSecret<T>(
  path: string,
  method: "POST" | "PUT",
  secret: string,
  vaultSessionToken?: string,
  signal?: AbortSignal
): Promise<T> {
  const bytes = new TextEncoder().encode(secret);
  try {
    const headers: Record<string, string> = {
      Accept: "application/json",
      "Content-Type": "application/octet-stream"
    };
    if (vaultSessionToken) headers["X-Galtek-Vault-Session"] = vaultSessionToken;
    const response = await fetch(path, { method, headers, body: bytes, signal });
    if (!response.ok) throw await apiErrorFromResponse(response);
    return (await response.json()) as T;
  } finally {
    bytes.fill(0);
  }
}

export async function apiErrorFromResponse(response: Response): Promise<ApiError> {
  let body: ApiErrorBody | null = null;

  try {
    body = (await response.json()) as ApiErrorBody;
  } catch {
    body = null;
  }

  const code = typeof body?.code === "string" ? body.code : undefined;
  const message = typeof body?.message === "string" ? body.message : "No pudimos completar la solicitud.";
  return new ApiError(response.status, message, code);
}
