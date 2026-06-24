"use client";

import { useEffect, useState, useCallback } from "react";
import { getEvents } from "@/app/actions/events";
import { format } from "date-fns";
import { Loader2, Calendar } from "lucide-react";

export function Timeline({ refreshTrigger }: { refreshTrigger: number }) {
  const [events, setEvents] = useState<any[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);

  const fetchEvents = useCallback(async (cursor?: string) => {
    try {
      const data = await getEvents(cursor);
      if (data.success) {
        if (cursor) {
          setEvents((prev) => [...prev, ...data.data]);
        } else {
          setEvents(data.data);
        }
        setNextCursor(data.nextCursor);
      }
    } catch (err: any) {
      console.error(err);
    }
  }, []);

  useEffect(() => {
    setIsLoading(true);
    fetchEvents().finally(() => setIsLoading(false));
  }, [fetchEvents, refreshTrigger]);

  return (
    <div className="w-full max-w-2xl mx-auto">
      <h2 className="text-xl font-semibold mb-6 flex items-center gap-2 text-zinc-100">
        <Calendar className="w-5 h-5 text-indigo-400" />
        Timeline
      </h2>

      {isLoading ? (
        <div className="flex justify-center py-12">
          <Loader2 className="w-8 h-8 animate-spin text-zinc-500" />
        </div>
      ) : events.length === 0 ? (
        <div className="text-center py-12 text-zinc-500">
          No events found.
        </div>
      ) : (
        <div className="space-y-4">
          {events.map((evt) => (
            <div key={evt.id} className="bg-zinc-900 border border-zinc-800 p-5 rounded-2xl hover:border-zinc-700 transition-colors">
              <div className="flex justify-between items-start mb-2">
                <h3 className="font-medium text-zinc-100">{evt.title}</h3>
                <span className="text-xs text-zinc-500">
                  {format(new Date(evt.occurredAt), "PPp")}
                </span>
              </div>
              <p className="text-sm text-zinc-400 whitespace-pre-wrap">{evt.content}</p>
              
              <div className="mt-4 flex flex-wrap gap-2">
                <span className="px-2 py-1 bg-zinc-800 text-xs rounded-md text-zinc-300">
                  {evt.type}
                </span>
                {evt.tags?.map((tag: string) => (
                  <span key={tag} className="px-2 py-1 bg-indigo-500/10 text-indigo-400 border border-indigo-500/20 text-xs rounded-md">
                    #{tag}
                  </span>
                ))}
              </div>
            </div>
          ))}

          {nextCursor && (
            <div className="pt-6 flex justify-center">
              <button
                onClick={async () => {
                  setIsLoadingMore(true);
                  await fetchEvents(nextCursor);
                  setIsLoadingMore(false);
                }}
                disabled={isLoadingMore}
                className="px-6 py-2 bg-zinc-800 hover:bg-zinc-700 text-sm font-medium rounded-xl text-zinc-300 transition-colors flex items-center gap-2"
              >
                {isLoadingMore && <Loader2 className="w-4 h-4 animate-spin" />}
                Load More
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
