\# Nepal Domestic Courier SaaS — Claude Code Master Specification



\## 1. Product Vision



Build a production-grade, multi-tenant Nepal domestic courier and logistics SaaS platform.



The platform must support courier companies operating multiple branches, hubs, riders, merchants and delivery networks across Nepal.



The system is not merely an e-commerce order management application.



The primary domain is the physical lifecycle, custody, movement, delivery and financial settlement of parcels.



Core principles:



\* Multi-tenant SaaS

\* Multi-branch

\* Role-based access control

\* Permission-based authorization

\* Branch-level data isolation

\* Parcel lifecycle management

\* Scan-driven operations

\* Hub-to-hub transfers

\* Rider delivery operations

\* COD lifecycle and reconciliation

\* NDR and RTO management

\* Proof of delivery

\* Merchant portal

\* Public tracking

\* REST APIs

\* Webhooks

\* Auditability

\* Nepal-specific geography

\* Nepal-specific COD and pricing workflows



\---



\# 2. Technology Stack



\## Backend



\* ASP.NET Core

\* C#

\* PostgreSQL

\* Entity Framework Core

\* Redis

\* RabbitMQ

\* Hangfire

\* FluentValidation

\* Serilog

\* OpenTelemetry-ready architecture



\## Frontend



\* Next.js

\* TypeScript

\* Tailwind CSS

\* shadcn/ui

\* TanStack Query

\* Zustand only where client-side global state is genuinely required

\* React Hook Form

\* Zod



\## Infrastructure



\* Docker

\* Docker Compose

\* GitHub Actions

\* AWS/Azure deployable architecture

\* Object storage abstraction

\* Redis

\* RabbitMQ



Do not introduce microservices during the initial implementation.



Use a modular monolith.



\---



\# 3. Architectural Rules



Use Clean Architecture with modular domain boundaries.



Each module should follow:



```text

Module

├── Domain

├── Application

├── Infrastructure

└── Presentation

```



Avoid creating a giant shared business layer.



Do not use CQRS/Mediator merely for ceremony.



Use normal application services and commands/queries where appropriate.



Domain logic belongs inside domain models/services.



Infrastructure concerns must not leak into Domain.



Do not put business logic inside controllers.



Do not put business logic inside React components.



\---



\# 4. Tenant Architecture



Every tenant-owned entity must belong to an Organization/Tenant.



Core model:



```text

Organization

Branch

User

Role

Permission

UserBranch

RolePermission

```



Never rely on the frontend to enforce tenant isolation.



Every backend query must enforce tenant scope.



A user must never access another tenant's data even if they manipulate an ID manually.



Tenant authorization must happen server-side.



\---



\# 5. Organization Model



An Organization represents a courier company using the SaaS.



Example:



```text

Organization

├── Head Office

├── Kathmandu Hub

├── Lalitpur Branch

├── Bhaktapur Branch

├── Pokhara Hub

└── Butwal Branch

```



An organization can have unlimited branches depending on subscription limits.



Each branch should contain:



\* Id

\* OrganizationId

\* Code

\* Name

\* Type

\* Province

\* District

\* Municipality

\* Ward

\* Address

\* Latitude

\* Longitude

\* Phone

\* OperatingHours

\* ParentBranchId

\* IsActive

\* CreatedAt

\* UpdatedAt



Branch types:



\* HeadOffice

\* Hub

\* Branch

\* PickupPoint

\* Warehouse



\---



\# 6. RBAC



Authorization must be permission based.



Default roles:



\* SuperAdmin

\* OrganizationOwner

\* OrganizationAdmin

\* OperationsManager

\* BranchManager

\* Dispatcher

\* SortingStaff

\* CounterStaff

\* PickupRider

\* DeliveryRider

\* FinanceManager

\* FinanceStaff

\* CustomerSupport

\* Merchant

\* MerchantStaff

\* Auditor

\* HubStaff



Roles are collections of permissions.



Never write:



```csharp

if (user.Role == "Admin")

```



throughout the codebase.



Use permissions.



Examples:



```text

shipment.create

shipment.read

shipment.update

shipment.cancel

shipment.assign

shipment.scan

shipment.sort

shipment.transfer



delivery.assign

delivery.attempt

delivery.complete



cod.read

cod.collect

cod.reconcile

cod.settle



branch.read

branch.create

branch.update



merchant.read

merchant.create

merchant.update



report.read

report.export



user.read

user.create

user.update

```



\---



\# 7. Branch Access



Users may belong to one or multiple branches.



Use:



```text

UserBranch

```



Do not use only:



```text

User.BranchId

```



Branch access must support:



\* One branch

\* Multiple branches

\* All branches

\* Branch hierarchy

\* Temporary assignment



Authorization must consider both:



```text

Permission

\+

BranchScope

```



Example:



A Branch Manager may have:



```text

shipment.read

shipment.assign

shipment.scan

```



but only for:



```text

Kathmandu Branch

```



\---



\# 8. Nepal Geography



Create normalized geography:



```text

Province

District

LocalLevel

Ward

Area

DeliveryZone

```



Avoid storing important routing/pricing information as free text.



Geography will drive:



\* Branch assignment

\* Delivery coverage

\* Pricing

\* Zones

\* Rider assignment

\* Reporting



\---



\# 9. Shipment Domain



Shipment is the central aggregate.



Example:



```text

Shipment

├── ShipmentItems

├── Recipient

├── Sender

├── COD

├── Pricing

├── TrackingEvents

├── DeliveryAttempts

├── NDR

├── POD

└── CustodyHistory

```



Shipment must have:



\* Internal UUID

\* AWB

\* MerchantId

\* OriginBranchId

\* DestinationBranchId

\* CurrentBranchId

\* CurrentCustodianId

\* CurrentStatus

\* ServiceType

\* Weight

\* Dimensions

\* DeclaredValue

\* CODAmount

\* DeliveryCharge

\* InsuranceCharge

\* OtherCharges

\* TotalCharge

\* Recipient details

\* Sender details

\* CreatedAt

\* UpdatedAt



AWB is a business identifier.



Database primary key must remain an internal ID.



\---



\# 10. Shipment State Machine



Shipment statuses must be controlled through valid transitions.



Initial lifecycle:



```text

CREATED

↓

PICKUP\_ASSIGNED

↓

PICKED\_UP

↓

AT\_ORIGIN\_BRANCH

↓

SORTED

↓

IN\_TRANSIT

↓

AT\_DESTINATION\_BRANCH

↓

OUT\_FOR\_DELIVERY

↓

DELIVERED

```



Failure lifecycle:



```text

OUT\_FOR\_DELIVERY

↓

DELIVERY\_FAILED

↓

NDR\_CREATED

↓

REATTEMPT

↓

OUT\_FOR\_DELIVERY

```



Return lifecycle:



```text

DELIVERY\_FAILED

↓

RTO\_INITIATED

↓

RTO\_IN\_TRANSIT

↓

RTO\_AT\_ORIGIN

↓

RTO\_DELIVERED

```



Invalid state transitions must be rejected server-side.



Every valid transition must create an immutable tracking event.



\---



\# 11. Tracking Events



Create:



```text

ShipmentEvent

```



Every event should contain:



\* ShipmentId

\* EventType

\* PreviousStatus

\* NewStatus

\* BranchId

\* UserId

\* RiderId

\* Timestamp

\* Latitude

\* Longitude

\* Notes

\* Metadata



Tracking events must be append-only.



Never silently modify historical events.



\---



\# 12. Physical Custody



The system must always be able to answer:



> Who had this parcel?

> At which branch?

> At what time?

> What operation was performed?



Maintain:



```text

CurrentBranch

CurrentCustodian

CustodyHistory

```



A shipment must not be considered physically transferred merely because a status changed.



Physical transfers should use scan/manifest operations.



\---



\# 13. Barcode and Scanning



Scanning is a core operational feature.



Support:



\* AWB barcode

\* QR code

\* Single scan

\* Continuous scan

\* Bulk scan



Operations:



```text

Receive

Sort

Dispatch

Transfer

RiderPickup

RiderDelivery

RTO

```



Every scan must validate:



\* User permission

\* Branch access

\* Shipment state

\* Expected operation

\* Shipment custody



\---



\# 14. Branch Transfer



Create:



```text

Manifest

ManifestShipment

ShipmentTransfer

```



A manifest can contain many shipments.



Example:



```text

Manifest:

MNF-20260818-001



From:

Kathmandu Hub



To:

Pokhara Hub



Vehicle:

BA 2 KHA 1234



Driver:

Driver ID



Shipment Count:

120

```



Manifest lifecycle:



```text

DRAFT

↓

SEALED

↓

DISPATCHED

↓

IN\_TRANSIT

↓

RECEIVED

↓

CLOSED

```



Receiving a manifest must scan/verify shipments.



Missing shipments must be identified.



\---



\# 15. Merchant Domain



Merchant represents an e-commerce seller/customer of the courier.



Support:



\* Merchant

\* Merchant users

\* Merchant branches

\* Merchant addresses

\* KYC

\* Pricing plans

\* API keys

\* Webhooks

\* Shipment history

\* COD settlement

\* Invoices

\* Reports



Merchant users must have their own permissions.



\---



\# 16. Pricing Engine



Never hardcode courier charges.



Create:



```text

RateCard

RateCardRule

ServiceType

WeightSlab

Zone

ZoneRate

AdditionalCharge

MerchantRateCard

```



Support:



\* Standard

\* Express

\* Same Day

\* Heavy

\* Special service



Pricing may depend on:



```text

Origin

Destination

Zone

Weight

Service

COD

Insurance

Merchant

```



Pricing must be versioned.



Historical shipments must preserve the price calculated at booking time.



Changing today's rate must not change yesterday's shipment price.



\---



\# 17. Rider Domain



Create:



```text

Rider

Vehicle

RiderAssignment

DeliveryRun

DeliveryTask

```



Rider application must support:



\* Login

\* Assigned pickups

\* Assigned deliveries

\* Navigation

\* Customer calling

\* OTP

\* COD collection

\* Signature

\* Photo

\* GPS

\* Delivery attempt

\* NDR

\* Reattempt

\* RTO

\* Cash settlement



\---



\# 18. Proof of Delivery



Create:



```text

DeliveryProof

```



Support:



\* OTP

\* Signature

\* Photo

\* GPS

\* Timestamp

\* Rider

\* Recipient name



POD should be immutable after successful delivery except through a controlled correction process with audit logging.



\---



\# 19. Delivery Attempts



Never represent failed delivery only as a shipment status.



Create:



```text

DeliveryAttempt

```



Fields:



\* ShipmentId

\* RiderId

\* AttemptNumber

\* AttemptedAt

\* Reason

\* Notes

\* Latitude

\* Longitude

\* CustomerContacted

\* NextAction



Reasons:



\* CustomerUnavailable

\* PhoneUnreachable

\* WrongAddress

\* IncompleteAddress

\* CustomerRefused

\* CustomerRequestedReschedule

\* CashUnavailable

\* DamagedParcel

\* Other



\---



\# 20. NDR



Create a dedicated NDR workflow.



```text

NDR

├── Reason

├── AssignedStaff

├── CustomerResponse

├── FollowUp

├── NextAction

└── Resolution

```



NDR statuses:



```text

OPEN

CONTACTING

CUSTOMER\_CONFIRMED

REATTEMPT\_SCHEDULED

RTO\_REQUESTED

RESOLVED

```



\---



\# 21. RTO



RTO must maintain linkage to the original shipment.



Create:



```text

ReturnShipment

```



or a controlled return lifecycle.



Track:



\* Original shipment

\* Return AWB

\* RTO reason

\* Origin

\* Destination

\* Current branch

\* Status

\* Received date

\* Merchant confirmation



\---



\# 22. COD



COD is a financial subsystem.



Never treat COD as a simple boolean.



Lifecycle:



```text

COD\_EXPECTED

↓

COD\_COLLECTED

↓

RIDER\_DEPOSITED

↓

BRANCH\_RECONCILED

↓

FINANCE\_VERIFIED

↓

MERCHANT\_SETTLEMENT\_CREATED

↓

SETTLED

```



Entities:



```text

CODCollection

RiderCashSettlement

BranchCashSettlement

MerchantSettlement

MerchantSettlementItem

FinancialTransaction

```



Example:



```text

COD = 2500

Courier Fee = 150



Merchant Payable = 2350

```



All calculations must be traceable.



\---



\# 23. Financial Reconciliation



The system must identify:



\* Expected COD

\* Collected COD

\* Rider deposited COD

\* Branch received COD

\* Merchant payable COD

\* Courier fees

\* Adjustments

\* Refunds

\* Shortages

\* Overages



Never silently adjust financial amounts.



All adjustments require:



\* User

\* Reason

\* Timestamp

\* Before value

\* After value

\* Approval where appropriate



\---



\# 24. Public Tracking



Create:



```text

/track/{awb}

```



Public tracking should expose:



\* Shipment received

\* Picked up

\* In transit

\* Destination branch

\* Out for delivery

\* Delivered



Do not expose:



\* Internal staff information

\* Internal notes

\* Financial information

\* Internal branch security data

\* Sensitive customer information



\---



\# 25. Merchant API



Provide REST API.



Examples:



```http

POST /api/v1/shipments

GET /api/v1/shipments/{awb}

GET /api/v1/shipments

POST /api/v1/shipments/{id}/cancel

POST /api/v1/pickups

GET /api/v1/cod/settlements

```



Use API keys.



Implement:



\* Authentication

\* Authorization

\* Rate limiting

\* Idempotency

\* Request logging

\* API versioning

\* Error contracts



\---



\# 26. Webhooks



Events:



```text

shipment.created

shipment.picked\_up

shipment.in\_transit

shipment.out\_for\_delivery

shipment.delivered

shipment.delivery\_failed

shipment.ndr\_created

shipment.rto

cod.collected

cod.settled

```



Webhook delivery must support:



\* Retry

\* Signature verification

\* Delivery logs

\* Failed webhook queue

\* Replay



\---



\# 27. Notifications



Use event-driven notifications.



Do not place SMS/email logic directly inside shipment business logic.



Architecture:



```text

Domain Event

↓

Event Handler

↓

Notification Queue

↓

Notification Worker

↓

SMS/Email/WhatsApp/Push

```



Templates must be configurable.



\---



\# 28. Audit



Every sensitive operation must be audited.



Audit:



\* Login

\* Logout

\* User changes

\* Role changes

\* Permission changes

\* Shipment edits

\* Status corrections

\* COD adjustments

\* Settlement approval

\* Branch changes

\* Rate changes

\* RTO changes



Audit record:



```text

User

Tenant

Action

Entity

EntityId

Before

After

IP

UserAgent

Timestamp

```



\---



\# 29. Dashboard



Organization dashboard:



```text

Today's Shipments

Created

Picked Up

In Transit

Out for Delivery

Delivered

NDR

RTO

```



Financial:



```text

COD Expected

COD Collected

COD Pending

COD Settled

Merchant Payable

```



Operational:



```text

Branch Performance

Rider Performance

Delivery Success Rate

NDR Rate

RTO Rate

Average Delivery Time

```



\---



\# 30. Branch Dashboard



Branch users must see branch-specific information according to their permissions.



Display:



\* Incoming shipments

\* Outgoing shipments

\* Pending sorting

\* Pending dispatch

\* Received manifests

\* Pending manifests

\* Riders

\* Deliveries

\* COD

\* Exceptions



\---



\# 31. Frontend Architecture



Next.js application should use modular feature folders.



Example:



```text

src/

├── app/

├── features/

│   ├── auth/

│   ├── shipments/

│   ├── merchants/

│   ├── branches/

│   ├── riders/

│   ├── cod/

│   ├── reports/

│   └── tracking/

├── components/

├── lib/

└── services/

```



Do not create one huge `components` folder containing all business logic.



\---



\# 32. API Client



Use a centralized API client.



Requirements:



\* Authentication

\* Error normalization

\* Request cancellation

\* Retry where appropriate

\* Query caching

\* Pagination

\* Filtering

\* Sorting



TanStack Query should manage server state.



Zustand should only manage genuinely global client state.



\---



\# 33. Security



Implement:



\* Password hashing

\* Refresh token rotation

\* Secure cookies where appropriate

\* JWT/session security

\* Tenant isolation

\* RBAC

\* Permission checks

\* Rate limiting

\* Input validation

\* SQL injection protection through EF Core

\* File upload validation

\* Audit logging

\* Sensitive-data masking

\* API key hashing

\* Webhook signatures



Never trust:



\* TenantId from frontend

\* BranchId from frontend

\* Role from frontend

\* Price from frontend

\* COD amount from frontend



All must be verified server-side.



\---



\# 34. Development Rules for Claude Code



Before implementing a feature:



1\. Inspect the existing architecture.

2\. Identify the correct module.

3\. Identify domain entities.

4\. Identify application services.

5\. Identify required database changes.

6\. Identify authorization requirements.

7\. Identify audit requirements.

8\. Identify events.

9\. Identify tests.

10\. Implement the smallest coherent change.



Do not rewrite unrelated code.



Do not introduce a new library without justification.



Do not duplicate existing utilities.



Do not bypass existing abstractions without documenting why.



\---



\# 35. Required Tests



Each module must include:



\### Unit tests



\* Domain rules

\* State transitions

\* Pricing

\* COD calculations

\* Permission rules



\### Integration tests



\* Database

\* Tenant isolation

\* Branch authorization

\* Shipment lifecycle

\* COD settlement



\### API tests



\* Authentication

\* Authorization

\* Validation

\* Error responses



Critical financial and shipment operations must have automated tests before being considered complete.



\---



\# 36. Implementation Order



Implement in this order:



\## Phase 0



\* Repository

\* Docker

\* PostgreSQL

\* Redis

\* RabbitMQ

\* API

\* Web

\* Logging

\* Error handling



\## Phase 1



\* Authentication

\* Organization

\* Users

\* Roles

\* Permissions

\* Tenant isolation

\* Audit



\## Phase 2



\* Branches

\* UserBranch

\* Branch authorization



\## Phase 3



\* Nepal geography

\* Zones

\* Coverage



\## Phase 4



\* Merchants

\* Merchant users

\* KYC

\* Merchant settings



\## Phase 5



\* Rate cards

\* Pricing engine



\## Phase 6



\* Shipments

\* AWB

\* Shipment items

\* Shipment lifecycle

\* Tracking events



\## Phase 7



\* Barcode scanning

\* Sorting

\* Manifests

\* Branch transfers



\## Phase 8



\* Riders

\* Assignments

\* Delivery

\* Delivery attempts

\* POD



\## Phase 9



\* NDR

\* Reattempt

\* RTO



\## Phase 10



\* COD

\* Rider settlements

\* Branch reconciliation

\* Merchant settlements



\## Phase 11



\* Notifications

\* SMS

\* Email

\* Push

\* Webhooks



\## Phase 12



\* Merchant portal

\* Public tracking

\* Merchant API



\## Phase 13



\* Reports

\* Analytics

\* Advanced dashboards



\## Phase 14



\* Route optimization

\* GPS

\* ETA

\* Smart dispatch

\* Partner courier integrations



\---



\# 37. Definition of Done



A feature is NOT complete merely because the UI exists.



A feature is complete only when:



\* Domain model exists

\* Database migration exists

\* Application logic exists

\* Authorization exists

\* Tenant isolation is enforced

\* Branch scope is enforced where required

\* Validation exists

\* Audit exists where required

\* API exists where required

\* Frontend exists where required

\* Loading states exist

\* Error states exist

\* Tests exist

\* Documentation is updated

\* Build succeeds

\* Tests pass

\* No unrelated regressions exist



\---



\# 38. Claude Code Workflow



For every requested feature:



\### Step 1



Analyze.



\### Step 2



Explain the proposed implementation.



\### Step 3



List affected modules/files.



\### Step 4



Identify database changes.



\### Step 5



Identify authorization/security implications.



\### Step 6



Implement.



\### Step 7



Run tests.



\### Step 8



Run build/type checks.



\### Step 9



Review for tenant/branch security.



\### Step 10



Summarize exactly what changed.



Do not claim a feature is complete if tests or builds are failing.



\---



\# 39. Critical Product Principle



The system must model the real physical movement of parcels.



Never build a system where an administrator can arbitrarily change:



```text

Created → Delivered

```



without validating the operational process.



Shipment status changes must represent real courier operations.



Scanning, custody, manifests, rider assignment, delivery attempts, POD and COD settlement must form a traceable chain.



The system should be able to answer:



> Where is the parcel?



> Who currently has it?



> Which branch owns it?



> What happened to it?



> When did it happen?



> Who performed the operation?



> Was COD collected?



> Has the merchant been settled?



> If delivery failed, why?



> What happens next?



This traceability is the foundation of the platform.



\---



\# 40. First Claude Code Task



Do NOT immediately implement the entire system.



First inspect the repository.



Then produce:



```text

docs/

├── PRODUCT-SPEC.md

├── ARCHITECTURE.md

├── DOMAIN-MODEL.md

├── RBAC.md

├── SHIPMENT-LIFECYCLE.md

├── COD-LIFECYCLE.md

├── BRANCH-OPERATIONS.md

├── API-SPEC.md

└── ROADMAP.md

```



The first task is architecture and domain analysis.



After the analysis is complete, implementation should begin with:



```text

Phase 0 → Phase 1 → Phase 2 → ...

```



Never skip the architecture review.



Never implement all modules in one pass.



