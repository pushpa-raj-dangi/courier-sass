using System.Text.Json.Serialization;

namespace Courier.Billing.Domain;

// No global JsonStringEnumConverter is registered for the API (same recurring gap as
// ShipmentStatus/BranchType/LocalLevelType elsewhere in this codebase) — without this attribute,
// PayInvoiceRequest.Gateway (bound from the frontend's JSON body as "Esewa"/"Khalti") fails to
// deserialize and every payment attempt 400s.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentGateway { Esewa = 0, Khalti = 1 }
