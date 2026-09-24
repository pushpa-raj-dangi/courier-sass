using Courier.International.Application;
using Courier.Shipments.Application;
using Microsoft.AspNetCore.Http;

namespace Courier.International.Presentation;

// One exception -> error-contract mapping for every I4 route (spec §7). Order matters:
// ShipmentInvalidTransitionException derives from ShipmentValidationException.
internal static class InternationalEndpointErrors
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InternationalShipmentValidationException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "INTERNATIONAL_SHIPMENT_VALIDATION_FAILED", ex.Message);
        }
        catch (CarrierAccountValidationException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "CARRIER_ACCOUNT_VALIDATION_FAILED", ex.Message);
        }
        catch (CarrierBookingFailedException ex)
        {
            return Error(StatusCodes.Status502BadGateway, "CARRIER_BOOKING_FAILED", ex.Message);
        }
        catch (CarrierUnavailableException ex)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "CARRIER_UNAVAILABLE", ex.Message);
        }
        catch (ShipmentInvalidTransitionException ex)
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    code = "SHIPMENT_INVALID_TRANSITION",
                    message = ex.Message,
                    details = new { shipmentId = ex.ShipmentId, from = ex.From.ToString(), to = ex.To.ToString() }
                }
            });
        }
        catch (ShipmentValidationException ex)
        {
            return Error(StatusCodes.Status400BadRequest, "SHIPMENT_VALIDATION_FAILED", ex.Message);
        }
    }

    public static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: statusCode);
}
