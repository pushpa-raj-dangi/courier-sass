import { openDB, type DBSchema, type IDBPDatabase } from "idb";
import { generateIdempotencyKey } from "@/lib/api/idempotencyKey";
import type { OutboxItem, QueuedAction } from "./types";

interface OutboxDBSchema extends DBSchema {
  outbox: {
    key: string;
    value: OutboxItem;
    indexes: { "by-createdAt": number };
  };
}

const DB_NAME = "courier-rider-outbox";
const DB_VERSION = 1;

let dbPromise: Promise<IDBPDatabase<OutboxDBSchema>> | null = null;

function getDb(): Promise<IDBPDatabase<OutboxDBSchema>> {
  if (!dbPromise) {
    dbPromise = openDB<OutboxDBSchema>(DB_NAME, DB_VERSION, {
      upgrade(db) {
        const store = db.createObjectStore("outbox", { keyPath: "id" });
        store.createIndex("by-createdAt", "createdAt");
      },
    });
  }
  return dbPromise;
}

export async function addOutboxItem(action: QueuedAction): Promise<OutboxItem> {
  const item: OutboxItem = {
    id: crypto.randomUUID(),
    action,
    idempotencyKey: generateIdempotencyKey(),
    createdAt: Date.now(),
    status: "pending",
  };
  const db = await getDb();
  await db.put("outbox", item);
  return item;
}

export async function listOutboxItems(): Promise<OutboxItem[]> {
  const db = await getDb();
  return db.getAllFromIndex("outbox", "by-createdAt");
}

export async function updateOutboxItem(id: string, patch: Partial<OutboxItem>): Promise<void> {
  const db = await getDb();
  const existing = await db.get("outbox", id);
  if (!existing) return;
  await db.put("outbox", { ...existing, ...patch });
}

export async function removeOutboxItem(id: string): Promise<void> {
  const db = await getDb();
  await db.delete("outbox", id);
}

// Test-only: forces the next getDb() call to reopen a fresh connection against
// fake-indexeddb's per-test-reset global, so state doesn't leak between test cases.
//
// The prior connection (if any) must be closed before deleteDatabase is requested —
// otherwise the open connection blocks the delete indefinitely, and every subsequent
// openDB() call queues behind that stuck delete and hangs.
export function resetOutboxDbForTests(): void {
  const prev = dbPromise;
  dbPromise = null;
  if (prev) {
    prev.then((db) => db.close()).catch(() => {});
  }
  indexedDB.deleteDatabase(DB_NAME);
}
