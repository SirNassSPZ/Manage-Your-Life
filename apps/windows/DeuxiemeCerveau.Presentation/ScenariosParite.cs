using System.Text;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

namespace DeuxiemeCerveau.Presentation;

/// <summary>Résultat d'un scénario : réussi ou non, avec de quoi comprendre pourquoi.</summary>
public sealed record ResultatScenario(string Nom, bool Reussi, string Detail);

/// <summary>
/// Scénarios de parité solo (§12, Étape 4g) déroulés sur l'application RÉELLE : vraie base locale,
/// vrai moteur de synchro, vraie API déployée, vrai jeton Entra.
/// <para>
/// Les tests de <c>DeuxiemeCerveau.App.Tests</c> couvrent déjà ces trois scénarios, mais contre une
/// API factice en mémoire : ils prouvent la <b>logique du client</b>. Ceci prouve l'<b>assemblage</b>
/// — configuration, authentification, sérialisation sur le fil, serveur déployé.
/// </para>
/// <para>
/// <b>Isolation :</b> chaque exécution monte son propre dossier de données temporaire. Les données
/// de l'utilisateur ne sont jamais touchées, ni lues, ni écrites.
/// </para>
/// </summary>
public static class ScenariosParite
{
    /// <summary>Option de ligne de commande : <c>--parite</c>.</summary>
    public const string Option = "--parite";

    public static async Task<IReadOnlyList<ResultatScenario>> Executer(
        OptionsApp options, IFournisseurJeton jetons,
        Func<IFournisseurJeton, HttpMessageHandler>? manipulateurApi = null)
    {
        var resultats = new List<ResultatScenario>();

        if (!options.Api.EstConfiguree)
            return [new("Préalable", false, "Aucune URL d'API configurée : rien à vérifier de bout en bout.")];

        if (await jetons.ObtenirSilencieux() is null)
            return [new("Préalable", false,
                "Non connecté. Connecte-toi dans l'application, puis relance : ces scénarios exigent le vrai serveur.")];

        resultats.Add(await Scenario1(options, jetons, manipulateurApi));
        resultats.Add(await Scenario2(options, jetons, manipulateurApi));
        resultats.Add(Scenario3(options, jetons, manipulateurApi));
        return resultats;
    }

    /// <summary>1 — Saisie hors-ligne puis reconnexion : tout remonte, aucun doublon.</summary>
    private static async Task<ResultatScenario> Scenario1(
        OptionsApp options, IFournisseurJeton jetons, Func<IFournisseurJeton, HttpMessageHandler>? manip)
    {
        const string nom = "1 · Saisie hors-ligne puis reconnexion";
        try
        {
            using var poste = new PosteJetable(options, jetons, manip);
            var marque = Marque();

            // « Hors-ligne » : on saisit sans jamais appeler la synchro. L'écriture est locale et
            // immédiate (filet 1) ; l'outbox retient ce qui devra partir.
            for (var i = 1; i <= 3; i++) poste.Saisir($"{marque} hors-ligne {i}");

            var enAttente = poste.Composition.Depot.Outbox().Count;
            if (enAttente < 3)
                return new(nom, false, $"L'outbox ne retient que {enAttente} changements sur 3 : la saisie hors-ligne perd des données.");

            // « Reconnexion » : un cycle de synchro complet contre le serveur réel.
            await poste.Composition.Synchro.Synchroniser("parite-windows", "windows");

            var reste = poste.Composition.Depot.Outbox().Count;
            if (reste != 0)
                return new(nom, false, $"{reste} changements restent dans l'outbox après synchro : tout n'est pas remonté.");

            // Aucun doublon : le serveur doit connaître exactement 3 Éléments portant la marque.
            var duServeur = await poste.CompterAuServeur(marque);
            return duServeur == 3
                ? new(nom, true, "3 saisies hors-ligne remontées, outbox vidée, 3 Éléments au serveur — aucun doublon.")
                : new(nom, false, $"Le serveur en compte {duServeur} au lieu de 3.");
        }
        catch (Exception ex)
        {
            return new(nom, false, "Échec : " + ex.Message);
        }
    }

    /// <summary>2 — Coupure réseau en plein push : même état final, aucun doublon (idempotence).</summary>
    private static async Task<ResultatScenario> Scenario2(
        OptionsApp options, IFournisseurJeton jetons, Func<IFournisseurJeton, HttpMessageHandler>? manip)
    {
        const string nom = "2 · Coupure en plein push";
        try
        {
            using var poste = new PosteJetable(options, jetons, manip);
            var marque = Marque();
            for (var i = 1; i <= 2; i++) poste.Saisir($"{marque} coupure {i}");

            // La coupure : le serveur applique le lot, mais la réponse n'arrive pas — donc l'outbox
            // n'est PAS vidée. On reproduit exactement cet état en gardant le lot de côté, puis en
            // le remettant dans l'outbox après un cycle réussi : le second envoi porte les mêmes
            // change_id. C'est là que l'idempotence du §6.2 se joue.
            var lot = poste.Composition.Depot.Outbox().ToList();
            await poste.Composition.Synchro.Synchroniser("parite-windows", "windows");

            // Reprise après la coupure : le même lot repart, à l'identique.
            foreach (var changement in lot) poste.Composition.Depot.AjouterOutbox(changement);
            await poste.Composition.Synchro.Synchroniser("parite-windows", "windows");

            var duServeur = await poste.CompterAuServeur(marque);
            return duServeur == 2
                ? new(nom, true, "Lot appliqué deux fois, 2 Éléments au serveur — l'idempotence tient.")
                : new(nom, false, $"Le serveur en compte {duServeur} au lieu de 2 : le renvoi a créé des doublons.");
        }
        catch (Exception ex)
        {
            return new(nom, false, "Échec : " + ex.Message);
        }
    }

    /// <summary>3 — Export réseau coupé, puis import sur installation vierge : contenu identique.</summary>
    private static ResultatScenario Scenario3(
        OptionsApp options, IFournisseurJeton jetons, Func<IFournisseurJeton, HttpMessageHandler>? manip)
    {
        const string nom = "3 · Export puis import sur installation vierge";
        try
        {
            using var source = new PosteJetable(options, jetons, manip);
            var marque = Marque();

            var actifs = new[] { $"{marque} gardé 1", $"{marque} gardé 2" };
            foreach (var titre in actifs) source.Saisir(titre);

            // Un Élément à la corbeille : l'export doit l'emporter AUSSI (§5.7), sinon ce n'est pas
            // une récupération.
            var jete = source.Saisir($"{marque} jeté");
            source.Composition.Saisie.Supprimer(EntiteSynchro.Element, jete);

            // Export : depuis la base locale, sans réseau. C'est la garantie du §5.7 — il doit
            // fonctionner au moment précis où le serveur est inaccessible.
            var archive = Path.Combine(Path.GetTempPath(), $"parite-{Guid.NewGuid():N}.zip");
            using (var fichier = File.Create(archive))
                source.Composition.Export.Exporter(fichier);

            try
            {
                using var vierge = new PosteJetable(options, jetons, manip);
                if (vierge.Composition.Lecture.Actifs().Count != 0)
                    return new(nom, false, "L'installation « vierge » ne l'était pas.");

                using (var fichier = File.OpenRead(archive))
                    vierge.Composition.Import.Importer(fichier);

                var restaures = vierge.Composition.Lecture.Actifs().Select(e => e.Titre).ToHashSet();
                var corbeille = vierge.Composition.Lecture.Corbeille().Select(e => e.Titre).ToHashSet();

                foreach (var titre in actifs)
                    if (!restaures.Contains(titre))
                        return new(nom, false, $"« {titre} » manque après import.");

                return corbeille.Contains($"{marque} jeté")
                    ? new(nom, true, "2 Éléments actifs et 1 à la corbeille restitués à l'identique, sans réseau.")
                    : new(nom, false, "L'Élément de la corbeille n'a pas survécu à l'export : ce n'est pas une récupération complète.");
            }
            finally
            {
                try { File.Delete(archive); } catch { /* fichier temporaire */ }
            }
        }
        catch (Exception ex)
        {
            return new(nom, false, "Échec : " + ex.Message);
        }
    }

    /// <summary>Marque unique : le serveur dev garde les données des exécutions précédentes.</summary>
    private static string Marque() => "PARITE-" + Guid.NewGuid().ToString("N")[..8];

    public static string Rapport(IReadOnlyList<ResultatScenario> resultats)
    {
        var texte = new StringBuilder();
        texte.AppendLine("Scénarios de parité §12 — sur l'application réelle");
        texte.AppendLine(new string('=', 52));
        foreach (var r in resultats)
        {
            texte.AppendLine();
            texte.AppendLine($"[{(r.Reussi ? "OK " : "NON")}] {r.Nom}");
            texte.AppendLine("       " + r.Detail);
        }
        texte.AppendLine();
        texte.AppendLine(resultats.All(r => r.Reussi)
            ? "Les trois scénarios passent."
            : "Au moins un scénario échoue — l'Étape 4 n'est pas finie.");
        return texte.ToString();
    }

    /// <summary>Un poste jetable : sa propre base, son propre cache, détruits à la fin.</summary>
    private sealed class PosteJetable : IDisposable
    {
        private readonly string _dossier;

        public PosteJetable(
            OptionsApp options, IFournisseurJeton jetons, Func<IFournisseurJeton, HttpMessageHandler>? manip)
        {
            _dossier = Path.Combine(Path.GetTempPath(), "dc-parite-" + Guid.NewGuid().ToString("N"));
            Composition = Composition.Creer(options, jetons, _dossier, manip);
        }

        public Composition Composition { get; }

        public Guid Saisir(string titre)
        {
            var element = new Element
            {
                Type = TypeElement.Note,
                Titre = titre,
                Statut = StatutElement.Active,
            };
            var resultat = Composition.Saisie.Enregistrer(element, EntiteSynchro.Element);
            if (!resultat.Reussi)
                throw new InvalidOperationException("Saisie refusée : " + resultat.Erreurs[0].Message);
            return element.Id;
        }

        /// <summary>Compte au SERVEUR les Éléments portant la marque, par un pull depuis zéro.</summary>
        public async Task<int> CompterAuServeur(string marque)
        {
            var compte = 0;
            long curseur = 0;
            while (true)
            {
                var page = await Composition.Api.Tirer(curseur, 500);
                compte += page.Entites.Count(e =>
                    e.Payload.GetRawText().Contains(marque, StringComparison.Ordinal));

                if (!page.Encore || page.Curseur <= curseur) break;
                curseur = page.Curseur;
            }
            return compte;
        }

        public void Dispose()
        {
            Composition.Dispose();
            try { Directory.Delete(_dossier, recursive: true); } catch { /* dossier temporaire */ }
        }
    }
}
