using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DeuxiemeCerveau.Api.Auth;

/// <summary>
/// Valide un jeton Bearer Entra ID (§8) : signature (clés publiques récupérées des métadonnées OIDC,
/// mises en cache et rafraîchies), audience, émetteur, durée de vie. Aucune logique métier — pur
/// adaptateur d'authentification (règle 4). Testable hors ligne via des paramètres injectés.
/// </summary>
public sealed class ValidateurJeton
{
    private readonly TokenValidationParameters _parametres;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _config;
    private readonly JsonWebTokenHandler _handler = new();

    public ValidateurJeton(
        TokenValidationParameters parametres,
        IConfigurationManager<OpenIdConnectConfiguration>? config = null)
    {
        _parametres = parametres;
        _config = config;
    }

    /// <summary>Construit un validateur qui récupère ses clés depuis les métadonnées OIDC de l'autorité.</summary>
    public static ValidateurJeton DepuisOptions(OptionsAuth options)
    {
        var config = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{options.Autorite}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        var parametres = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences = [options.Audience, $"api://{options.Audience}"],
            ValidateIssuer = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };
        return new ValidateurJeton(parametres, config);
    }

    /// <summary>Renvoie l'identité si le jeton est valide, sinon null.</summary>
    public async Task<ClaimsPrincipal?> ValiderAsync(string jeton, CancellationToken annulation = default)
    {
        var parametres = _parametres.Clone();
        if (_config is not null)
        {
            var oidc = await _config.GetConfigurationAsync(annulation);
            parametres.IssuerSigningKeys = oidc.SigningKeys;
            parametres.ValidIssuer ??= oidc.Issuer;
        }

        var resultat = await _handler.ValidateTokenAsync(jeton, parametres);
        return resultat.IsValid ? new ClaimsPrincipal(resultat.ClaimsIdentity) : null;
    }
}
