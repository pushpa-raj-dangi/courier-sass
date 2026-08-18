using Courier.Identity.Application;
using Courier.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Courier.Identity.Infrastructure;

public class PasswordHasherAdapter : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string password, string hash)
        => _inner.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
