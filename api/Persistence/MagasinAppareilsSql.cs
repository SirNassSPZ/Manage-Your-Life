using System.Data.Common;
using System.Globalization;
using DeuxiemeCerveau.Core.Synchro;
using DeuxiemeCerveau.Core.Temps;

namespace DeuxiemeCerveau.Api.Persistence;

/// <summary>Registre d'appareils sur base relationnelle (table devices §9, adaptateur).</summary>
public sealed class MagasinAppareilsSql(Func<DbConnection> fabriqueConnexion, IHorloge horloge) : IMagasinAppareils
{
    public Appareil Enregistrer(string nom, string plateforme)
    {
        var appareil = new Appareil(Guid.NewGuid(), nom, plateforme, horloge.MaintenantUtc);
        using var connexion = fabriqueConnexion();
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText =
            "INSERT INTO devices (id, nom, plateforme, date_enregistrement) VALUES (@id, @nom, @plateforme, @date)";
        Param(cmd, "@id", appareil.Id);
        Param(cmd, "@nom", appareil.Nom);
        Param(cmd, "@plateforme", appareil.Plateforme);
        Param(cmd, "@date", appareil.DateEnregistrement.UtcDateTime);
        cmd.ExecuteNonQuery();
        return appareil;
    }

    public Appareil? Obtenir(Guid id)
    {
        using var connexion = fabriqueConnexion();
        connexion.Open();
        using var cmd = connexion.CreateCommand();
        cmd.CommandText = "SELECT nom, plateforme, date_enregistrement FROM devices WHERE id = @id";
        Param(cmd, "@id", id);
        using var lecteur = cmd.ExecuteReader();
        if (!lecteur.Read())
            return null;
        var date = lecteur.GetValue(2) switch
        {
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
            string t => DateTimeOffset.Parse(t, CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal),
            _ => default,
        };
        return new Appareil(id, lecteur.GetString(0), lecteur.GetString(1), date);
    }

    private static void Param(DbCommand cmd, string nom, object valeur)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nom;
        p.Value = valeur;
        cmd.Parameters.Add(p);
    }
}
