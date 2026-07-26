using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace DokployMonitor.Infrastructure.Identity;

public static class MonitorClaims
{
    /// <summary>Kullanici kimlik bilgilerini degistirmeden panele giremez.</summary>
    public const string MustChangeCredentials = "dm:must_change_credentials";

    /// <summary>Ekranda gosterilecek ad (her istekte veritabanina gitmemek icin).</summary>
    public const string DisplayName = "dm:display_name";
}

/// <summary>
/// Oturum cerezine panele ozel talepleri ekler. Boylece "kimlik bilgisi degistirmeli"
/// kontrolu ve kullanici adi gosterimi veritabani sorgusu gerektirmez; degisiklikten
/// sonra <c>RefreshSignInAsync</c> ile cerez tazelenir.
/// </summary>
public sealed class MonitorClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.MustChangeCredentials)
        {
            identity.AddClaim(new Claim(MonitorClaims.MustChangeCredentials, "true"));
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(MonitorClaims.DisplayName, user.DisplayName));
        }

        return identity;
    }
}
