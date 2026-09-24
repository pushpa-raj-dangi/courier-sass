using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Courier.International.Application.Carriers;

// How a provider-call exception maps to the API: open circuit -> 503 CARRIER_UNAVAILABLE; the
// carrier refusing, erroring or timing out -> 502 CARRIER_BOOKING_FAILED. Anything else is a bug
// and propagates (500).
public static class CarrierCallFailures
{
    public static bool IsCarrierUnavailable(Exception ex) => ex is BrokenCircuitException;

    public static bool IsCarrierFailure(Exception ex) =>
        ex is CarrierBookingException or HttpRequestException or TimeoutRejectedException or TaskCanceledException;
}
