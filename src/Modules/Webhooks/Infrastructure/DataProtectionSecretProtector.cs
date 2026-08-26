using Courier.Webhooks.Application;
using Microsoft.AspNetCore.DataProtection;

namespace Courier.Webhooks.Infrastructure;

public class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    // A named purpose string ("Webhooks.Secret") scopes this protector's keys away from any
    // other Data Protection consumer this codebase might add later — ASP.NET Core's Data
    // Protection system derives a distinct key per purpose string from the same master key
    // ring, so two different purposes can never decrypt each other's ciphertext even if both
    // exist in the same process.
    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Webhooks.Secret");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
