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

async function apiErrorFromResponse(response: Response): Promise<ApiError> {
  let body: ApiErrorBody | null = null;

  try {
    body = (await response.json()) as ApiErrorBody;
  } catch {
    body = null;
  }

  const code = typeof body?.code === "string" ? body.code : undefined;
  const message = typeof body?.message === "string" ? body.message : "No pudimos cargar el aula.";
  return new ApiError(response.status, message, code);
}
