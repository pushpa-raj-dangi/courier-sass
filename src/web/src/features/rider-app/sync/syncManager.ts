import { listOutboxItems, removeOutboxItem, updateOutboxItem } from "./outboxDb";
import { isNonRetryableError, QueuedRequestError } from "./errors";
import { useOutboxStatusStore } from "./outboxStatusStore";
import { queryClient } from "@/lib/queryClient";
import type { OutboxItem } from "./types";

async function syncFetch<T>(url: string, init: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw new QueuedRequestError(response.status, text || `Request to ${url} failed with ${response.status}`);
  }
  return response.json();
}

async function replayAction(item: OutboxItem): Promise<void> {
  const { action, idempotencyKey } = item;
  const jsonHeaders = { "Content-Type": "application/json", "Idempotency-Key": idempotencyKey };

  switch (action.kind) {
    case "pickup-scan": {
      await syncFetch(`/api/riders/${action.riderId}/scan/pickup`, {
        method: "POST",
        headers: jsonHeaders,
        body: JSON.stringify({ awb: action.awb }),
      });
      return;
    }
    case "delivery-fail": {
      await syncFetch(`/api/riders/${action.riderId}/delivery-attempts/fail`, {
        method: "POST",
        headers: jsonHeaders,
        body: JSON.stringify(action.input),
      });
      return;
    }
    case "delivery-complete": {
      // Local vars, not repeated type-narrowing through the QueuedAction union — each starts
      // from whatever the action already carries (non-null on a retry that already uploaded one
      // of the two files) and is persisted back onto the stored item after each successful
      // upload, so a later retry never re-uploads a file it already has a key for.
      let signatureStorageKey = action.signatureStorageKey;
      let photoStorageKey = action.photoStorageKey;

      // pod-uploads and delivery-attempts/complete are three distinct backend requests that all
      // originate from one outbox item, but IdempotencyMiddleware.cs keys its replay store as
      // `{partition}:{idempotencyKey}` with no path/route component — so reusing the item's bare
      // idempotencyKey across all three would make the 2nd and 3rd calls collide with the 1st
      // (409 IDEMPOTENCY_KEY_REUSED, since each has a different body). Derive three distinct but
      // deterministic sub-keys from the same idempotencyKey instead: a retry of this item recomputes
      // the same three keys (so it still replays correctly against a call that already succeeded),
      // but the three calls within one attempt never collide with each other.
      if (action.signatureBlob && !signatureStorageKey) {
        const result = await syncFetch<{ storageKey: string }>(
          `/api/riders/${action.riderId}/pod-uploads?kind=Signature`,
          {
            method: "POST",
            headers: {
              "Content-Type": action.signatureBlob.type || "application/octet-stream",
              "Idempotency-Key": `${idempotencyKey}:signature`,
            },
            body: action.signatureBlob,
          }
        );
        signatureStorageKey = result.storageKey;
        await updateOutboxItem(item.id, { action: { ...action, signatureStorageKey } });
      }
      if (action.photoBlob && !photoStorageKey) {
        const result = await syncFetch<{ storageKey: string }>(
          `/api/riders/${action.riderId}/pod-uploads?kind=Photo`,
          {
            method: "POST",
            headers: {
              "Content-Type": action.photoBlob.type || "application/octet-stream",
              "Idempotency-Key": `${idempotencyKey}:photo`,
            },
            body: action.photoBlob,
          }
        );
        photoStorageKey = result.storageKey;
        await updateOutboxItem(item.id, { action: { ...action, signatureStorageKey, photoStorageKey } });
      }

      await syncFetch(`/api/riders/${action.riderId}/delivery-attempts/complete`, {
        method: "POST",
        headers: { ...jsonHeaders, "Idempotency-Key": `${idempotencyKey}:complete` },
        body: JSON.stringify({
          shipmentId: action.shipmentId,
          recipientName: action.recipientName,
          otpVerified: action.otpVerified,
          signatureStorageKey,
          photoStorageKey,
          latitude: action.latitude,
          longitude: action.longitude,
          collectedCodAmount: action.collectedCodAmount,
        }),
      });
      return;
    }
  }
}

// Reentrancy guard: startSyncManager() can trigger drainOutbox() from three independent,
// unsynchronized call sites (the "online" listener, the "visibilitychange" listener, and the
// 30s poll interval) plus an immediate call at mount. Without this guard, two overlapping
// passes could each read the same "pending" item via listOutboxItems() before either writes
// "syncing" back (outboxDb's updateOutboxItem is a non-atomic read-then-put, not a
// compare-and-swap), and then both call replayAction on it concurrently — e.g. two near-
// simultaneous POSTs to delivery-attempts/complete (COD collection) for the same shipment.
// A concurrent caller must await and receive the SAME result as the already-running pass
// rather than starting a second one.
let inFlightDrain: Promise<{ synced: string[]; stuck: string[] }> | null = null;

export function drainOutbox(): Promise<{ synced: string[]; stuck: string[] }> {
  if (inFlightDrain) {
    return inFlightDrain;
  }
  const run = runDrain().finally(() => {
    if (inFlightDrain === run) {
      inFlightDrain = null;
    }
  });
  inFlightDrain = run;
  return run;
}

async function runDrain(): Promise<{ synced: string[]; stuck: string[] }> {
  const items = await listOutboxItems();
  const synced: string[] = [];
  const stuck: string[] = [];
  const blockedShipmentIds = new Set<string>();

  for (const item of items) {
    const shipmentId = item.action.shipmentId;
    if (item.status === "stuck") {
      blockedShipmentIds.add(shipmentId);
      continue;
    }
    if (blockedShipmentIds.has(shipmentId)) {
      continue;
    }

    await updateOutboxItem(item.id, { status: "syncing" });
    try {
      await replayAction(item);
      await removeOutboxItem(item.id);
      synced.push(item.id);
    } catch (error) {
      if (isNonRetryableError(error)) {
        const message = error instanceof Error ? error.message : "Unknown error";
        await updateOutboxItem(item.id, { status: "stuck", lastError: message });
        stuck.push(item.id);
        blockedShipmentIds.add(shipmentId);
      } else {
        await updateOutboxItem(item.id, { status: "pending" });
        break; // likely offline again — stop hammering the rest of the queue
      }
    }
  }

  await useOutboxStatusStore.getState().refresh();
  useOutboxStatusStore.getState().recordSync();
  if (synced.length > 0) {
    // A queued task (pickup/delivery) that just synced to the server should disappear from
    // "Today's Tasks" without requiring a full page reload — the rider may have queued it
    // offline and only reconnected minutes later via the poll/online listener, well after
    // they've navigated away from the screen that originally triggered the mutation.
    void queryClient.invalidateQueries({ queryKey: ["riders", "me", "tasks", "today"] });
  }
  return { synced, stuck };
}

export function startSyncManager(): () => void {
  const handleOnline = () => { void drainOutbox(); };
  const handleVisibility = () => {
    if (document.visibilityState === "visible") void drainOutbox();
  };

  window.addEventListener("online", handleOnline);
  document.addEventListener("visibilitychange", handleVisibility);
  const pollId = window.setInterval(() => { void drainOutbox(); }, 30_000);
  void drainOutbox();

  return () => {
    window.removeEventListener("online", handleOnline);
    document.removeEventListener("visibilitychange", handleVisibility);
    window.clearInterval(pollId);
  };
}
