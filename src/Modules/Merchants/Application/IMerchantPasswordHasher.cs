namespace Courier.Merchants.Application;

public interface IMerchantPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
