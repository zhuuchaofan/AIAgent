"use client";

import { useEffect, useState, useCallback } from "react";
import { getEvents, updateEvent, deleteEvent, type LifeEvent } from "@/app/actions/events";
import { Loader2, Calendar, Trash2, Edit3, Save, X, Tag } from "lucide-react";
import { useAuth } from "@/providers/AuthProvider";
import { formatShortChineseDateTime } from "@/lib/dateFormat";
import { getLifeEventDisplayRecord } from "@/lib/lifeEventDisplay";
import { TimelineSkeleton } from "@/components/LoadingSkeletons";

function getTypeText(type: string): string {
  switch(type) {
    case "cycling": return "骑行";
    case "cat": return "宠物猫";
    case "home": return "家务";
    case "life": return "生活日常";
    default: return "未分类";
  }
}

export function Timeline({
  refreshTrigger,
  onRecentEventsChange,
}: {
  refreshTrigger: number;
  onRecentEventsChange?: (events: LifeEvent[]) => void;
}) {
  const { user } = useAuth();
  const [events, setEvents] = useState<LifeEvent[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [selectedTag, setSelectedTag] = useState<string | null>(null);
  const [showAllRecords, setShowAllRecords] = useState(false);

  // 编辑态状态
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editTitle, setEditTitle] = useState("");
  const [editContent, setEditContent] = useState("");

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
    } catch {
      setEvents([]);
      setNextCursor(null);
    }
  }, []);

  useEffect(() => {
    if (!user) return;
    const load = async () => {
      await Promise.resolve();
      setIsLoading(true);
      try {
        await fetchEvents(undefined, selectedTag);
      } finally {
        setIsLoading(false);
      }
    };
    load();
  }, [fetchEvents, selectedTag, refreshTrigger, user]);

  const handleDelete = async (id: string) => {
    if (window.confirm("确定要删除此事件吗？删除后将无法恢复。")) {
      try {
        await deleteEvent(id);
        setEvents((prev) => prev.filter((e) => e.id !== id));
      } catch (err) {
        const errMsg = err instanceof Error ? err.message : String(err);
        alert("删除失败: " + errMsg);
      }
    }
  };

  const startEdit = (evt: LifeEvent) => {
    const displayRecord = getLifeEventDisplayRecord(evt);
    setEditingId(evt.id);
    setEditTitle(displayRecord.title);
    setEditContent(displayRecord.content || displayRecord.title);
  };

  const handleSave = async (evt: LifeEvent) => {
    try {
      const payload = {
        title: editTitle,
        content: editContent,
        tags: evt.tags ?? [],
        importance: evt.importance,
        structuredData: evt.structuredData ?? {},
        type: evt.type || "unknown",
      };

      await updateEvent(evt.id, payload);

      setEvents((prev) =>
        prev.map((e) =>
          e.id === evt.id
            ? {
                ...e,
                title: editTitle,
                content: editContent,
              }
            : e
        )
      );
      setEditingId(null);
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : String(err);
      alert("保存失败: " + errMsg);
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

  const visibleEvents = showAllRecords ? events : events.slice(0, 3);
  const hasMoreLocalRecords = events.length > 3 && !showAllRecords;

  useEffect(() => {
    onRecentEventsChange?.(events.slice(0, 3));
  }, [events, onRecentEventsChange]);

  return (
    <div className="w-full max-w-2xl mx-auto">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-xl font-semibold flex items-center gap-2 text-zinc-100">
          <Calendar className="w-5 h-5 text-indigo-400" />
          最近生活记录
        </h2>
      </div>

      {/* 标签过滤栏 */}
      <details className="mb-4 bg-zinc-900/40 border border-zinc-800 p-3 rounded-2xl">
        <summary className="cursor-pointer select-none text-xs text-zinc-400 font-medium flex items-center gap-1">
          <Tag className="w-3.5 h-3.5 text-zinc-400" />
          标签筛选
        </summary>
        <div className="flex flex-wrap gap-2 mt-3">
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
      </details>

      {isLoading ? (
        <TimelineSkeleton />
      ) : events.length === 0 ? (
        <div className="text-center py-12 text-zinc-500 bg-zinc-900/20 border border-zinc-800 rounded-2xl">
          暂无生活记录。
        </div>
      ) : (
        <div className="space-y-4">
          {visibleEvents.map((evt) => (
            <div
              key={evt.id}
              className="bg-zinc-900 border border-zinc-800 p-4 rounded-2xl hover:border-zinc-700 transition-colors relative group"
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

                  <div className="flex justify-end gap-2 pt-2">
                    <button
                      onClick={() => setEditingId(null)}
                      className="px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-xs font-medium rounded-lg text-zinc-300 transition-colors flex items-center gap-1"
                    >
                      <X className="w-3.5 h-3.5" />
                      取消
                    </button>
                    <button
                      onClick={() => handleSave(evt)}
                      className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-xs font-medium rounded-lg text-zinc-100 transition-colors flex items-center gap-1"
                    >
                      <Save className="w-3.5 h-3.5" />
                      保存
                    </button>
                  </div>
                </div>
              ) : (
                /* 正常展示状态 */
                (() => {
                  const displayRecord = getLifeEventDisplayRecord(evt);

                  return (
                <>
                  <div className="flex items-start justify-between gap-3 mb-2">
                    <div className="min-w-0 flex-1">
                      <h3 className="font-medium text-zinc-100 text-base leading-snug break-words">{displayRecord.title}</h3>
                      <span className="block text-xs text-zinc-500 font-mono mt-1">
                        {formatShortChineseDateTime(evt.occurredAt)}
                      </span>
                    </div>
                    <div className="flex gap-1 bg-zinc-900 border border-zinc-800 rounded-lg p-0.5 shrink-0">
                      <button
                        onClick={() => startEdit(evt)}
                        title="编辑记录"
                        aria-label="编辑记录"
                        className="p-1.5 text-zinc-400 hover:text-indigo-400 hover:bg-zinc-800 rounded-md transition-colors"
                      >
                        <Edit3 className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => handleDelete(evt.id)}
                        title="删除记录"
                        aria-label="删除记录"
                        className="p-1.5 text-zinc-400 hover:text-red-400 hover:bg-zinc-800 rounded-md transition-colors"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                  {displayRecord.content && (
                    <p className="text-sm text-zinc-400 whitespace-pre-wrap mb-4 leading-relaxed break-words">{displayRecord.content}</p>
                  )}

                  <div className="flex flex-wrap gap-2">
                    <span className="px-2 py-0.5 bg-zinc-800/70 text-zinc-400 text-xs rounded-md font-medium border border-zinc-800">
                      {getTypeText(evt.type)}
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
                </>
                  );
                })()
              )}
            </div>
          ))}

          {hasMoreLocalRecords && (
            <div className="pt-2 flex justify-center">
              <button
                type="button"
                onClick={() => setShowAllRecords(true)}
                className="px-5 py-2 bg-zinc-800 hover:bg-zinc-700 text-sm font-medium rounded-xl text-zinc-300 transition-colors border border-zinc-700"
              >
                查看全部生活记录
              </button>
            </div>
          )}

          {showAllRecords && nextCursor && (
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
