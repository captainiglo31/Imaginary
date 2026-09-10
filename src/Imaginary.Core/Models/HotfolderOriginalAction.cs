namespace Imaginary.Core.Models;

public enum HotfolderOriginalAction
{
    /// <summary>
    /// Originaldatei bleibt unverändert im Eingangsordner.
    /// </summary>
    Keep = 0,

    /// <summary>
    /// Originaldatei wird nach erfolgreicher Verarbeitung in einen Unterordner (Standard: 'Originale') verschoben.
    /// Verhindert erneutes Einlesen und hält den Eingangsordner sauber.
    /// </summary>
    MoveToSubfolder = 1,

    /// <summary>
    /// Originaldatei wird nach erfolgreicher Verarbeitung endgültig gelöscht.
    /// </summary>
    Delete = 2
}
