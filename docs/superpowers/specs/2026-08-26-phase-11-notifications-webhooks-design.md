# Phase 11 — Notifications & Webhooks: Design Spec

**Status:** Approved in brainstorming session 2026-08-26 (section-by-section, in chat). This is the
first phase in this project to use the full architectural brainstorming path with a written spec —
every prior phase (1-10) used a bounded, in-chat-only brainstorm per their own plan documents. This
phase is scoped heavier because it is the first to touch RabbitMQ and Hangfire at all (confirmed via
`docs/CLARIFICATIONS.md` #4: "Hangfire is in the tech stack... but nothing uses it before Phase 11")
and introduces three new modules in one phase.

**Spec authority:** `CLAUDE.md` §26-27, `docs/ROADMAP.md` Phase 11 section, `docs/ARCHITECTURE.md`
"Domain events & reliability (outbox pattern)" / "Background jobs — Hangfire" sections,
`docs/DOMAIN-MODEL.md` "Notifications" / "Webhooks" sections, `docs/API-SPEC.md` §5 (Webhooks).

---

## 1. Goal

Turn the currently-inert outbox concept from `ARCHITECTURE.md` into a real, working pipeline:
a domain event becomes a durable `OutboxMessage` row in the same transaction as the business
change that caused it, a Hangfire job publishes it to RabbitMQ, and two independent consumers —
one for notifications, one for webhooks — each act on it. At least one notification channel
(email, via real SMTP) actually delivers; webhook delivery is signed, retried on failure with
backoff, and replayable on demand.

## 2. Scope for this phase

**In scope:**
- `Events` module: `OutboxMessage` entity + `IOutboxWriter` narrow interface (mirrors `Audit`'s
  `IAuditLogger` pattern — a small shared-write module every event-producing module references).
- `Notifications` module: `NotificationTemplate`, `NotificationLog`, a RabbitMQ consumer that
  resolves template + recipient + channel and dispatches. Real SMTP for `Email`; `SMS`/`WhatsApp`/
  `Push` write a `NotificationLog` row and log via Serilog only (no outbound call) — the channel
  abstraction (`INotificationChannel`) is real and pluggable, only the non-Email implementations
  are stubs.
- `Webhooks` module: `WebhookSubscription`, `WebhookDelivery`, a RabbitMQ consumer that fans out to
  matching subscriptions, signs and POSTs, records delivery attempts; a Hangfire recurring job
  retries `NextRetryAt`-due deliveries with exponential backoff up to a capped attempt count.
  Full CRUD/read API per `API-SPEC.md` §5, merchant-scoped.
- Four wired domain events (the "proving set" — each is one new `IOutboxWriter.WriteAsync(...)`
  call at an existing application-service call site, no new business logic):
  `shipment.created`, `shipment.delivered`, `shipment.ndr_created`, `cod.collected`.
- Hangfire wired generically (dashboard, PostgreSQL job storage — reusing the existing `courier`
  Postgres database, a separate schema) plus exactly two recurring jobs this pipeline needs: the
  outbox publisher and the webhook-delivery retry job.
- A dev-only SMTP mail-catcher container (`docker-compose.yml`) so real-SMTP delivery is verifiable
  in tests/dev without real-world credentials.

**Explicitly out of scope (deferred, not forgotten):**
- `NotificationTemplate` CRUD API — templates are seeded (one default per wired event/channel pair)
  this phase; a config API is a natural follow-up once the pipeline is proven.
- The other 6 events in `CLAUDE.md` §26's catalog (`shipment.picked_up`, `shipment.in_transit`,
  `shipment.out_for_delivery`, `shipment.delivery_failed`, `shipment.rto`, `cod.settled`) — they
  remain valid *subscribable* webhook event types (a merchant can register for any of the 10) but
  nothing publishes them yet. Same "documented but not yet triggered" shape as Phase 9's `AutoRTO`.
- Hangfire jobs unrelated to this pipeline: scheduled/recurring merchant-settlement generation
  (explicitly named in Phase 10's `ROADMAP.md` update as blocked on this phase's infrastructure —
  now unblocked, but it's a `Cod`-module business feature, not part of this phase), NDR SLA/
  follow-up reminders, expired `UserBranch` grant cleanup. All three are real, valid future work,
  just not part of proving the notification/webhook pipeline itself.
- SMS/WhatsApp/Push *real* delivery — the abstraction is real, the providers are stubs.

## 3. Architecture

```text
Application service (Shipments/Riders/Ndr/Cod)
    │  (same SaveChangesAsync as the business change)
    ▼
IOutboxWriter.WriteAsync(orgId, eventType, payload)  →  OutboxMessage row (Events module)
    │
    ▼  (Hangfire recurring job: outbox publisher, polls unpublished rows)
RabbitMQ topic exchange (routing key = event type)
    │                                   │
    ▼ (Notifications consumer)          ▼ (Webhooks consumer)
resolve template+recipient+channel      match WebhookSubscriptions for org+event
dispatch (real SMTP / stub log)         create WebhookDelivery row, sign+POST
write NotificationLog                        │
                                              ▼ (on failure)
                                         Hangfire recurring job: webhook retry
                                         (exponential backoff, capped attempts)
```

Everything above runs in the single API process — `BackgroundService`-hosted consumers plus
Hangfire recurring jobs, not separate deployables. This satisfies CLAUDE.md's "modular monolith,
no microservices" rule while still using RabbitMQ as a genuine internal broker, which is what
`ARCHITECTURE.md`'s already-committed text specifies ("no distributed sagas... the outbox pattern
above solves the one reliability gap... that a monolith with an external broker actually has").

**Why a real RabbitMQ hop instead of one Hangfire job doing everything in-process:** the simpler
alternative was considered and rejected — it would deviate from `ARCHITECTURE.md`'s committed
design without a strong enough reason (CLAUDE.md §34: don't bypass existing abstractions without
documenting why), and would mean redoing this exact wiring work later when a real pub/sub need
arises anyway.

## 4. Module boundaries and entities

Three new modules, each a thin vertical slice — Domain/Application/Infrastructure/Presentation
per `CLAUDE.md` §3, following the same shape every prior phase's module used:

### `Events` (new, infrastructure-shaped like `Audit`)

```text
OutboxMessage { Id, OrganizationId, EventType, Payload, CreatedAt, PublishedAt (nullable), Attempts }
```

`IOutboxWriter.WriteAsync(Guid organizationId, string eventType, object payload,
CancellationToken)` — the only public surface. No project reference to any other module's domain
types; `payload` is a plain object serialized to JSON at write time, matching the "no cached copy,
snapshot what you need" principle already established in Phases 6/10. `Shipments`, `Riders`,
`Ndr`, `Cod` each gain a project reference to `Events` (one-directional, `Events` references
nothing else — same "narrowest possible dependency" shape as `Audit`).

### `Notifications` (new)

```text
NotificationTemplate { Id, OrganizationId, EventType, Channel (SMS|Email|WhatsApp|Push), Body, IsActive }

NotificationLog { Id, OrganizationId, EventType, Channel, RecipientAddress, Status, SentAt, ProviderResponse }
```

`INotificationChannel { Task SendAsync(string recipientAddress, string body, CancellationToken) }`
— one implementation per channel. `EmailNotificationChannel` uses real SMTP (`System.Net.Mail` or
`MailKit`, TBD at implementation time — plan will decide). `SmsNotificationChannel`/
`WhatsAppNotificationChannel`/`PushNotificationChannel` write straight to `NotificationLog` with
`Status = "Logged"` and a Serilog entry, no outbound call. The consumer resolves the template
(active template for the org/event/channel, or a seeded org-independent default if none), resolves
the recipient (`MerchantId` on the payload → `Merchant.ContactEmail` via `IMerchantLookupService`),
renders the body, dispatches, and always writes a `NotificationLog` row (success or failure).

### `Webhooks` (new)

```text
WebhookSubscription { Id, MerchantId, OrganizationId, TargetUrl, Secret (encrypted at rest — see
                        §5 below), Events[], IsActive }

WebhookDelivery { Id, WebhookSubscriptionId, EventType, Payload, Attempt, ResponseStatus,
                    ResponseSnippet, DeliveredAt (nullable), NextRetryAt (nullable) }
```

API (`API-SPEC.md` §5, merchant-authenticated, implicitly scoped to the calling merchant — same
pattern as every merchant-track endpoint since Phase 6):

```http
GET    /api/v1/webhooks
POST   /api/v1/webhooks
DELETE /api/v1/webhooks/{id}
GET    /api/v1/webhooks/{id}/deliveries
POST   /api/v1/webhooks/deliveries/{id}/replay
```

Consumer: for each outbox message, finds every `IsActive` `WebhookSubscription` in the org whose
`Events[]` contains the event type, creates one `WebhookDelivery` row per subscription, computes
`X-Webhook-Signature: sha256=<HMAC>` over the raw JSON payload using the subscription's decrypted
secret, POSTs to `TargetUrl`. Non-2xx or timeout leaves `DeliveredAt` null and sets `NextRetryAt`;
the retry job picks it up.

## 5. Resolved ambiguity: `WebhookSubscription.Secret` storage

`DOMAIN-MODEL.md` states the secret is "hashed at rest," but HMAC-signing an outgoing payload
requires the plaintext secret at send time — a one-way hash cannot produce a valid signature.

**Resolution:** the secret is **encrypted at rest** (reversible), not hashed. Shown in full exactly
once, at creation (`POST /api/v1/webhooks`'s response), the same UX convention as an API key.
Decrypted only inside the webhook-signing code path inside the `Webhooks` consumer — never
returned by any `GET` endpoint after creation. "Hashed" in `DOMAIN-MODEL.md` was imprecise phrasing
for "not stored in plaintext"; this will be corrected in `DOMAIN-MODEL.md` and recorded as a new
`CLARIFICATIONS.md` entry during the implementation phase's docs-sync task, matching this
project's established pattern for resolving doc/implementation contradictions (e.g. Phase 5's
`RateCardRule` resolution, Phase 10's `BranchCashSettlement.RiderCashSettlementIds[]` resolution).

## 6. Event wiring (the "proving set")

| Event | Call site | New line |
|---|---|---|
| `shipment.created` | `Shipments.Application.ShipmentService.CreateShipmentAsync` | `IOutboxWriter.WriteAsync` after the shipment is persisted |
| `shipment.delivered` | `Riders.Application.RiderService.CompleteDeliveryAsync` (via the shipment-completion path) | after the shipment moves to `Delivered` |
| `shipment.ndr_created` | `Ndr.Application.NdrCreationService` | after the `NDR` row is created |
| `cod.collected` | `Cod.Application.CodService.MarkCollectedAsync` | after `CODCollection.Status` moves to `CodCollected` |

**Recipient resolution:** `Shipment` carries only `RecipientPhone`/`SenderPhone` (no email field —
matches Nepal courier reality, customers aren't on file with email). `Merchant.ContactEmail`
(nullable) exists. All four wired events therefore notify the **merchant** by real email — not
the shipment's own recipient/sender, who would need SMS (stub-only this phase). If
`Merchant.ContactEmail` is null, the notification is logged as `Status = "NoRecipient"` in
`NotificationLog` rather than thrown as an error — a missing contact email is an expected,
non-exceptional org data state, not a pipeline failure.

## 7. Reliability

- **Same-transaction outbox write:** `IOutboxWriter.WriteAsync` is called inside the same
  `DbContext`/`SaveChangesAsync` as the triggering business change (matching `IAuditLogger`'s
  existing call shape) — guarantees no "state changed, no event" gap.
- **Retry policy (resolved ambiguity, no threshold specified anywhere in the docs):** 5 delivery
  attempts per `WebhookDelivery`, exponential backoff starting at 1 minute and doubling (1m, 2m,
  4m, 8m, 16m), then the delivery is left in a terminal failed state (`Attempt = 5`, `DeliveredAt`
  still null, `NextRetryAt` cleared) — visible via `GET /webhooks/{id}/deliveries` and replayable
  on demand via `POST /webhooks/deliveries/{id}/replay`, which resets the attempt counter and
  re-attempts immediately. This mirrors Phase 9's `AutoRTO` N-resolution pattern — a concrete
  number chosen where the source docs left one unspecified, recorded in `CLARIFICATIONS.md`.
- **Idempotent consumption:** RabbitMQ consumer acknowledges a message only after successfully
  processing it (creating the `WebhookDelivery` rows / writing the `NotificationLog` row) — a
  crash mid-processing redelivers, and processing is safe to repeat (creating `WebhookDelivery`
  rows twice for the same outbox message is the one real risk; the consumer will check for an
  existing delivery batch by `(WebhookSubscriptionId, EventType, a payload-derived idempotency
  key)` before creating new rows — exact mechanism decided at plan time).

## 8. Testing

- Unit tests (EF InMemory, no RabbitMQ) for `Events`, `Notifications`, `Webhooks` application
  services — template resolution, recipient resolution, signature computation, retry/backoff math.
- `Api.Tests` integration tests against the real `courier-rabbitmq-1` container (same pattern as
  Postgres since Phase 0) proving: an event fired through a real application-service call reaches
  a real RabbitMQ queue; a real SMTP send lands in the dev mail-catcher; a forced-failure webhook
  target retries on backoff then appears failed in the delivery log, and a replay succeeds once the
  target is fixed. This is the ROADMAP's own stated exit criteria, proven end-to-end.
- A dev-only mail-catcher (e.g. `mailhog` or similar) added to `docker-compose.yml` alongside the
  existing `postgres`/`redis`/`rabbitmq` services, with its SMTP port used by both local dev and
  `Api.Tests`.

## 9. Tenant isolation & permissions

- `OutboxMessage`, `NotificationTemplate`, `NotificationLog`, `WebhookSubscription` all carry their
  own `OrganizationId` — direct query filters, same pattern as every prior phase.
  `WebhookDelivery` does not — isolation flows transitively through
  `WebhookSubscriptionId -> WebhookSubscription.OrganizationId`, matching the established
  `MerchantSettlementItem`/`ShipmentItem` convention.
- Webhook management endpoints are merchant-track only (a merchant manages their own webhooks);
  no new staff-facing permission keys are needed for this phase's scope — the outbox/notification
  pipeline has no staff-facing read surface yet (a `GET /notifications/log`-style admin view is a
  natural follow-up, not required by this phase's exit criteria).

## 10. Self-review

**Placeholder scan:** no TBD/TODO left unresolved — the two source-doc ambiguities found during
this brainstorm (secret storage, retry-attempt count) are both explicitly resolved above, matching
the project's established `CLARIFICATIONS.md` pattern for documenting such resolutions during the
implementation phase's docs-sync task.

**Internal consistency:** the architecture diagram (§3), module boundaries (§4), and event wiring
(§6) all agree — four call sites write to one `Events` module, two independent consumers read from
RabbitMQ, no module reads another's tables directly.

**Scope check:** focused enough for one implementation plan — three modules, but each is a thin
slice (2-4 entities each) and the wiring into existing modules is exactly four one-line additions,
consistent with Phase 10's own scale (which bundled 7 entities into one module across 15 tasks).

**Ambiguity check:** the "should Phase 11 also implement scheduled merchant settlements/NDR
reminders" question was explicitly narrowed to out-of-scope in §2, so a future reader won't assume
this phase's Hangfire wiring covers those.
