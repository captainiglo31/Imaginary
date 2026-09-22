namespace Imaginary.Core.Models;

public enum DefaultFileOpenAction
{
    /// <summary>
    /// Datei in die Konvertierungsliste laden (Standard).
    /// </summary>
    AddToBatchList = 0,

    /// <summary>
    /// Direkt im Profi-Modus (RAW-Entwickler & Color-Grading) öffnen.
    /// </summary>
    OpenInProfiMode = 1,

    /// <summary>
    /// Direkt in Paint / Quick-Editor öffnen.
    /// </summary>
    OpenInImageEditor = 2
}
