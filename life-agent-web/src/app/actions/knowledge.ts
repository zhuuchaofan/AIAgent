"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export async function getDocuments() {
  try {
    const token = await getToken();
    if (!token) return { success: false, message: "未授权，请重新登录" };

    const res = await fetch(`${API_BASE}/api/v1/documents`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
      cache: "no-store",
    });

    const data = await res.json();
    if (!res.ok) {
      return { success: false, message: data.message || "获取文档列表失败" };
    }
    return data;
  } catch (err: unknown) {
    const errMsg = err instanceof Error ? err.message : String(err);
    return { success: false, message: errMsg || "连接服务器发生异常" };
  }
}

export async function uploadDocument(formData: FormData) {
  try {
    const token = await getToken();
    if (!token) return { success: false, message: "未授权，请重新登录" };

    const res = await fetch(`${API_BASE}/api/v1/documents`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        // 注意：千万不要手动设置 Content-Type，FormData 会由 fetch 自动配置 boundary
      },
      body: formData,
    });

    const data = await res.json();
    if (!res.ok) {
      return { success: false, message: data.message || "上传失败" };
    }
    return data;
  } catch (err: unknown) {
    const errMsg = err instanceof Error ? err.message : String(err);
    return { success: false, message: errMsg || "上传文件过程中连接服务器失败" };
  }
}

export async function deleteDocument(documentId: string) {
  try {
    const token = await getToken();
    if (!token) return { success: false, message: "未授权，请重新登录" };

    const res = await fetch(`${API_BASE}/api/v1/documents/${documentId}`, {
      method: "DELETE",
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });

    const data = await res.json();
    if (!res.ok) {
      return { success: false, message: data.message || "删除文档失败" };
    }
    return data;
  } catch (err: unknown) {
    const errMsg = err instanceof Error ? err.message : String(err);
    return { success: false, message: errMsg || "删除文档过程中连接服务器失败" };
  }
}

export async function sendRagMessage(
  conversationId: string,
  message: string,
  documentIds?: string[],
  clientTimeZone?: string
) {
  try {
    const token = await getToken();
    if (!token) return { success: false, message: "未授权，请重新登录" };

    const res = await fetch(`${API_BASE}/api/v1/chat/rag`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({
        conversationId,
        message,
        documentIds,
        clientTimeZone: clientTimeZone || "Asia/Shanghai",
      }),
    });

    const data = await res.json();
    if (!res.ok) {
      return { success: false, message: data.message || "知识库问答失败" };
    }
    return data;
  } catch (err: unknown) {
    const errMsg = err instanceof Error ? err.message : String(err);
    return { success: false, message: errMsg || "连接问答服务异常" };
  }
}

export async function getRagChatHistory(conversationId: string) {
  try {
    const token = await getToken();
    if (!token) return { success: false, message: "未授权，请重新登录" };

    const res = await fetch(`${API_BASE}/api/v1/chat/rag/${conversationId}/messages`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
      cache: "no-store",
    });

    if (res.status === 404) {
      return { success: true, data: [] };
    }

    const data = await res.json();
    if (!res.ok) {
      return { success: false, message: data.message || "拉取历史会话失败" };
    }
    return data;
  } catch (err: unknown) {
    const errMsg = err instanceof Error ? err.message : String(err);
    return { success: false, message: errMsg || "拉取历史消息连接服务器失败" };
  }
}
