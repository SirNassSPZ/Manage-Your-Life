using System.Security.Cryptography;
using DeuxiemeCerveau.Api.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DeuxiemeCerveau.Api.Tests;

/// <summary>
/// Validation des jetons Bearer (§8) testée hors ligne : on signe des jetons avec une clé connue et
/// on vérifie que le validateur accepte les jetons conformes et rejette tout le reste. En production,
/// les clés publiques viennent des métadonnées OIDC d'Entra ID (même logique de validation).
/// </summary>
public class ValidateurJetonTests
{
    private const string Emetteur = "https://login.microsoftonline.com/test/v2.0";
    private const string Audience = "api://deuxieme-cerveau";

    private readonly RsaSecurityKey _cle = new(RSA.Create(2048)) { KeyId = "cle-test" };

    private ValidateurJeton Validateur() => new(new TokenValidationParameters
    {
        ValidateAudience = true,
        ValidAudiences = [Audience, $"api://{Audience}"],
        ValidateIssuer = true,
        ValidIssuer = Emetteur,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _cle,
        ClockSkew = TimeSpan.FromMinutes(2),
    });

    private string Jeton(
        string? emetteur = null, string? audience = null,
        DateTime? expiration = null, SecurityKey? cle = null)
    {
        var descripteur = new SecurityTokenDescriptor
        {
            Issuer = emetteur ?? Emetteur,
            Audience = audience ?? Audience,
            Expires = expiration ?? DateTime.UtcNow.AddMinutes(30),
            SigningCredentials = new SigningCredentials(cle ?? _cle, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descripteur);
    }

    [Fact]
    public async Task Jeton_conforme_accepte()
    {
        var principal = await Validateur().ValiderAsync(Jeton());
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task Mauvaise_audience_rejetee()
        => Assert.Null(await Validateur().ValiderAsync(Jeton(audience: "api://autre-appli")));

    [Fact]
    public async Task Mauvais_emetteur_rejete()
        => Assert.Null(await Validateur().ValiderAsync(Jeton(emetteur: "https://pirate.example/v2.0")));

    [Fact]
    public async Task Jeton_expire_rejete()
        => Assert.Null(await Validateur().ValiderAsync(Jeton(expiration: DateTime.UtcNow.AddMinutes(-10))));

    [Fact]
    public async Task Signature_par_une_autre_cle_rejetee()
    {
        var autreCle = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "cle-pirate" };
        Assert.Null(await Validateur().ValiderAsync(Jeton(cle: autreCle)));
    }

    [Fact]
    public async Task Chaine_non_jwt_rejetee()
        => Assert.Null(await Validateur().ValiderAsync("ceci-n-est-pas-un-jeton"));
}
