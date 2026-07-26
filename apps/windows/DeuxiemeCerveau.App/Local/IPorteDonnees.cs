namespace DeuxiemeCerveau.App.Local;

/// <summary>
/// Sérialise les accès à la base <b>par bouffées courtes</b>, pour les opérations qui alternent
/// réseau et base — la synchro (§6.2) et les pièces jointes (§7).
/// <para>
/// <b>Pourquoi une porte plutôt qu'un verrou autour de tout le cycle.</b> <c>BaseLocale</c> détient
/// une <c>SqliteConnection</c> unique et <c>DepotLocal.DansTransaction</c> n'est pas réentrante :
/// la synchro de fond ne doit jamais écrire pendant que l'interface lit. Mais tenir le verrou
/// pendant l'appel réseau bloquerait l'interface le temps de la réponse — jusqu'à ~61 s au réveil
/// du SQL serverless (§10.1). L'interface n'attend <b>jamais</b> le serveur (filet 1, règle 10).
/// </para>
/// <para>
/// La porte se referme donc autour de chaque touche à la base, et reste <b>ouverte pendant le
/// réseau</b>. C'est ce qui permet à l'utilisateur de continuer à saisir pendant une synchro lente.
/// </para>
/// </summary>
public interface IPorteDonnees
{
    T Franchir<T>(Func<T> operation);
    void Franchir(Action operation);
}

/// <summary>
/// Porte toujours ouverte — aucun verrou. C'est le défaut : un test ou un outil qui monte son
/// propre moteur est seul sur sa base et n'a rien à sérialiser. Seule l'application vivante, où
/// l'interface et la synchro de fond coexistent, injecte une vraie porte.
/// </summary>
public sealed class PorteOuverte : IPorteDonnees
{
    public static readonly PorteOuverte Instance = new();

    public T Franchir<T>(Func<T> operation) => operation();

    public void Franchir(Action operation) => operation();
}
