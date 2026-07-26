namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Notifications locales (§4, D-018 #4). La décision « quoi notifier, quand » est testée ici ;
/// le côté Windows ne fait que remettre le toast.
/// </summary>
public class RappelsTests
{
    private static DateTimeOffset Midi(DateOnly jour) =>
        new(jour.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    [Fact]
    public void Une_echeance_de_demain_est_rappelee_la_veille()
    {
        // La veille, pas le jour même : un rappel qui arrive le matin d'un prélèvement est inutile.
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterSortie("Loyer", Midi(demain), 84_000);

        var rappels = PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now);

        var rappel = Assert.Single(rappels);
        Assert.Equal("Échéance demain", rappel.Titre);
        Assert.Contains("Loyer", rappel.Corps);
        Assert.Contains("840", rappel.Corps);
    }

    [Fact]
    public void Ce_qui_tombe_aujourd_hui_ou_apres_demain_ne_declenche_rien()
    {
        using var f = new FabriquePresentation();
        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        f.AjouterSortie("Aujourd'hui", Midi(aujourdhui));
        f.AjouterSortie("Dans trois jours", Midi(aujourdhui.AddDays(3)));

        Assert.Empty(PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now));
    }

    [Fact]
    public void Un_revenu_et_un_rendez_vous_ont_leur_propre_formulation()
    {
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterEntree("Salaire", Midi(demain), 238_000);

        var rappel = Assert.Single(PlanificateurRappels.Echeances(f.Composition, DateTimeOffset.Now));

        Assert.Equal("Rentrée attendue demain", rappel.Titre);
        Assert.StartsWith("+", rappel.Corps.Split('—')[1].Trim());
    }

    [Fact]
    public void La_cle_porte_le_jour_pour_qu_un_rappel_ne_sorte_qu_une_fois()
    {
        // Le mode --rappels peut être déclenché plusieurs fois dans la journée.
        using var f = new FabriquePresentation();
        var demain = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        f.AjouterSortie("Loyer", Midi(demain));

        // Heure FIXE, pas DateTimeOffset.Now : « deux fois dans la même journée » doit le rester.
        // Avec Now, un lancement après 21:00 mettait le second appel au lendemain — « demain »
        // devenait le surlendemain, la liste sortait vide et le test échouait sur un index. Le code
        // de production avait raison ; c'était le test qui datait mal ses deux instants.
        var matin = new DateTimeOffset(DateTime.Today.AddHours(8));

        var premier = PlanificateurRappels.Echeances(f.Composition, matin);
        var second = PlanificateurRappels.Echeances(f.Composition, matin.AddHours(3));

        Assert.Equal(premier[0].Cle, second[0].Cle);
        Assert.Contains(demain.ToString("yyyy-MM-dd"), premier[0].Cle);
    }

    [Fact]
    public void Le_digest_resume_la_semaine_en_une_phrase()
    {
        using var f = new FabriquePresentation();
        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);
        f.AjouterEntree("Salaire", Midi(aujourdhui.AddDays(1)), 238_000);
        f.AjouterSortie("Loyer", Midi(aujourdhui.AddDays(2)), 84_000);

        var digest = PlanificateurRappels.Digest(f.Composition, DateTimeOffset.Now);

        Assert.NotNull(digest);
        Assert.Equal("Le point de la semaine", digest!.Titre);
        Assert.Contains("2 mouvements", digest.Corps);
        // Le groupement des milliers utilise une espace insécable fine : on vise les chiffres.
        Assert.Contains("380,00", digest.Corps);
        Assert.Contains("840,00", digest.Corps);
        Assert.Contains("+", digest.Corps);
        Assert.Contains("−", digest.Corps);
    }

    [Fact]
    public void Une_semaine_vide_ne_produit_aucun_digest()
    {
        // Une notification qui ne dit rien apprend à ignorer les suivantes.
        using var f = new FabriquePresentation();

        Assert.Null(PlanificateurRappels.Digest(f.Composition, DateTimeOffset.Now));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, true)]
    [InlineData(DayOfWeek.Monday, false)]
    [InlineData(DayOfWeek.Saturday, false)]
    public void Le_digest_est_un_rendez_vous_du_dimanche(DayOfWeek jour, bool attendu)
    {
        var date = new DateTime(2026, 7, 26);            // un dimanche
        var vise = date.AddDays(((int)jour - (int)DayOfWeek.Sunday + 7) % 7);

        Assert.Equal(attendu, PlanificateurRappels.EstJourDuDigest(new DateTimeOffset(vise)));
    }

    [Fact]
    public void Le_rappel_d_export_ne_sort_qu_une_fois_par_mois()
    {
        // §5.7 : mensuel. Il voyage avec le digest, donc il est PROPOSÉ tous les dimanches —
        // c'est la clé qui porte le mois, et le journal, qui en font un rappel mensuel.
        var juillet = new DateTimeOffset(new DateTime(2026, 7, 5), TimeSpan.Zero);
        var memeMois = new DateTimeOffset(new DateTime(2026, 7, 26), TimeSpan.Zero);
        var aout = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.Zero);

        Assert.Equal(PlanificateurRappels.RappelExport(juillet).Cle,
                     PlanificateurRappels.RappelExport(memeMois).Cle);
        Assert.NotEqual(PlanificateurRappels.RappelExport(juillet).Cle,
                        PlanificateurRappels.RappelExport(aout).Cle);
        Assert.Contains("2026-07", PlanificateurRappels.RappelExport(juillet).Cle);
    }
}

/// <summary>
/// Le journal (§4) : la mémoire de ce qui a déjà sonné sur CET appareil. Il survit au processus —
/// chaque déclenchement de la tâche planifiée est un processus neuf.
/// </summary>
public class JournalRappelsTests
{
    private static Rappel Quelconque(string cle) => new(cle, "Titre", "Corps");

    /// <summary>
    /// Un mercredi, fixe. Les passages quotidiens ne doivent pas se mettre à compter le digest
    /// parce que la suite tourne un dimanche — c'est arrivé.
    /// </summary>
    private static readonly DateTimeOffset Mercredi =
        new(new DateTime(2026, 7, 22, 12, 0, 0), TimeSpan.Zero);

    private static DateTimeOffset MidiLe(int joursApres) => Mercredi.AddDays(joursApres);

    [Fact]
    public void Un_rappel_deja_remis_ne_repasse_pas_au_declenchement_suivant()
    {
        using var f = new FabriquePresentation();
        var rappel = Quelconque("echeance-1-2026-07-27");
        var maintenant = DateTimeOffset.Now;

        var premier = JournalRappels.Ouvrir(f.Dossier);
        Assert.Single(premier.Inedits([rappel]));
        premier.Noter(rappel, maintenant);
        premier.Enregistrer(maintenant);

        // Processus neuf : tout doit venir du disque.
        Assert.Empty(JournalRappels.Ouvrir(f.Dossier).Inedits([rappel]));
    }

    [Fact]
    public void Ce_qui_n_a_pas_ete_note_repasse()
    {
        // On note APRÈS la remise : un toast qui échoue doit avoir une seconde chance.
        using var f = new FabriquePresentation();
        var rappel = Quelconque("echeance-2-2026-07-27");

        JournalRappels.Ouvrir(f.Dossier).Enregistrer(DateTimeOffset.Now);

        Assert.Single(JournalRappels.Ouvrir(f.Dossier).Inedits([rappel]));
    }

    [Fact]
    public void Le_journal_ne_garde_pas_les_cles_perimees()
    {
        using var f = new FabriquePresentation();
        var vieux = Quelconque("echeance-vieille");
        var maintenant = DateTimeOffset.Now;

        var journal = JournalRappels.Ouvrir(f.Dossier);
        journal.Noter(vieux, maintenant.AddDays(-120));
        journal.Enregistrer(maintenant);

        // Purgé : une clé datée de 2026-03 ne peut plus se représenter, elle ne ferait que grossir.
        Assert.Single(JournalRappels.Ouvrir(f.Dossier).Inedits([vieux]));
    }

    [Fact]
    public void Le_rappel_d_export_est_actif_par_defaut_et_desactivable()
    {
        // §5.7 : « notification locale, désactivable ». Actif sans qu'on le demande — c'est une
        // protection ; coupé, il le reste après redémarrage.
        using var f = new FabriquePresentation();

        Assert.True(JournalRappels.Ouvrir(f.Dossier).RappelExportActif);

        var journal = JournalRappels.Ouvrir(f.Dossier);
        journal.RappelExportActif = false;
        journal.Enregistrer(DateTimeOffset.Now);

        Assert.False(JournalRappels.Ouvrir(f.Dossier).RappelExportActif);
    }

    [Fact]
    public void Un_echec_de_remise_ne_note_rien_et_laisse_le_rappel_repasser()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", MidiLe(1));

        var echoue = PlanificateurRappels.Derouler(
            f.Composition, JournalRappels.Ouvrir(f.Dossier), Mercredi, forcerDigest: false,
            remettre: _ => false);

        Assert.Equal(0, echoue);

        var reussi = PlanificateurRappels.Derouler(
            f.Composition, JournalRappels.Ouvrir(f.Dossier), Mercredi, forcerDigest: false,
            remettre: _ => true);

        Assert.Equal(1, reussi);
    }

    [Fact]
    public void Un_second_passage_dans_la_journee_ne_remet_rien()
    {
        // Le cas réel : la tâche planifiée du matin, puis une ouverture de session l'après-midi.
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", MidiLe(1));

        var premier = PlanificateurRappels.Derouler(
            f.Composition, JournalRappels.Ouvrir(f.Dossier), Mercredi,
            forcerDigest: false, remettre: _ => true);
        var second = PlanificateurRappels.Derouler(
            f.Composition, JournalRappels.Ouvrir(f.Dossier), Mercredi.AddHours(6),
            forcerDigest: false, remettre: _ => true);

        Assert.Equal(1, premier);
        Assert.Equal(0, second);
    }

    [Fact]
    public void Le_passage_hebdomadaire_ajoute_le_digest_et_le_rappel_d_export()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", MidiLe(2));

        var remis = new List<Rappel>();
        PlanificateurRappels.Derouler(
            f.Composition, JournalRappels.Ouvrir(f.Dossier), Mercredi,
            forcerDigest: true, remettre: r => { remis.Add(r); return true; });

        Assert.Contains(remis, r => r.Titre == "Le point de la semaine");
        Assert.Contains(remis, r => r.Cle.StartsWith("export-", StringComparison.Ordinal));
    }

    [Fact]
    public void Le_rappel_d_export_coupe_ne_sort_pas()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", MidiLe(2));

        var journal = JournalRappels.Ouvrir(f.Dossier);
        journal.RappelExportActif = false;

        var remis = new List<Rappel>();
        PlanificateurRappels.Derouler(
            f.Composition, journal, Mercredi,
            forcerDigest: true, remettre: r => { remis.Add(r); return true; });

        Assert.DoesNotContain(remis, r => r.Cle.StartsWith("export-", StringComparison.Ordinal));
        // Le digest, lui, n'est pas concerné par ce réglage.
        Assert.Contains(remis, r => r.Titre == "Le point de la semaine");
    }

    [Fact]
    public void Un_journal_illisible_ne_fait_pas_tomber_la_notification()
    {
        using var f = new FabriquePresentation();
        File.WriteAllText(Path.Combine(f.Dossier, JournalRappels.NomFichier), "{ ceci n'est pas du json");

        var journal = JournalRappels.Ouvrir(f.Dossier);

        Assert.Single(journal.Inedits([Quelconque("echeance-3")]));
        Assert.True(journal.RappelExportActif);
    }
}
