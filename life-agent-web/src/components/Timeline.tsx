"use client";

import { useEffect, useState, useCallback } from "react";
import { getEvents, updateEvent, deleteEvent } from "@/app/actions/events";
import { format } from "date-fns";
import { Loader2, Calendar, Trash2, Edit3, Save, X, Tag } from "lucide-react";

function getTypeText(type: string): string {
  switch(type) {
    case "cycling": return "骑行";
    case "cat": return "宠物猫";
    case "home": return "家务";
    case "life": return "生活日常";
    default: return "未分类";
  }
}

export function Timeline({ refreshTrigger }: { refreshTrigger: number }) {
  const [events, setEvents] = useState<any[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [selectedTag, setSelectedTag] = useState<string | null>(null);

  // 编辑态状态
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [editContent, setEditContent] = useState("");
  const [editTags, setEditTags] = useState("");
  const [editImportance, setEditImportance] = useState(3);
  const [editStructuredData, setEditStructuredData] = useState("");
  const [editType, setEditType] = useState("unknown");

  const fetchEvents = useCallback(async (cursor?: string, tag?: string | null) => {
    try {
      const data = await getEvents(cursor, tag || undefined);
      if (data.success) {
        if (cursor) {
          setEvents((prev) => [...prev, ...data.data]);
        } else {
          setEvents(data.data);
        }
        setNextCursor(data.nextCursor);
      }
    } catch (err: any) {
      console.error("Fetch events error:", err);
    }
  }, []);

  useEffect(() => {
    setIsLoading(true);
    fetchEvents(undefined, selectedTag).finally(() => setIsLoading(false));
  }, [fetchEvents, selectedTag, refreshTrigger]);

  const handleDelete = async (id: string) => {
    if (window.confirm("确定要删除此事件吗？删除后将无法恢复。")) {
      try {
        await deleteEvent(id);
        setEvents((prev) => prev.filter((e) => e.id !== id));
      } catch (err: any) {
        alert("删除失败: " + err.message);
      }
    }
  };

  const startEdit = (evt: any) => {
    setEditingId(evt.id);
    setEditTitle(evt.title);
    setEditContent(evt.content);
    setEditTags(evt.tags?.join(", ") || "");
    setEditImportance(evt.importance || 3);
    setEditStructuredData(JSON.stringify(evt.structuredData || {}, null, 2));
    setEditType(evt.type || "unknown");
  };

  const handleSave = async (id: string) => {
    try {
      const parsedTags = editTags
        .split(",")
        .map((t) => t.trim())
        .filter((t) => t.length > 0);

      let parsedStructuredData = {};
      try {
        parsedStructuredData = JSON.parse(editStructuredData);
      } catch (e) {
        alert("结构化数据 (Structured Data) 格式不合法，请输入正确的 JSON");
        return;
      }

      const payload = {
        title: editTitle,
        content: editContent,
        tags: parsedTags,
        importance: editImportance,
        structuredData: parsedStructuredData,
        type: editType,
      };

      await updateEvent(id, payload);

      setEvents((prev) =>
        prev.map((e) =>
          e.id === id
            ? {
                ...e,
                title: editTitle,
                content: editContent,
                tags: parsedTags,
                importance: editImportance,
                structuredData: parsedStructuredData,
                type: editType,
              }
            : e
        )
      );
      setEditingId(null);
    } catch (err: any) {
      alert("保存失败: " + err.message);
    }
  };

  // 提取当前所有事件的去重标签
  const allTags = Array.from(
    new Set(events.flatMap((e) => e.tags || []))
  ) as string[];

  // 保证正在过滤的 tag 始终在列表里，即使 events 过滤后变少了
  if (selectedTag && !allTags.includes(selectedTag)) {
    allTags.push(selectedTag);
  }

  return (
    <div className="w-full max-w-2xl mx-auto">
      <div className="flex justify-between items-center mb-6">
        <h2 className="text-xl font-semibold flex items-center gap-2 text-zinc-100">
          <Calendar className="w-5 h-5 text-indigo-400" />
          生活记录
        </h2>
      </div>

      {/* 标签过滤栏 */}
      <div className="mb-6 bg-zinc-900/50 border border-zinc-800 p-4 rounded-2xl">
        <div className="text-xs text-zinc-500 mb-2 font-medium flex items-center gap-1">
          <Tag className="w-3.5 h-3.5 text-zinc-400" />
          按标签筛选
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => setSelectedTag(null)}
            className={`px-3 py-1 text-xs font-medium rounded-full border transition-all ${
              selectedTag === null
                ? "bg-indigo-600 text-zinc-100 border-indigo-500 shadow-md shadow-indigo-500/20"
                : "bg-zinc-800 text-zinc-400 border-zinc-700 hover:border-zinc-600 hover:text-zinc-200"
            }`}
          >
            全部
          </button>
          {allTags.map((tag) => (
            <button
              key={tag}
              onClick={() => setSelectedTag(tag)}
              className={`px-3 py-1 text-xs font-medium rounded-full border transition-all ${
                selectedTag === tag
                  ? "bg-indigo-600 text-zinc-100 border-indigo-500 shadow-md shadow-indigo-500/20"
                  : "bg-zinc-800 text-zinc-400 border-zinc-700 hover:border-zinc-600 hover:text-zinc-200"
              }`}
            >
              #{tag}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-12">
          <Loader2 className="w-8 h-8 animate-spin text-zinc-500" />
        </div>
      ) : events.length === 0 ? (
        <div className="text-center py-12 text-zinc-500 bg-zinc-900/20 border border-zinc-800 rounded-2xl">
          暂无生活记录。
        </div>
      ) : (
        <div className="space-y-4">
          {events.map((evt) => (
            <div
              key={evt.id}
              className="bg-zinc-900 border border-zinc-800 p-5 rounded-2xl hover:border-zinc-700 transition-colors relative group"
            >
              {editingId === evt.id ? (
                /* 编辑状态表单 */
                <div className="space-y-4">
                  <div>
                    <label className="block text-xs font-semibold text-zinc-400 mb-1">标题</label>
                    <input
                      type="text"
                      value={editTitle}
                      onChange={(e) => setEditTitle(e.target.value)}
                      className="w-full bg-zinc-950 border border-zinc-850 rounded-xl px-3 py-2 text-zinc-100 text-sm focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-zinc-400 mb-1">内容</label>
                    <textarea
                      value={editContent}
                      onChange={(e) => setEditContent(e.target.value)}
                      rows={3}
                      className="w-full bg-zinc-950 border border-zinc-850 rounded-xl px-3 py-2 text-zinc-100 text-sm focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                    />
                  </div>

                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-xs font-semibold text-zinc-400 mb-1">分类</label>
                      <select
                        value={editType}
                        onChange={(e) => setEditType(e.target.value)}
                        className="w-full bg-zinc-950 border border-zinc-850 rounded-xl px-3 py-2 text-zinc-100 text-sm focus:outline-none focus:border-indigo-500"
                      >
                        <option value="cycling">骑行</option>
                        <option value="cat">宠物猫</option>
                        <option value="home">家务</option>
                        <option value="life">生活日常</option>
                        <option value="unknown">未分类</option>
                      </select>
                    </div>

                    <div>
                      <label className="block text-xs font-semibold text-zinc-400 mb-1">重要度 (1-5)</label>
                      <select
                        value={editImportance}
                        onChange={(e) => setEditImportance(Number(e.target.value))}
                        className="w-full bg-zinc-950 border border-zinc-850 rounded-xl px-3 py-2 text-zinc-100 text-sm focus:outline-none focus:border-indigo-500"
                      >
                        <option value={1}>1 - 低</option>
                        <option value={2}>2 - 提示</option>
                        <option value={3}>3 - 中</option>
                        <option value={4}>4 - 高</option>
                        <option value={5}>5 - 紧急</option>
                      </select>
                    </div>
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-zinc-400 mb-1">标签 (逗号分隔)</label>
                    <input
                      type="text"
                      value={editTags}
                      onChange={(e) => setEditTags(e.target.value)}
                      className="w-full bg-zinc-950 border border-zinc-850 rounded-xl px-3 py-2 text-zinc-100 text-sm focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                      placeholder="例如：运动，户外"
                    />
                  </div>

                  <div>
                    <label className="block text-xs font-semibold text-zinc-400 mb-1">结构化数据 (JSON)</label>
                    <textarea
                      value={editStructuredData}
                      onChange={(e) => setEditStructuredData(e.target.value)}
                      rows={3}
                      className="w-full bg-zinc-950 border border-zinc-850 rounded-xl px-3 py-2 text-zinc-100 text-xs font-mono focus:outline-none focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                    />
                  </div>

                  <div className="flex justify-end gap-2 pt-2">
                    <button
                      onClick={() => setEditingId(null)}
                      className="px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-xs font-medium rounded-lg text-zinc-300 transition-colors flex items-center gap-1"
                    >
                      <X className="w-3.5 h-3.5" />
                      取消
                    </button>
                    <button
                      onClick={() => handleSave(evt.id)}
                      className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-xs font-medium rounded-lg text-zinc-100 transition-colors flex items-center gap-1"
                    >
                      <Save className="w-3.5 h-3.5" />
                      保存
                    </button>
                  </div>
                </div>
              ) : (
                /* 正常展示状态 */
                <>
                  <div className="flex justify-between items-start mb-2 pr-16">
                    <h3 className="font-medium text-zinc-100 text-base">{evt.title}</h3>
                    <span className="text-xs text-zinc-500 font-mono">
                      {format(new Date(evt.occurredAt), "PPp")}
                    </span>
                  </div>
                  <p className="text-sm text-zinc-400 whitespace-pre-wrap mb-4">{evt.content}</p>

                  <div className="flex flex-wrap gap-2">
                    <span className="px-2 py-0.5 bg-zinc-800 text-zinc-300 text-xs rounded-md font-medium border border-zinc-700">
                      {getTypeText(evt.type)}
                    </span>
                    <span className="px-2 py-0.5 bg-zinc-800/80 text-zinc-400 text-xs rounded-md font-medium border border-zinc-850">
                      重要度: {evt.importance}
                    </span>
                    {evt.tags?.map((tag: string) => (
                      <button
                        key={tag}
                        onClick={() => setSelectedTag(tag)}
                        className="px-2 py-0.5 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-400 border border-indigo-500/20 text-xs rounded-md transition-colors"
                      >
                        #{tag}
                      </button>
                    ))}
                  </div>

                  {/* 操作栏（卡片 Hover 时显式呈现，移动端常驻） */}
                  <div className="absolute right-4 top-4 flex gap-1 bg-zinc-900 border border-zinc-800 rounded-lg p-0.5 opacity-100 md:opacity-0 md:group-hover:opacity-100 transition-opacity">
                    <button
                      onClick={() => startEdit(evt)}
                      title="编辑记录"
                      className="p-1.5 text-zinc-400 hover:text-indigo-400 hover:bg-zinc-800 rounded-md transition-colors"
                    >
                      <Edit3 className="w-4 h-4" />
                    </button>
                    <button
                      onClick={() => handleDelete(evt.id)}
                      title="删除记录"
                      className="p-1.5 text-zinc-400 hover:text-red-400 hover:bg-zinc-800 rounded-md transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                </>
              )}
            </div>
          ))}

          {nextCursor && (
            <div className="pt-6 flex justify-center">
              <button
                onClick={async () => {
                  setIsLoadingMore(true);
                  await fetchEvents(nextCursor, selectedTag);
                  setIsLoadingMore(false);
                }}
                disabled={isLoadingMore}
                className="px-6 py-2 bg-zinc-800 hover:bg-zinc-700 text-sm font-medium rounded-xl text-zinc-300 transition-colors flex items-center gap-2 shadow-lg border border-zinc-700"
              >
                {isLoadingMore && <Loader2 className="w-4 h-4 animate-spin" />}
                加载更多
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
