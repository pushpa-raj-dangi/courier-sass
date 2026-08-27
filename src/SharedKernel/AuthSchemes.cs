namespace Courier.SharedKernel;

public static class AuthSchemes
{
    // Mirrors Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme's
    // value ("Bearer") without adding a JwtBearer package reference to SharedKernel, which has no
    // other ASP.NET Core auth dependency today.
    public const string Bearer = "Bearer";
    public const string ApiKey = "ApiKey";
}
