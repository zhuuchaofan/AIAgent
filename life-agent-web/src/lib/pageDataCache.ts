"use client";

const DEFAULT_TTL_MS = 60_000;

interface PageDataCacheEntry<T> {
  value: T;
  updatedAt: number;
}

const pageDataCache = new Map<string, PageDataCacheEntry<unknown>>();
const inFlightLoads = new Map<string, Promise<unknown>>();
const invalidatedAtByPrefix = new Map<string, number>();

export interface PageDataCacheHit<T> {
  value: T;
  isFresh: boolean;
  ageMs: number;
}

export class PageDataCacheInvalidatedError extends Error {
  constructor() {
    super("Page data cache load was invalidated before completion.");
    this.name = "PageDataCacheInvalidatedError";
  }
}

export function pageDataCacheKey(scope: string, userId: string, ...parts: Array<string | number | boolean | null | undefined>) {
  return [scope, userId, ...parts.map(part => String(part ?? ""))].join(":");
}

export function readPageDataCache<T>(key: string, ttlMs = DEFAULT_TTL_MS): PageDataCacheHit<T> | null {
  const entry = pageDataCache.get(key) as PageDataCacheEntry<T> | undefined;
  if (!entry) return null;

  const ageMs = Date.now() - entry.updatedAt;
  return {
    value: entry.value,
    isFresh: ageMs <= ttlMs,
    ageMs,
  };
}

export function writePageDataCache<T>(key: string, value: T) {
  pageDataCache.set(key, {
    value,
    updatedAt: Date.now(),
  });
}

export async function loadPageDataCache<T>(key: string, loader: () => Promise<T>): Promise<T> {
  const currentLoad = inFlightLoads.get(key) as Promise<T> | undefined;
  if (currentLoad) return currentLoad;

  const loadStartedAt = Date.now();
  const nextLoad = loader()
    .then(value => {
      if (wasInvalidatedAfter(key, loadStartedAt)) {
        throw new PageDataCacheInvalidatedError();
      }

      writePageDataCache(key, value);
      return value;
    })
    .finally(() => {
      inFlightLoads.delete(key);
    });

  inFlightLoads.set(key, nextLoad);
  return nextLoad;
}

export function invalidatePageDataCache(prefixes: string | string[]) {
  const normalizedPrefixes = Array.isArray(prefixes) ? prefixes : [prefixes];
  const invalidatedAt = Date.now();

  normalizedPrefixes.forEach(prefix => {
    invalidatedAtByPrefix.set(prefix, invalidatedAt);
  });

  for (const key of pageDataCache.keys()) {
    if (normalizedPrefixes.some(prefix => key.startsWith(prefix))) {
      pageDataCache.delete(key);
    }
  }

  for (const key of inFlightLoads.keys()) {
    if (normalizedPrefixes.some(prefix => key.startsWith(prefix))) {
      inFlightLoads.delete(key);
    }
  }
}

function wasInvalidatedAfter(key: string, timestamp: number) {
  for (const [prefix, invalidatedAt] of invalidatedAtByPrefix.entries()) {
    if (key.startsWith(prefix) && invalidatedAt >= timestamp) {
      return true;
    }
  }

  return false;
}
