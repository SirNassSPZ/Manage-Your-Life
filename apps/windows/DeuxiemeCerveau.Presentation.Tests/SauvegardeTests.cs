using DeuxiemeCerveau.Presentation.VueModeles;

namespace DeuxiemeCerveau.Presentation.Tests;

/// <summary>
/// Export / import local (§5.7). Le sélecteur de fichier est remplacé par des flux en mémoire :
/// c'est la seule partie qui exige Windows, tout le reste est du vrai code de production.
/// </summary>
public class SauvegardeTests
{
    /// <summary>Sélecteur de test : garde l'archive en mémoire, ou renonce comme le ferait l'utilisateur.</summary>
    private sealed class SelecteurEnMemoire(byte[]? aLire = null) : ISelecteurFichier
    {
        private readonly MemoryStream _ecrit = new();

        public bool Renonce { get; set; }
        public string? NomDemande { get; private set; }
        public byte[] Ecrit => _ecrit.ToArray();

        public Task<Stream?> PourEcrire(string nomPropose)
        {
            NomDemande = nomPropose;
            // Le modèle de vue referme le flux : sans ça, ToArray() ne verrait rien.
            return Task.FromResult<Stream?>(Renonce ? null : new FluxSansFermeture(_ecrit));
        }

        public Task<Stream?> PourLire() =>
            Task.FromResult<Stream?>(Renonce || aLire is null ? null : new MemoryStream(aLire));
    }

    /// <summary>Laisse le modèle de vue faire son <c>using</c> sans perdre le tampon sous-jacent.</summary>
    private sealed class FluxSansFermeture(Stream interne) : Stream
    {
        public override bool CanRead => interne.CanRead;
        public override bool CanSeek => interne.CanSeek;
        public override bool CanWrite => interne.CanWrite;
        public override long Length => interne.Length;
        public override long Position { get => interne.Position; set => interne.Position = value; }
        public override void Flush() => interne.Flush();
        public override int Read(byte[] b, int o, int c) => interne.Read(b, o, c);
        public override long Seek(long o, SeekOrigin s) => interne.Seek(o, s);
        public override void SetLength(long v) => interne.SetLength(v);
        public override void Write(byte[] b, int o, int c) => interne.Write(b, o, c);
        protected override void Dispose(bool disposing) { /* le test garde la main */ }
    }

    [Fact]
    public async Task Un_export_produit_une_archive_lisible_sans_l_application()
    {
        using var f = new FabriquePresentation();
        f.AjouterSortie("Loyer", DateTimeOffset.Now, 84_000);

        var selecteur = new SelecteurEnMemoire();
        var modele = new VueModeleSauvegarde(f.Composition, selecteur);

        await modele.ExporterCommand.ExecuteAsync(null);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(selecteur.Ecrit));
        var donnees = zip.GetEntry("donnees.json");
        Assert.NotNull(donnees);
        using var lecteur = new StreamReader(donnees!.Open());
        Assert.Contains("Loyer", lecteur.ReadToEnd());
    }

    [Fact]
    public async Task Renoncer_au_choix_du_fichier_ne_dit_rien()
    {
        // Fermer la boîte de dialogue n'est pas un échec : ce n'est pas un message d'erreur.
        using var f = new FabriquePresentation();
        var modele = new VueModeleSauvegarde(f.Composition, new SelecteurEnMemoire { Renonce = true });

        await modele.ExporterCommand.ExecuteAsync(null);

        Assert.Null(modele.Message);
    }

    [Fact]
    public void Le_nom_propose_est_date_donc_triable()
    {
        var nom = VueModeleSauvegarde.NomPropose(new DateTimeOffset(new DateTime(2026, 7, 26), TimeSpan.Zero));

        Assert.Equal("deuxieme-cerveau-2026-07-26.zip", nom);
    }

    [Fact]
    public async Task Un_import_sur_une_installation_non_vierge_est_refuse()
    {
        // §5.7 : la V1 ne réimporte que dans une installation vide. ServiceImport écrase entité par
        // entité — lancé sur des données existantes, il produirait un mélange indéfaisable.
        using var f = new FabriquePresentation();
        f.AjouterSortie("Déjà là", DateTimeOffset.Now);

        var modele = new VueModeleSauvegarde(f.Composition, new SelecteurEnMemoire([1, 2, 3]));

        await modele.ImporterCommand.ExecuteAsync(null);

        Assert.NotNull(modele.Message);
        Assert.Contains("vierge", modele.Message);
    }

    [Fact]
    public async Task Une_corbeille_non_vide_compte_comme_des_donnees()
    {
        // Un poste qui n'aurait que des Éléments supprimés n'est pas vierge : un import y écraserait
        // une corbeille encore récupérable (filet 2).
        using var f = new FabriquePresentation();
        var id = f.AjouterSortie("Supprimé", DateTimeOffset.Now);
        f.Composition.Acces.Ecrire(() =>
            f.Composition.Saisie.Supprimer(Core.Synchro.EntiteSynchro.Element, id));

        var modele = new VueModeleSauvegarde(f.Composition, new SelecteurEnMemoire([1, 2, 3]));
        Assert.False(modele.EstVierge());

        await modele.ImporterCommand.ExecuteAsync(null);

        Assert.Contains("vierge", modele.Message);
    }

    [Fact]
    public async Task Export_puis_import_sur_un_poste_vierge_rend_le_meme_contenu()
    {
        using var source = new FabriquePresentation();
        source.AjouterSortie("Loyer", DateTimeOffset.Now, 84_000);
        source.AjouterCategorie("Maison");

        var selecteur = new SelecteurEnMemoire();
        await new VueModeleSauvegarde(source.Composition, selecteur).ExporterCommand.ExecuteAsync(null);

        using var vierge = new FabriquePresentation();
        var modele = new VueModeleSauvegarde(vierge.Composition, new SelecteurEnMemoire(selecteur.Ecrit));
        Assert.True(modele.EstVierge());

        var recharge = false;
        modele.ApresImport = () => recharge = true;
        await modele.ImporterCommand.ExecuteAsync(null);

        Assert.Equal("Archive restaurée.", modele.Message);
        Assert.True(recharge);

        var titres = vierge.Composition.Acces.Lire(() => vierge.Composition.Lecture.Actifs())
            .Select(e => e.Titre).ToList();
        Assert.Contains("Loyer", titres);
        Assert.Contains("Maison",
            vierge.Composition.Acces.Lire(() => vierge.Composition.Lecture.Categories()).Select(c => c.Nom));
    }

    [Fact]
    public void Le_rappel_mensuel_est_desactivable_et_survit_au_redemarrage()
    {
        // §5.7 : « notification locale, désactivable ». Le drapeau vit dans le journal des rappels,
        // hors du schéma synchronisé — notifier est une affaire d'appareil (D-023).
        using var f = new FabriquePresentation();

        var modele = new VueModeleSauvegarde(f.Composition, new SelecteurEnMemoire());
        Assert.True(modele.RappelMensuel);

        modele.RappelMensuel = false;

        Assert.False(JournalRappels.Ouvrir(f.Dossier).RappelExportActif);
        Assert.False(new VueModeleSauvegarde(f.Composition, new SelecteurEnMemoire()).RappelMensuel);
    }
}
