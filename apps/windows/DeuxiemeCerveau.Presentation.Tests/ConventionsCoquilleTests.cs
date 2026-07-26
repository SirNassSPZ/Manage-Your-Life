using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Deux balayages de la coquille WinUI, sur des défauts que <b>rien d'autre ne voit</b>.
/// <para>
/// La coquille vit hors de la solution (D-020) et son job CI se contente de la <b>compiler</b> :
/// tout ce qui relève du câblage — ce bouton appelle-t-il une fonction ? ce convertisseur est-il
/// déclaré ? — n'est vérifié par personne. Le projet l'a payé six fois : trois boutons morts,
/// l'export sans porte (D-024), la saisie sans chemin (D-027), et la vue Finances qui a tourné
/// <b>entièrement non liée pendant des heures</b>. Chaque fois, le code compilait et les tests
/// étaient verts.
/// </para>
/// <para>
/// Ces contrôles existaient en commande à retaper à la main (passation §10). L'histoire du projet
/// montre que le contrôle manuel se rate — le motif de date qui lisait <c>demarrage.log</c> ne
/// couvrait pas l'après-midi et rendait « 0 erreur » sur trente. Un garde-fou ne dépend de la
/// mémoire de personne.
/// </para>
/// <para>
/// <b>Ceci ne contredit pas D-020</b> : ces tests ne compilent ni ne chargent WinUI, ils lisent des
/// fichiers <c>.xaml</c> comme du <b>texte</b>. C'est ce qui leur permet de tourner sur la CI Linux
/// avec le reste de la solution.
/// </para>
/// </summary>
public class ConventionsCoquilleTests
{
    /// <summary>
    /// Clés fournies par WinUI lui-même (ex. <c>AccentButtonStyle</c>), légitimement référencées
    /// sans être déclarées chez nous. <b>Vide aujourd'hui</b> : aucune vue n'en utilise.
    /// <para>
    /// Le jour où l'une devient nécessaire, <b>l'ajouter ici</b> — surtout pas désactiver le test.
    /// Un contrôle qui produit des faux positifs finit ignoré, et l'échappatoire doit exister avant
    /// qu'on en ait besoin.
    /// </para>
    /// </summary>
    private static readonly string[] ClesFourniesParLeFramework = [];

    /// <summary>
    /// Balise ouvrante d'un contrôle cliquable. Les valeurs entre guillemets sont sautées
    /// explicitement : un <c>&gt;</c> peut vivre dans un attribut (<c>Content="=&gt;"</c>), donc
    /// s'arrêter au premier <c>&gt;</c> rencontré serait faux.
    /// </summary>
    private const string BaliseBouton = """<(?:Button|HyperlinkButton)\b(?:[^>"']|"[^"]*"|'[^']*')*>""";

    [Fact]
    public void Toute_ressource_statique_utilisee_est_declaree()
    {
        var globales = ClesDeclarees(File.ReadAllText(CheminJetons()));

        // Un balayage qui ne trouve aucune clé passerait au vert sans rien avoir vérifié.
        Assert.True(globales.Count > 0, $"Aucune clé lue dans {CheminJetons()} — le balayage tourne à vide.");

        var manquantes = new List<string>();

        foreach (var fichier in FichiersBalayes())
        {
            var texte = File.ReadAllText(fichier);
            var locales = ClesDeclarees(texte);

            foreach (Match occurrence in Regex.Matches(texte, @"\{StaticResource (\w+)\}"))
            {
                var cle = occurrence.Groups[1].Value;
                if (locales.Contains(cle) || globales.Contains(cle) || ClesFourniesParLeFramework.Contains(cle))
                    continue;

                manquantes.Add(
                    $"  {Path.GetFileName(fichier)}:{Ligne(texte, occurrence.Index)} — {{StaticResource {cle}}}");
            }
        }

        Assert.True(manquantes.Count == 0,
            "Ressource utilisée sans être déclarée. À l'exécution, WinUI lève « Cannot find a resource "
            + "with the given key », ce qui interrompt Bindings.Initialize() et laisse TOUTE la vue non "
            + "liée — sans rien afficher qui le signale.\n"
            + "Déclarer la clé dans Ressources/Jetons.xaml (si elle est partagée) ou dans les ressources "
            + "de la vue.\n\n"
            + string.Join("\n", manquantes));
    }

    [Fact]
    public void Tout_bouton_porte_une_action()
    {
        var morts = new List<string>();

        foreach (var fichier in FichiersBalayes())
        {
            var texte = File.ReadAllText(fichier);

            foreach (Match balise in Regex.Matches(texte, BaliseBouton))
            {
                // x:Name ne prouve pas le câblage, seulement qu'il PEUT se faire dans le code-behind.
                // Ce test attrape le cas franc — aucune action du tout — qui s'est produit trois fois.
                if (balise.Value.Contains("Command=")
                    || balise.Value.Contains("Click=")
                    || balise.Value.Contains("x:Name="))
                    continue;

                var contenu = Regex.Match(balise.Value, @"Content=""([^""]*)""");
                morts.Add($"  {Path.GetFileName(fichier)}:{Ligne(texte, balise.Index)} — "
                    + (contenu.Success ? $"« {contenu.Groups[1].Value} »" : "(dans un gabarit)"));
            }
        }

        Assert.True(morts.Count == 0,
            "Bouton sans Command, Click ni x:Name : il ne peut rien déclencher. Le code compile et les "
            + "tests passent quand même — c'est exactement ainsi que « Nouvel élément », « Recaler le "
            + "solde » et « Ajouter » sont restés inertes jusqu'à ce que l'utilisateur les signale.\n\n"
            + string.Join("\n", morts));
    }

    /// <summary>
    /// Les fichiers balayés : les vues, plus la fenêtre principale. <c>App.xaml</c> est hors du lot —
    /// il ne déclare aucune clé et ne porte aucun bouton.
    /// </summary>
    private static IReadOnlyList<string> FichiersBalayes()
    {
        var vues = Path.Combine(DossierCoquille(), "Vues");
        Assert.True(Directory.Exists(vues), $"Dossier des vues introuvable : {vues}");

        var fichiers = Directory.GetFiles(vues, "*.xaml").OrderBy(f => f).ToList();
        Assert.True(fichiers.Count > 0, $"Aucun .xaml dans {vues} — le balayage tourne à vide.");

        var fenetre = Path.Combine(DossierCoquille(), "FenetrePrincipale.xaml");
        if (File.Exists(fenetre))
            fichiers.Add(fenetre);

        return fichiers;
    }

    private static string CheminJetons()
    {
        var jetons = Path.Combine(DossierCoquille(), "Ressources", "Jetons.xaml");
        Assert.True(File.Exists(jetons), $"Fichier de jetons introuvable : {jetons}");
        return jetons;
    }

    /// <summary>
    /// Le dossier de la coquille, atteint depuis l'emplacement de CE fichier source.
    /// <para>
    /// <c>[CallerFilePath]</c> plutôt que <c>AppContext.BaseDirectory</c> : celui-ci dépend du
    /// répertoire de travail de <c>dotnet test</c>, qui n'est pas le même selon qu'on lance la
    /// solution entière ou ce seul projet. Le projet de tests ne référence délibérément pas
    /// <c>DeuxiemeCerveau.Windows</c> — il en lit les fichiers, il n'en compile rien.
    /// </para>
    /// </summary>
    private static string DossierCoquille([CallerFilePath] string cheminDeCeFichier = "")
    {
        var projetTests = Path.GetDirectoryName(cheminDeCeFichier)!;   // …/apps/windows/…Presentation.Tests
        var windows = Path.GetFullPath(Path.Combine(projetTests, "..")); // …/apps/windows
        var coquille = Path.Combine(windows, "DeuxiemeCerveau.Windows");

        Assert.True(Directory.Exists(coquille), $"Dossier de la coquille introuvable : {coquille}");
        return coquille;
    }

    private static HashSet<string> ClesDeclarees(string xaml) =>
        Regex.Matches(xaml, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

    private static int Ligne(string texte, int index) =>
        texte.Take(index).Count(c => c == '\n') + 1;
}
