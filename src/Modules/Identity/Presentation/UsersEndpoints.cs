using Courier.Identity.Domain;
using Courier.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Courier.Identity.Presentation;

public record UserListItem(Guid Id, string Email, string FullName, bool IsActive);

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/users", async (DbContext dbContext) =>
        {
            var users = await dbContext.Set<User>()
                .Select(u => new UserListItem(u.Id, u.Email, u.FullName, u.IsActive))
                .ToListAsync();
            return Results.Ok(users);
        }).RequirePermission("user.read")
        .WithTags("Users Management");
    }
}
