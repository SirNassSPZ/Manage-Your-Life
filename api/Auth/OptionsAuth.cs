namespace DeuxiemeCerveau.Api.Auth;

/// <summary>
/// Configuration de l'authentification Entra ID (§8, §10). Lue depuis les réglages d'application
/// (jamais de secret : uniquement des identifiants publics). Désactivable en local et dans les tests.
/// </summary>
public sealed class OptionsAuth
{
    /// <summary>Vrai pour exiger un jeton Bearer valide sur chaque appel (hors ping).</summary>
    public bool Activee { get; init; }

    /// <summary>Audience attendue du jeton : l'identifiant de l'inscription d'application « API ».</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>Locataire Entra : GUID du locataire, ou « common » / « organizations » / « consumers ».</summary>
    public string LocataireId { get; init; } = "common";

    public string Autorite => $"https://login.microsoftonline.com/{LocataireId}/v2.0";

    public static OptionsAuth DepuisEnvironnement()
    {
        bool Vrai(string cle) =>
            string.Equals(Environment.GetEnvironmentVariable(cle), "true", StringComparison.OrdinalIgnoreCase);
        return new OptionsAuth
        {
            Activee = Vrai("AUTH_ACTIVEE"),
            Audience = Environment.GetEnvironmentVariable("ENTRA_AUDIENCE") ?? string.Empty,
            LocataireId = Environment.GetEnvironmentVariable("ENTRA_TENANT_ID") is { Length: > 0 } t ? t : "common",
        };
    }
}
