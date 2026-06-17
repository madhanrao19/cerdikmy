using Cerdik.Application.Abstractions;

namespace Cerdik.Infrastructure.Auth;

/// <summary>BCrypt-backed password hashing.</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, workFactor: 11);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch
        {
            return false;
        }
    }
}
