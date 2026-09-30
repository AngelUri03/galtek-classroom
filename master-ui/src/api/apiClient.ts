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

export async function getJsonWithVaultSession<T>(
  path: string,
  vaultSessionToken: string,
  signal?: AbortSignal
): Promise<T> {
  const response = await fetch(path, {
    method: "GET",
    headers: { Accept: "application/json", "X-Galtek-Vault-Session": vaultSessionToken },
    signal
  });
  if (!response.ok) throw await apiErrorFromResponse(response);
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

export async function postJsonAuthorized<T>(
  path: string,
  body: object,
  sensitiveAuthorization: string,
  signal?: AbortSignal
): Promise<T> {
  const response = await fetch(path, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      "X-Galtek-Sensitive-Authorization": sensitiveAuthorization
    },
    body: JSON.stringify(body),
    signal
  });
  if (!response.ok) throw await apiErrorFromResponse(response);
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

export async function deleteWithVaultSession<T>(path: string, vaultSessionToken: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    method: "DELETE",
    headers: { Accept: "application/json", "X-Galtek-Vault-Session": vaultSessionToken },
    signal
  });
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

export async function authorizeSensitiveAction<T>(
  path: string,
  masterPassword: string,
  headers: Record<string, string> = {},
  signal?: AbortSignal
): Promise<T> {
  const bytes = new TextEncoder().encode(masterPassword);
  try {
    const response = await fetch(path, {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/octet-stream", ...headers },
      body: bytes,
      signal
    });
    if (!response.ok) throw await apiErrorFromResponse(response);
    return (await response.json()) as T;
  } finally {
    bytes.fill(0);
  }
}

export async function revealSensitiveSecret(
  path: string,
  sensitiveAuthorization: string,
  signal?: AbortSignal
): Promise<string> {
  const response = await fetch(path, {
    method: "POST",
    headers: {
      Accept: "application/octet-stream",
      "X-Galtek-Sensitive-Authorization": sensitiveAuthorization
    },
    signal,
    cache: "no-store"
  });
  if (!response.ok) throw await apiErrorFromResponse(response);
  const contentType = response.headers.get("Content-Type")?.split(";", 1)[0]?.trim().toLowerCase();
  if (contentType !== "application/octet-stream") {
    throw new ApiError(502, "El servidor devolvió una respuesta de contraseña no válida.", "INVALID_SECRET_RESPONSE");
  }
  const bytes = new Uint8Array(await response.arrayBuffer());
  try {
    if (bytes.length === 0) {
      throw new ApiError(502, "El servidor devolvió una respuesta de contraseña vacía.", "INVALID_SECRET_RESPONSE");
    }
    return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  } catch (error) {
    if (error instanceof ApiError) throw error;
    throw new ApiError(502, "El servidor devolvió una contraseña con codificación no válida.", "INVALID_SECRET_RESPONSE");
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
