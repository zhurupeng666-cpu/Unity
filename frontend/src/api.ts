export type Priority = "low" | "medium" | "high";
export type Status = "open" | "in_progress" | "resolved";
export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: Priority;
  status: Status;
  position: { x: number; y: number; z: number };
  createdAt: string;
  updatedAt: string;
}

async function request<T>(url: string, options: RequestInit = {}): Promise<T> {
  try {
    const response = await fetch(url, {
      ...options,
      signal: AbortSignal.timeout(10000),
    });
    if (!response.ok) {
      const data = await response.json().catch(() => null);
      throw new Error(
        data?.error?.message ||
          `请求失败（HTTP ${response.status}），请确认后端服务正常。`,
      );
    }
    return (await response.json()) as T;
  } catch (error) {
    if (
      error instanceof TypeError ||
      (error instanceof DOMException &&
        ["TimeoutError", "AbortError"].includes(error.name))
    ) {
      throw new Error("无法连接后端服务，请检查网络与 Go 服务后重试。");
    }
    throw error;
  }
}

export const getIssues = () => request<Issue[]>("/api/issues");
export const updateStatus = (id: string, status: Status) =>
  request<Issue>(`/api/issues/${encodeURIComponent(id)}/status`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status }),
  });
