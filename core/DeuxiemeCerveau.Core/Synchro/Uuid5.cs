using System.Security.Cryptography;
using System.Text;

namespace DeuxiemeCerveau.Core.Synchro;

/// <summary>
/// UUID version 5 (RFC 4122, SHA-1) — identifiants déterministes pour les changements induits
/// (cascade de fermeture de projet, D-006) et les entités de réglage. Le déterminisme rend le rejeu
/// d'un lot inoffensif : mêmes entrées → mêmes change_id → idempotence.
/// </summary>
public static class Uuid5
{
    /// <summary>Espace de noms du projet (constant, identique dans les deux apps).</summary>
    public static readonly Guid EspaceDeuxiemeCerveau = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8"); // espace « DNS » RFC 4122

    public static Guid Derive(string nom) => Derive(EspaceDeuxiemeCerveau, nom);

    public static Guid Derive(Guid espace, string nom)
    {
        var octetsEspace = espace.ToByteArray();
        EchangerBoutisme(octetsEspace); // RFC 4122 : octets réseau (big-endian)
        var octetsNom = Encoding.UTF8.GetBytes(nom);

        var tampon = new byte[octetsEspace.Length + octetsNom.Length];
        octetsEspace.CopyTo(tampon, 0);
        octetsNom.CopyTo(tampon, octetsEspace.Length);

        var empreinte = SHA1.HashData(tampon);

        var resultat = new byte[16];
        Array.Copy(empreinte, resultat, 16);
        resultat[6] = (byte)((resultat[6] & 0x0F) | 0x50); // version 5
        resultat[8] = (byte)((resultat[8] & 0x3F) | 0x80); // variante RFC 4122

        EchangerBoutisme(resultat);
        return new Guid(resultat);
    }

    private static void EchangerBoutisme(byte[] guid)
    {
        void Echanger(int i, int j) => (guid[i], guid[j]) = (guid[j], guid[i]);
        Echanger(0, 3);
        Echanger(1, 2);
        Echanger(4, 5);
        Echanger(6, 7);
    }
}
