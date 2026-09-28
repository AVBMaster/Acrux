namespace UpBrowser.PageContract;

/// <summary>
/// Decides whether a page host is keeping up, from nothing but the age of its last
/// heartbeat and whether the shell is waiting on it. A host that holds the pipe open
/// while its own loop is wedged still degrades Healthy → Suspect → Unresponsive,
/// because a wedged loop simply stops beating.
/// </summary>
public static class ResponsivenessEvaluator
{
    /// <summary>Silence that makes a host suspect while the shell is waiting on it.</summary>
    public const int SuspectAfterMs = 750;

    /// <summary>Silence that makes a host unresponsive while the shell is waiting on it.</summary>
    public const int UnresponsiveAfterMs = 3000;

    /// <summary>
    /// <paramref name="silentMs"/> is the time since the last heartbeat;
    /// <paramref name="awaitingPage"/> is true while the shell has input, a navigation or a
    /// load outstanding. A host nobody is waiting on is reported Healthy even if silent, so a
    /// background tab running a long script cannot steal the front page with a modal.
    /// </summary>
    public static PageResponsiveness Evaluate(long silentMs, bool awaitingPage, bool isDead)
    {
        if (isDead) return PageResponsiveness.Dead;
        if (!awaitingPage) return PageResponsiveness.Healthy;
        if (silentMs >= UnresponsiveAfterMs) return PageResponsiveness.Unresponsive;
        if (silentMs >= SuspectAfterMs) return PageResponsiveness.Suspect;
        return PageResponsiveness.Healthy;
    }
}
