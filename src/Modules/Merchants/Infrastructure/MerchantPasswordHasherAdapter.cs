using Courier.Merchants.Application;
using Courier.Merchants.Domain;
using Microsoft.AspNetCore.Identity;

namespace Courier.Merchants.Infrastructure;

public class MerchantPasswordHasherAdapter : IMerchantPasswordHasher
{
    private readonly PasswordHasher<MerchantUser> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string password, string hash)
        => _inner.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
