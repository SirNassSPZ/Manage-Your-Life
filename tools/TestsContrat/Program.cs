using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DeuxiemeCerveau.Api.Contrats;
using DeuxiemeCerveau.Core.Json;
using DeuxiemeCerveau.Core.Modele;
using DeuxiemeCerveau.Core.Synchro;

// Tests de contrat (§8, §12) contre l'API DÉPLOYÉE. Rejouent en HTTP les garanties clés :
// enregistrement d'appareil, recalage du solde, push appliqué, pull par curseur, push idempotent
// (même lot deux fois = même état), projection budgétaire. Sortie non nulle si un test échoue.
//
// Usage : BASE_URL=https://…azurewebsites.net dotnet run --project tools/TestsContrat

var baseUrl = (Environment.GetEnvironmentVariable("BASE_URL") ?? "").TrimEnd('/');
if (string.IsNullOrWhiteSpace(baseUrl))
{
    Console.Error.WriteLine("BASE_URL manquant.");
    return 2;
}

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var options = SerialisationCanonique.Options;
var echecs = 0;

void Verifier(bool condition, string libelle)
{
    Console.WriteLine($"{(condition ? "✓" : "✗")} {libelle}");
    if (!condition)
        echecs++;
}

async Task<(int statut, string corps)> Envoyer(HttpMethod methode, string chemin, string? json = null)
{
    using var requete = new HttpRequestMessage(methode, $"{baseUrl}{chemin}");
    if (json is not null)
        requete.Content = new StringContent(json, Encoding.UTF8, "application/json");
    requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    using var reponse = await http.SendAsync(requete);
    var corps = await reponse.Content.ReadAsStringAsync();
    if ((int)reponse.StatusCode >= 400)
        Console.WriteLine($"  ⚠ {methode} {chemin} → {(int)reponse.StatusCode} : {corps}");
    return ((int)reponse.StatusCode, corps);
}

Console.WriteLine($"— Tests de contrat contre {baseUrl} —");

// 0. Sonde : l'API répond (réveil du serverless compris).
for (var i = 1; ; i++)
{
    try
    {
        var (statut, _) = await Envoyer(HttpMethod.Get, "/api/ping");
        if (statut == 200)
            break;
    }
    catch { /* réveil en cours */ }
    if (i >= 10)
    {
        Console.Error.WriteLine("L'API ne répond pas.");
        return 1;
    }
    await Task.Delay(TimeSpan.FromSeconds(6));
}

// 1. Enregistrement d'appareil (§8).
var (sEnr, cEnr) = await Envoyer(HttpMethod.Post, "/api/devices/register",
    """{"nom":"CI contrat","plateforme":"ci"}""");
Verifier(sEnr == 200, "enregistrement d'appareil : 200");
if (sEnr != 200) { Console.Error.WriteLine("Arrêt : impossible d'enregistrer l'appareil."); return 1; }
var appareilId = SerialisationCanonique.Deserialiser<ReponseEnregistrementAppareil>(cEnr).AppareilId;
Verifier(appareilId != Guid.Empty, "appareil_id renvoyé");

// 2. Recalage du solde de référence (§3.4) : 150 000 centimes au 1er du mois courant.
var maintenant = DateTimeOffset.UtcNow;
var premierDuMois = new DateOnly(maintenant.Year, maintenant.Month, 1);
var recalage = new DemandeRecalageSolde(Guid.NewGuid(), 150000, premierDuMois, maintenant, appareilId);
var (sSolde, _) = await Envoyer(HttpMethod.Put, "/api/settings/solde-reference",
    SerialisationCanonique.Serialiser(recalage));
Verifier(sSolde == 200, "recalage du solde : 200");

// 3. Push d'une facture mensuelle.
var idFacture = Guid.NewGuid();
var facture = new Element
{
    Id = idFacture, Type = TypeElement.Facture, Titre = "Loyer (contrat)",
    DateDebut = new DateTimeOffset(maintenant.Year, maintenant.Month, 5, 7, 0, 0, TimeSpan.Zero),
    Fuseau = "Europe/Paris", Recurrence = "FREQ=MONTHLY", MontantCentimes = 80000, Devise = "EUR",
    Sens = Sens.Sortie, Statut = StatutElement.AVenir,
    DateCreation = maintenant, DateModification = maintenant, AppareilSource = appareilId, Version = 1,
};
var changement = new ChangementPush
{
    ChangeId = Guid.NewGuid(), Entite = EntiteSynchro.Element, EntiteId = idFacture, Version = 1,
    DateModification = maintenant, AppareilId = appareilId,
    Payload = JsonSerializer.SerializeToElement(facture, options),
};
var lot = new LotPush { AppareilId = appareilId, Changements = [changement] };
var jsonLot = SerialisationCanonique.Serialiser(lot);

var (sPush, cPush) = await Envoyer(HttpMethod.Post, "/api/sync/push", jsonLot);
Verifier(sPush == 200, "push : 200");
if (sPush != 200 || string.IsNullOrWhiteSpace(cPush))
{
    Console.Error.WriteLine($"Arrêt : push a renvoyé {sPush} (corps vide: {string.IsNullOrWhiteSpace(cPush)}).");
    Console.WriteLine();
    Console.WriteLine($"✗ {echecs + 1} test(s) de contrat en échec (arrêt anticipé après push).");
    return 1;
}
var push = SerialisationCanonique.Deserialiser<ReponsePushDto>(cPush);
Verifier(push.Resultats[0].Resultat == ResultatChangement.Applique, "push : changement appliqué");
Verifier(!push.Resultats[0].Rejoue, "push : premier envoi non rejoué");
var seq = push.Resultats[0].ServerSeq;

// 4. Pull : la facture est visible.
var (sPull, cPull) = await Envoyer(HttpMethod.Get, "/api/sync/pull?since=0");
Verifier(sPull == 200, "pull : 200");
if (sPull == 200 && !string.IsNullOrWhiteSpace(cPull))
{
    var pull = SerialisationCanonique.Deserialiser<ReponsePullDto>(cPull);
    Verifier(pull.Entites.Any(e => e.Id == idFacture), "pull : la facture est présente");
}

// 5. Idempotence (§6.2.1) : le MÊME lot renvoyé ne s'applique pas deux fois.
var (sRejeu, cRejeu) = await Envoyer(HttpMethod.Post, "/api/sync/push", jsonLot);
Verifier(sRejeu == 200, "push rejoué : 200");
if (sRejeu == 200 && !string.IsNullOrWhiteSpace(cRejeu))
{
    var rejeu = SerialisationCanonique.Deserialiser<ReponsePushDto>(cRejeu);
    Verifier(rejeu.Resultats[0].Rejoue, "idempotence : second envoi marqué rejoué");
    Verifier(rejeu.Resultats[0].ServerSeq == seq, "idempotence : même server_seq (aucun doublon)");
}

// 6. Projection budgétaire (§5.1) : l'ouverture du mois courant = solde de référence.
var (sProj, cProj) = await Envoyer(HttpMethod.Get, "/api/projection/budget?mois=3");
Verifier(sProj == 200, "projection : 200");
if (sProj == 200 && !string.IsNullOrWhiteSpace(cProj))
{
    var projection = SerialisationCanonique.Deserialiser<ReponseProjectionDto>(cProj);
    Verifier(projection.Mois.Count == 3, "projection : 3 mois renvoyés");
    Verifier(projection.Mois[0].OuvertureCentimes == 150000, "projection : ouverture = solde de référence");
}

Console.WriteLine();
Console.WriteLine(echecs == 0
    ? "✓ Tous les tests de contrat en ligne passent."
    : $"✗ {echecs} test(s) de contrat en échec.");
return echecs == 0 ? 0 : 1;
