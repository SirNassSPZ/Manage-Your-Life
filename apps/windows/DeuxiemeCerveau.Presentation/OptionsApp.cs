namespace DeuxiemeCerveau.Presentation;

/// <summary>
/// Réglages lus depuis appsettings.json, surchargeables par appsettings.local.json (gitignoré).
/// Aucun secret ici : l'URL de l'API et les identifiants Entra sont des identifiants PUBLICS
/// (règle 16 — l'adresse de l'API est un paramètre de configuration, jamais codée en dur).
/// </summary>
public sealed class OptionsApp
{
    public OptionsApi Api { get; init; } = new();
    public OptionsEntra Entra { get; init; } = new();
}

public sealed class OptionsApi
{
    /// <summary>
    /// Adresse de base de l'API. DOIT se terminer par « / » : ClientApiHttp appelle des chemins
    /// relatifs (api/sync/push), et sans la barre finale le dernier segment serait remplacé.
    /// </summary>
    public string Url { get; init; } = "";

    public bool EstConfiguree => !string.IsNullOrWhiteSpace(Url);

    /// <summary>Adresse normalisée, barre finale garantie.</summary>
    public Uri BaseUri() => new(Url.EndsWith('/') ? Url : Url + "/");
}

public sealed class OptionsEntra
{
    /// <summary>Inscription d'application « client public » (desktop). Vide = mode hors ligne.</summary>
    public string ClientId { get; init; } = "";

    public string TenantId { get; init; } = "";

    /// <summary>
    /// Portée demandée à l'API. En configuration et non en dur, pour pouvoir basculer d'un
    /// « /.default » vers une portée nommée sans recompiler.
    /// </summary>
    public string[] Portees { get; init; } = [];

    public bool EstConfiguree =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(TenantId) && Portees.Length > 0;
}
