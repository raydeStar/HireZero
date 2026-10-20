import { useEffect, useState } from "react";

// Relative times for work (UX round 8): "just now", "5 min ago", "3 h ago", then the day.
export function ago(iso: string | undefined, now: number): string {
  if (!iso) return "";
  const minutes = Math.floor((now - Date.parse(iso)) / 60_000);
  if (Number.isNaN(minutes)) return "";
  if (minutes < 1) return "just now";
  if (minutes < 60) return minutes + " min ago";
  if (minutes < 24 * 60) return Math.floor(minutes / 60) + " h ago";
  return new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

// How long something has been going: "under a minute", "4 min", "1 h 20 min".
export function elapsed(iso: string | undefined, now: number): string {
  if (!iso) return "";
  const minutes = Math.floor((now - Date.parse(iso)) / 60_000);
  if (Number.isNaN(minutes)) return "";
  if (minutes < 1) return "under a minute";
  if (minutes < 60) return minutes + " min";
  const hours = Math.floor(minutes / 60);
  return hours + " h" + (minutes % 60 ? " " + (minutes % 60) + " min" : "");
}

// Whether an instant falls on the viewer's current day.
export function isToday(iso: string | undefined, now: number): boolean {
  return !!iso && new Date(iso).toDateString() === new Date(now).toDateString();
}

// The current time, refreshed on an interval so relative times stay live.
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}
