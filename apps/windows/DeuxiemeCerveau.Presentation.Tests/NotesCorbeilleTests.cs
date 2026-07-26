using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Note libre (§5.5) — <b>boîte de capture</b> : on écrit dans une zone vierge, on enregistre, la
/// zone se vide. Deux promesses à tenir ensemble, et elles tirent en sens contraire : la zone doit
/// se vider à l'enregistrement, et <b>un brouillon ne doit jamais se perdre</b>.
/// </summary>
public class NotesTests
{
    [Fact]
    public void On_ecrit_sans_avoir_a_creer_une_note_d_abord()
    {
        // L'ancien écran exigeait de créer la note AVANT de pouvoir taper un mot. Une boîte de
        // capture est prête tout de suite : la note naît de l'enregistrement.
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);
        Assert.True(vue.AucuneNote);
        Assert.Null(vue.Ouverte);

        vue.Titre = "Courses";
        vue.Texte = "Penser au pain";
        vue.EnregistrerCommand.Execute(null);

        var note = Assert.Single(vue.Notes);
        Assert.Equal("Courses", note.Titre);
    }

    [Fact]
    public void Enregistrer_vide_la_zone()
    {
        // LE point de la demande : « la zone doit se réinitialiser pour pouvoir y mettre d'autres
        // notes ». Un texte qui reste laisse croire qu'il n'est pas parti.
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Titre = "Courses";
        vue.Texte = "Penser au pain";
        vue.EnregistrerCommand.Execute(null);

        Assert.Equal("", vue.Titre);
        Assert.Equal("", vue.Texte);
        Assert.Null(vue.Ouverte);
        Assert.False(vue.AUneNoteOuverte);
        Assert.False(vue.Modifiee);
    }

    [Fact]
    public void Enregistrer_vide_la_zone_meme_apres_correction_d_une_note_rouverte()
    {
        // Aucune exception : rouvrir pour corriger n'ouvre pas un « document » qui resterait à
        // l'écran. La règle est la même dans les deux sens (§5.5).
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Titre = "Courses";
        vue.EnregistrerCommand.Execute(null);

        vue.OuvrirNoteCommand.Execute(Assert.Single(vue.Notes).Id);
        Assert.Equal("Courses", vue.Titre);   // rouverte pour correction
        Assert.True(vue.AUneNoteOuverte);

        vue.Texte = "et du lait";
        vue.EnregistrerCommand.Execute(null);

        Assert.Equal("", vue.Titre);
        Assert.Equal("", vue.Texte);
        Assert.Null(vue.Ouverte);
        Assert.Equal("et du lait", Assert.Single(vue.Notes).Apercu);
    }

    [Fact]
    public void Un_texte_en_cours_survit_a_l_ouverture_d_une_autre_note()
    {
        // La promesse du §5.5 tient malgré le vidage : passer à autre chose enregistre d'abord.
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Titre = "Déjà là";
        vue.EnregistrerCommand.Execute(null);

        vue.Titre = "En cours";
        vue.Texte = "pas encore enregistré";
        vue.OuvrirNoteCommand.Execute(vue.Notes.Single(n => n.Titre == "Déjà là").Id);

        Assert.Equal(2, vue.Notes.Count);
        Assert.Contains(vue.Notes, n => n.Titre == "En cours");
    }

    [Fact]
    public void Nouvelle_enregistre_avant_de_liberer_la_zone()
    {
        // « Je passe à la suivante » ne veut jamais dire « jette ce que je viens d'écrire ».
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Texte = "à ne pas perdre";
        vue.NouvelleCommand.Execute(null);

        Assert.Equal("", vue.Texte);
        Assert.Equal("à ne pas perdre", Assert.Single(vue.Notes).Apercu);
    }

    [Fact]
    public void Une_zone_vide_n_enregistre_rien()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.EnregistrerCommand.Execute(null);
        vue.NouvelleCommand.Execute(null);

        Assert.True(vue.AucuneNote);
    }

    [Fact]
    public void Une_note_sans_titre_reste_reperable_dans_la_liste()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Titre = "   ";
        vue.Texte = "du contenu";
        vue.EnregistrerCommand.Execute(null);

        Assert.Equal("(sans titre)", Assert.Single(vue.Notes).Titre);
    }

    [Fact]
    public void L_apercu_resume_le_texte_sans_le_deborder()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Texte = new string('a', 200);
        vue.EnregistrerCommand.Execute(null);

        var apercu = Assert.Single(vue.Notes).Apercu;
        Assert.EndsWith("…", apercu);
        Assert.True(apercu.Length <= 71);
    }

    [Fact]
    public void Une_note_sans_texte_le_dit_plutot_que_de_montrer_du_blanc()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Titre = "Titre seul";
        vue.EnregistrerCommand.Execute(null);

        Assert.Equal("Vide", Assert.Single(vue.Notes).Apercu);
    }

    [Fact]
    public void Supprimer_une_note_rouverte_la_met_a_la_corbeille_et_vide_la_zone()
    {
        using var f = new FabriquePresentation();
        var vue = new VueModeleNotes(f.Composition);

        vue.Titre = "À jeter";
        vue.EnregistrerCommand.Execute(null);
        vue.OuvrirNoteCommand.Execute(Assert.Single(vue.Notes).Id);

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
