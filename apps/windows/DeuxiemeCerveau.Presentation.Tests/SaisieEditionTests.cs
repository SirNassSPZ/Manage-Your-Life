using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// <b>Corriger</b> un Élément déjà saisi (§5 — « tout ce qui est affiché se modifie »).
/// <para>
/// Jusqu'ici un Élément était figé dès sa création : rattraper une faute de frappe demandait de le
/// supprimer et de le ressaisir. C'est la demande « il n'y a aucun moyen de modifier une entrée ou
/// une sortie ou de la supprimer ».
/// </para>
/// <para>
/// Le formulaire est le <b>même</b> qu'à la création — c'est ce qui garantit qu'un champ ajouté un
/// jour sera modifiable le même jour, sans second chemin d'écriture à tenir à jour.
/// </para>
/// </summary>
public class SaisieEditionTests
{
    private static Element Lire(FabriquePresentation f, Guid id)
    {
        var etat = f.Composition.Acces.Lire(() => f.Composition.Depot.Obtenir(EntiteSynchro.Element, id));
        Assert.NotNull(etat);
        return DeuxiemeCerveau.Core.Json.SerialisationCanonique.Deserialiser<Element>(etat!.PayloadCanonique);
    }

    private static VueModeleSaisie Corriger(FabriquePresentation f, Guid id)
    {
        var modele = new VueModeleSaisie(f.Composition);
        modele.OuvrirPour(Lire(f, id));
        return modele;
    }

    [Fact]
    public void Le_formulaire_s_ouvre_prerempli()
    {
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero), 84_000);

        var modele = Corriger(f, id);

        Assert.True(modele.EstEdition);
        Assert.True(modele.Ouvert);
        Assert.Equal("Modifier", modele.TitreFormulaire);
        Assert.Equal("Loyer", modele.Titre);
        Assert.Equal(TypeElement.Facture, modele.Type);
        Assert.Contains("840", modele.Montant);
    }

    [Fact]
    public void Corriger_modifie_l_element_au_lieu_d_en_creer_un_second()
    {
        // Le piège central : un objet neuf produirait un DOUBLON au lieu d'une correction.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyr", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero), 84_000);

        var modele = Corriger(f, id);
        modele.Titre = "Loyer";
        modele.EnregistrerCommand.Execute(null);

        var actifs = f.Composition.Acces.Lire(() => f.Composition.Lecture.Actifs());
        Assert.Single(actifs);
        Assert.Equal(id, actifs[0].Id);
        Assert.Equal("Loyer", actifs[0].Titre);
    }

    [Fact]
    public void Corriger_conserve_les_champs_d_audit_et_incremente_la_version()
    {
        // Repartir de l'entité STOCKÉE, jamais d'un objet neuf : la date de création et l'historique
        // de synchro appartiennent à l'Élément, pas au formulaire (§3.1).
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero), 84_000);
        var avant = Lire(f, id);

        var modele = Corriger(f, id);
        modele.Titre = "Loyer révisé";
        modele.EnregistrerCommand.Execute(null);

        var apres = Lire(f, id);
        Assert.Equal(avant.DateCreation, apres.DateCreation);
        Assert.True(apres.Version > avant.Version);
    }

    [Fact]
    public void Corriger_un_libelle_ne_depaie_pas_une_facture()
    {
        // Le statut appartient à la vie de l'Élément, pas au formulaire. Le remettre à « à venir »
        // à chaque correction annulerait silencieusement une confirmation de paiement (D-017).
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero), 84_000);
        f.Composition.Acces.Lire(() => f.Composition.Confirmation.Confirmer(id));
        Assert.Equal(StatutElement.Paye, Lire(f, id).Statut);

        var modele = Corriger(f, id);
        modele.Titre = "Loyer de juillet";
        modele.EnregistrerCommand.Execute(null);

        Assert.Equal(StatutElement.Paye, Lire(f, id).Statut);
    }

    [Fact]
    public void Changer_le_type_refait_le_statut()
    {
        // La table des statuts est propre à chaque type (§3.1) : garder « payé » sur un rendez-vous
        // produirait un Élément que le cœur refuse.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Dentiste", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero), 5_000);

        var modele = Corriger(f, id);
        modele.ChoisirTypeCommand.Execute(modele.Types.Single(t => t.Type == TypeElement.Rendezvous));
        modele.EnregistrerCommand.Execute(null);

        var apres = Lire(f, id);
        Assert.Equal(TypeElement.Rendezvous, apres.Type);
        Assert.Equal(StatutElement.Planifie, apres.Statut);
    }

    [Fact]
    public void Une_recurrence_trop_riche_survit_a_une_correction_de_titre()
    {
        // D-003 : le cœur accepte bien plus que les trois boutons du formulaire. « Dernier jour du
        // mois » s'y affiche « Aucune » — l'enregistrer tel quel EFFACERAIT la récurrence, sans
        // rien dire. C'est exactement le genre de perte qu'on ne remarque que des mois plus tard.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
            84_000, "FREQ=MONTHLY;BYMONTHDAY=-1");

        var modele = Corriger(f, id);
        Assert.Equal(Periodicite.Aucune, modele.Periodicite);   // le formulaire ne sait pas la montrer
        modele.Titre = "Loyer (dernier jour)";
        modele.EnregistrerCommand.Execute(null);

        Assert.Equal("FREQ=MONTHLY;BYMONTHDAY=-1", Lire(f, id).Recurrence);
    }

    [Fact]
    public void Choisir_une_periodicite_remplace_bien_la_recurrence()
    {
        // Le pendant du test précédent : préserver ne doit pas devenir « on ne peut plus changer ».
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero),
            84_000, "FREQ=MONTHLY;BYMONTHDAY=-1");

        var modele = Corriger(f, id);
        modele.Periodicite = Periodicite.Hebdomadaire;
        modele.EnregistrerCommand.Execute(null);

        Assert.Equal("FREQ=WEEKLY", Lire(f, id).Recurrence);
    }

    [Fact]
    public void Retirer_une_recurrence_simple_est_possible()
    {
        // Sans remise à blanc des champs pilotés par le formulaire, l'ancienne valeur survivrait et
        // « aucune répétition » serait impossible à exprimer.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero),
            84_000, "FREQ=MONTHLY");

        var modele = Corriger(f, id);
        Assert.Equal(Periodicite.Mensuel, modele.Periodicite);
        modele.Periodicite = Periodicite.Aucune;
        modele.EnregistrerCommand.Execute(null);

        Assert.Null(Lire(f, id).Recurrence);
    }

    [Fact]
    public void Supprimer_depuis_le_formulaire_met_a_la_corbeille()
    {
        // Filet 2 : marqué, jamais détruit (§5.6) — la corbeille doit pouvoir le rendre.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("À jeter", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero));

        var modele = Corriger(f, id);
        modele.SupprimerCommand.Execute(null);

        Assert.False(modele.Ouvert);
        Assert.Empty(f.Composition.Acces.Lire(() => f.Composition.Lecture.Actifs()));
        Assert.Single(f.Composition.Acces.Lire(() => f.Composition.Lecture.Corbeille()));
    }

    [Fact]
    public void Supprimer_ne_fait_rien_hors_edition()
    {
        // Il n'y a rien à supprimer d'un Élément qui n'existe pas encore.
        using var f = new FabriquePresentation();
        var modele = new VueModeleSaisie(f.Composition);
        modele.Ouvrir();

        modele.SupprimerCommand.Execute(null);

        Assert.True(modele.Ouvert);
        Assert.Null(modele.Erreur);
    }

    [Fact]
    public void Fermer_puis_rouvrir_en_creation_oublie_l_edition_precedente()
    {
        // Un état d'édition qui traîne ferait écraser un Élément existant par une création.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Loyer", new DateTimeOffset(2026, 7, 5, 9, 0, 0, TimeSpan.Zero), 84_000);

        var modele = Corriger(f, id);
        modele.FermerCommand.Execute(null);
        modele.Ouvrir();

        Assert.False(modele.EstEdition);
        Assert.Equal("Ajouter", modele.TitreFormulaire);

        modele.Titre = "Courses";
        modele.Montant = "50";
        modele.EnregistrerCommand.Execute(null);

        var actifs = f.Composition.Acces.Lire(() => f.Composition.Lecture.Actifs());
        Assert.Equal(2, actifs.Count);   // l'ancien intact, le nouveau créé
    }
}
