namespace DeuxiemeCerveau.App.Local;

/// <summary>
/// Identité de l'appareil pour la synchro (§6.2). Générée localement dès le premier lancement — la
/// saisie hors-ligne (filet 1) ne peut pas attendre un aller-retour serveur — et conservée dans
/// <c>sync_etat</c>. L'articulation avec l'enregistrement serveur (POST /devices/register, §6.2) est
/// tranchée à l'incrément 4d (client de synchro).
/// </summary>
public sealed class IdentiteAppareil(DepotLocal depot)
{
    public const string Cle = "appareil_id";

    /// <summary>Renvoie l'appareil_id de cette installation, en le créant au premier appel.</summary>
    public Guid Obtenir()
    {
        if (depot.LireEtat(Cle) is { } existant && Guid.TryParse(existant, out var id))
            return id;
        var nouveau = Guid.NewGuid();
        depot.EcrireEtat(Cle, nouveau.ToString());
        return nouveau;
    }
}
