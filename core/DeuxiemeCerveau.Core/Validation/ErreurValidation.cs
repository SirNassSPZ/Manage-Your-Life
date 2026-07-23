namespace DeuxiemeCerveau.Core.Validation;

/// <summary>Une erreur de validation : code stable (contrat d'API) + message lisible.</summary>
public sealed record ErreurValidation(string Champ, string Code, string Message)
{
    public override string ToString() => $"[{Code}] {Champ} : {Message}";
}
