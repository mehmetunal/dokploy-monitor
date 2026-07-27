using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace DokployMonitor.Infrastructure.GitHub;

/// <summary>GitHub App JWT (RS256). GitHub <c>iat</c> / <c>exp</c> / <c>iss</c> ister.</summary>
internal static class GitHubAppJwt
{
    public static string Create(long appId, string privateKeyPem, TimeSpan? lifetime = null)
    {
        var lifetimeValue = lifetime ?? TimeSpan.FromMinutes(9);
        if (lifetimeValue > TimeSpan.FromMinutes(10))
        {
            lifetimeValue = TimeSpan.FromMinutes(9);
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var key = new RsaSecurityKey(rsa)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };

        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        // GitHub: iat en fazla 60 sn gecmis olabilir; exp max 10 dk.
        var now = DateTime.UtcNow;
        var iat = now.AddSeconds(-30);
        var exp = now.Add(lifetimeValue);

        var header = new JwtHeader(credentials);
        var payload = new JwtPayload(
            issuer: appId.ToString(),
            audience: null,
            claims: null,
            notBefore: null,
            expires: exp,
            issuedAt: iat);

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}
