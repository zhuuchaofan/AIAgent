function SkeletonBlock({ className = "" }: { className?: string }) {
  return (
    <div className={`animate-pulse rounded-lg bg-zinc-800/60 ${className}`} />
  );
}

export function TimelineSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 3 }).map((_, index) => (
        <article key={index} className="rounded-2xl border border-zinc-800 bg-zinc-900/45 p-4">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1 space-y-3">
              <SkeletonBlock className="h-5 w-4/5 max-w-sm" />
              <SkeletonBlock className="h-3 w-28" />
              <div className="space-y-2 pt-1">
                <SkeletonBlock className="h-3 w-full" />
                <SkeletonBlock className="h-3 w-2/3" />
              </div>
              <SkeletonBlock className="h-6 w-20 rounded-md" />
            </div>
            <SkeletonBlock className="h-9 w-16 shrink-0 rounded-xl" />
          </div>
        </article>
      ))}
    </div>
  );
}

export function InsightSkeleton() {
  return (
    <div className="mt-3 space-y-2">
      <SkeletonBlock className="h-10 w-full rounded-xl" />
      <SkeletonBlock className="h-10 w-11/12 rounded-xl" />
      <SkeletonBlock className="h-10 w-4/5 rounded-xl" />
    </div>
  );
}

export function ReviewCardSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 3 }).map((_, index) => (
        <section key={index} className="rounded-2xl border border-zinc-800 bg-zinc-900/35 p-5">
          <div className="mb-4 flex items-center gap-2">
            <SkeletonBlock className="h-4 w-4 rounded-full" />
            <SkeletonBlock className="h-5 w-36" />
          </div>
          <div className="space-y-2">
            <SkeletonBlock className="h-3 w-full" />
            <SkeletonBlock className="h-3 w-11/12" />
            <SkeletonBlock className="h-3 w-2/3" />
          </div>
          <div className="mt-4 flex gap-2">
            <SkeletonBlock className="h-8 w-20 rounded-lg" />
            <SkeletonBlock className="h-8 w-24 rounded-lg" />
          </div>
        </section>
      ))}
    </div>
  );
}

export function MemoryListSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 3 }).map((_, index) => (
        <article key={index} className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4">
          <div className="mb-3 flex gap-2">
            <SkeletonBlock className="h-5 w-12 rounded-md" />
            <SkeletonBlock className="h-5 w-24 rounded-md" />
          </div>
          <div className="space-y-2">
            <SkeletonBlock className="h-4 w-full" />
            <SkeletonBlock className="h-4 w-3/4" />
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <SkeletonBlock className="h-7 w-24 rounded-md" />
            <SkeletonBlock className="h-7 w-24 rounded-md" />
          </div>
        </article>
      ))}
    </div>
  );
}

export function MemoryCandidateSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 3 }).map((_, index) => (
        <article key={index} className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4">
          <div className="mb-3 flex flex-wrap gap-2">
            <SkeletonBlock className="h-5 w-12 rounded-md" />
            <SkeletonBlock className="h-5 w-16 rounded-md" />
            <SkeletonBlock className="h-5 w-20 rounded-md" />
          </div>
          <SkeletonBlock className="h-5 w-4/5" />
          <div className="mt-3 space-y-2">
            <SkeletonBlock className="h-3 w-full" />
            <SkeletonBlock className="h-3 w-2/3" />
          </div>
          <div className="mt-4 flex gap-2">
            <SkeletonBlock className="h-9 w-20 rounded-lg" />
            <SkeletonBlock className="h-9 w-24 rounded-lg" />
          </div>
        </article>
      ))}
    </div>
  );
}

export function PageContentSkeleton() {
  return (
    <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5">
      <div className="space-y-3">
        <SkeletonBlock className="h-4 w-3/4" />
        <SkeletonBlock className="h-4 w-full" />
        <SkeletonBlock className="h-4 w-2/3" />
      </div>
    </div>
  );
}
