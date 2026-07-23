namespace DeuxiemeCerveau.Core.Modele;

/// <summary>
/// Projet (§3.2) : un objectif de vie. Un projet est un calendrier (filtre automatique, V2).
/// À la fermeture (« termine » ou « en_pause »), ses tâches « a_faire » passent « reporte » (§3.2) —
/// cascade appliquée côté serveur par le moteur de synchro (D-006).
/// </summary>
public sealed class Projet : EntiteSynchronisee
{
    public string Nom { get; set; } = string.Empty;

    /// <summary>Format « #RRGGBB ».</summary>
    public string Couleur { get; set; } = string.Empty;

    public Guid? CategorieId { get; set; }

    public StatutProjet Statut { get; set; }
}
