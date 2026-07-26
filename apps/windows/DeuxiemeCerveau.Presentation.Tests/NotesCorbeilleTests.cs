using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>Note libre (§5.5) — « un brouillon ne se perd jamais » est la seule promesse à tenir.</summary>
public class NotesTests
{
    [Fact]
    public void Creer_une_note_l_ouvre_aussitot()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        Assert.True(vue.AucuneNote);

        vue.NouvelleCommand.Execute(null);

        Assert.Single(vue.Notes);
        Assert.True(vue.AUneNoteOuverte);
        Assert.NotNull(vue.Ouverte);
        Assert.False(vue.AucuneNote);
    }

    [Fact]
    public void Le_texte_saisi_survit_a_un_changement_de_note()
    {
        // C'est LA promesse du §5.5 : rien ne se perd entre deux gestes.
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.NouvelleCommand.Execute(null);
        vue.Titre = "Courses";
        vue.Texte = "Penser au pain";
        Assert.True(vue.Modifiee);

        // Créer une seconde note enregistre la première au passage.
        vue.NouvelleCommand.Execute(null);
        vue.Titre = "Autre";

        var courses = vue.Notes.Single(n => n.Titre == "Courses");
        vue.OuvrirNoteCommand.Execute(courses.Id);

        Assert.Equal("Courses", vue.Titre);
        Assert.Equal("Penser au pain", vue.Texte);
        Assert.False(vue.Modifiee);
        Assert.Equal("Enregistrée", vue.Etat);
    }

    [Fact]
    public void L_etat_dit_franchement_si_la_saisie_est_en_securite()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        vue.NouvelleCommand.Execute(null);

        vue.Texte = "quelque chose";
        Assert.Equal("Modifiée — non enregistrée", vue.Etat);

        vue.EnregistrerCommand.Execute(null);
        Assert.Equal("Enregistrée", vue.Etat);
        Assert.False(vue.Modifiee);
    }

    [Fact]
    public void Une_note_sans_titre_reste_reperable_dans_la_liste()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        vue.NouvelleCommand.Execute(null);

        vue.Titre = "   ";
        vue.EnregistrerCommand.Execute(null);

        Assert.Equal("(sans titre)", Assert.Single(vue.Notes).Titre);
    }

    [Fact]
    public void L_apercu_resume_le_texte_sans_le_deborder()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        vue.NouvelleCommand.Execute(null);
        vue.Texte = new string('a', 200);
        vue.EnregistrerCommand.Execute(null);

        var apercu = Assert.Single(vue.Notes).Apercu;
        Assert.EndsWith("…", apercu);
        Assert.True(apercu.Length <= 71);
    }

    [Fact]
    public void Une_note_vide_le_dit_plutot_que_de_montrer_du_blanc()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        vue.NouvelleCommand.Execute(null);
        vue.EnregistrerCommand.Execute(null);

        Assert.Equal("Vide", Assert.Single(vue.Notes).Apercu);
    }

    [Fact]
    public void Supprimer_une_note_la_met_a_la_corbeille_et_ferme_l_editeur()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        vue.NouvelleCommand.Execute(null);
        vue.Titre = "À jeter";
        vue.EnregistrerCommand.Execute(null);

        vue.SupprimerCommand.Execute(null);

        Assert.True(vue.AucuneNote);
        Assert.False(vue.AUneNoteOuverte);
        Assert.Null(vue.Ouverte);
        Assert.Contains("corbeille", vue.Etat);

        // Filet 2 : marquée, pas détruite.
        Assert.Single(f.Composition.Lecture.Corbeille());
    }
}

/// <summary>Corbeille (§5.6) — restauration en un geste, purge sur confirmation explicite.</summary>
public class CorbeilleTests
{
    [Fact]
    public void Une_corbeille_vide_le_dit()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleCorbeille(f.Composition);

        Assert.True(vue.Vide);
        Assert.Empty(vue.Lignes);
    }

    [Fact]
    public void Un_element_supprime_apparait_avec_sa_nature()
    {
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Courses", DateTimeOffset.UtcNow);
        f.Composition.Saisie.Supprimer(EntiteSynchro.Element, id);

        var vue = new VueModeleCorbeille(f.Composition);
        var ligne = Assert.Single(vue.Lignes);

        Assert.Equal("Courses", ligne.Titre);
        Assert.Equal("Facture", ligne.Nature);
        Assert.Equal("Supprimé aujourd'hui", ligne.SupprimeLe);
        Assert.False(vue.Vide);
    }

    [Fact]
    public void Un_calendrier_supprime_est_lui_aussi_restaurable()
    {
        // Régression : la corbeille ne listait que les Éléments, donc une catégorie supprimée
        // depuis « Gérer les calendriers » était perdue sans recours — un marquage déguisé en
        // destruction. Le filet 2 vaut pour toute entité synchronisée (D-006).
        using var f = new FabriquePresentation();
        var id = f.AjouterCategorie("Sport");
        f.Composition.Saisie.Supprimer(EntiteSynchro.Categorie, id);

        var vue = new VueModeleCorbeille(f.Composition);
        var ligne = Assert.Single(vue.Lignes);

        Assert.Equal("Sport", ligne.Titre);
        Assert.Equal("Calendrier", ligne.Nature);
        Assert.Equal(EntiteSynchro.Categorie, ligne.Entite);
    }

    [Fact]
    public void Restaurer_remet_l_element_en_service_sans_confirmation()
    {
        // Restaurer ne détruit rien : le geste doit être immédiat.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Courses", DateTimeOffset.UtcNow);
        f.Composition.Saisie.Supprimer(EntiteSynchro.Element, id);

        var vue = new VueModeleCorbeille(f.Composition);
        vue.RestaurerCommand.Execute(Assert.Single(vue.Lignes));

        Assert.True(vue.Vide);
        Assert.Contains("de retour", vue.Message);
        Assert.Contains(f.Composition.Lecture.Actifs(), e => e.Titre == "Courses");
    }

    [Fact]
    public void Restaurer_un_calendrier_le_remet_dans_la_barre_laterale()
    {
        using var f = new FabriquePresentation();
        var id = f.AjouterCategorie("Sport");
        f.Composition.Saisie.Supprimer(EntiteSynchro.Categorie, id);

        var coquille = f.Coquille();
        Assert.Empty(coquille.Calendriers);

        coquille.Corbeille.RestaurerCommand.Execute(Assert.Single(coquille.Corbeille.Lignes));

        Assert.Equal("Sport", Assert.Single(coquille.Calendriers).Nom);
    }

    [Fact]
    public void La_purge_exige_deux_gestes()
    {
        // §5.6 : la seule destruction réelle de l'application. Elle doit coûter un geste de plus.
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Courses", DateTimeOffset.UtcNow);
        f.Composition.Saisie.Supprimer(EntiteSynchro.Element, id);

        var vue = new VueModeleCorbeille(f.Composition);
        var ligne = Assert.Single(vue.Lignes);
        Assert.False(ligne.ConfirmationPurge);

        vue.DemanderPurgeCommand.Execute(ligne);
        Assert.True(ligne.ConfirmationPurge);
        Assert.Single(vue.Lignes);              // rien n'est encore détruit

        vue.ConfirmerPurgeCommand.Execute(ligne);
        Assert.True(vue.Vide);
        Assert.Contains("définitivement", vue.Message);
    }

    [Fact]
    public void Annuler_une_demande_de_purge_ne_detruit_rien()
    {
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Courses", DateTimeOffset.UtcNow);
        f.Composition.Saisie.Supprimer(EntiteSynchro.Element, id);

        var vue = new VueModeleCorbeille(f.Composition);
        var ligne = Assert.Single(vue.Lignes);

        vue.DemanderPurgeCommand.Execute(ligne);
        vue.AnnulerPurgeCommand.Execute(ligne);

        Assert.False(ligne.ConfirmationPurge);
        Assert.Single(vue.Lignes);
    }

    [Fact]
    public void Une_seule_purge_est_en_attente_de_confirmation_a_la_fois()
    {
        // Deux boutons « Confirmer » ouverts en même temps invitent au clic de trop.
        using var f = new FabriquePresentation();
        foreach (var titre in new[] { "A", "B" })
        {
            var id = f.AjouterSortie(titre, DateTimeOffset.UtcNow);
            f.Composition.Saisie.Supprimer(EntiteSynchro.Element, id);
        }

        var vue = new VueModeleCorbeille(f.Composition);
        vue.DemanderPurgeCommand.Execute(vue.Lignes[0]);
        vue.DemanderPurgeCommand.Execute(vue.Lignes[1]);

        Assert.False(vue.Lignes[0].ConfirmationPurge);
        Assert.True(vue.Lignes[1].ConfirmationPurge);
    }
}
