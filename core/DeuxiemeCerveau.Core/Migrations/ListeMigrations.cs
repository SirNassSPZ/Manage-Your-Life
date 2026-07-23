namespace DeuxiemeCerveau.Core.Migrations;

/// <summary>
/// LA liste des migrations (règle 18) : définie une seule fois ici, répliquée à l'identique côté
/// Swift depuis ce fichier de référence. Numérotées, additives uniquement — jamais de suppression
/// ni de renommage en V1–V3. Deux apps aux schémas divergents sont une cause directe de perte de données.
/// </summary>
public static class ListeMigrations
{
    public static readonly IReadOnlyList<Migration> Toutes =
    [
        Migration001SchemaInitial.Definition,
        Migration002Purges.Definition,
    ];
}
