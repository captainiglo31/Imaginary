# Imaginary – Bild-Konvertierungs- und Verkleinerungs-Tool

Planungsdokument. Stand: 2026-09-10.

## 1. Ziel

Eine .NET-Anwendung, die Bilder **konvertiert** (Formatwechsel) und **verkleinert** (Auflösung
und/oder Dateigröße), einzeln oder als Batch. Läuft als natives Windows-Programm **und** als
Docker-gehostete Webanwendung, auf derselben Kernlogik.

Typische Anwendungsfälle (aus der Anforderung):

1. 20 gemischte Bilder (`.jpg/.jpeg/.png/.gif/...`) → alle nach `.jpg`, zusätzlich harte Grenze
   "keine Datei größer als 0,5 MB".
2. Ein einzelnes Bild `.jpg` → `.png`, Dateigröße irrelevant.
3. Ganzer Ordner → alle Bilder in Pixel-Auflösung halbieren, Dateiendung/Format bleibt gleich.

## 2. Geklärte Grundsatzentscheidungen

| Thema | Entscheidung |
|---|---|
| Architektur | Gemeinsame Core-Bibliothek + **zwei UIs**: WPF-Desktop (Windows) und Blazor-Web (Docker-fähig) |
| Desktop-Framework | **WPF** (explizit gegen WinUI 3 und Avalonia abgewogen und bestätigt, siehe Begründung unten) |
| Bild-Bibliothek | **SkiaSharp** (MIT-Lizenz, uneingeschränkt kommerziell nutzbar) |
| Umgang mit Zielgröße bei verlustfreien Formaten (z. B. PNG) | Nutzer wählt **pro Auftrag** eine Fallback-Strategie: Verkleinern (Resize) / Farbreduktion (Quantisierung) / nur warnen |
| Umgang mit Ausgabedateien | Ergebnisse landen **immer in neuen Dateien/Zielordner**, Originale werden nie überschrieben |
| Erwarteter Batch-Umfang | Einige bis mehrere hundert Bilder, synchrone Verarbeitung mit Fortschrittsanzeige (keine Job-Queue nötig) |

### 2.1 Begründung: WPF statt WinUI 3 / Avalonia / MAUI

Bewusst abgewogen und bestätigt:

- **Reifegrad & Stabilität**: WPF ist seit 2006 etabliert, auf .NET 8 vollständig unterstützt,
  extrem gut dokumentiert und in den Kernszenarien (Datenbindung, Drag & Drop, Dialoge) sehr
  ausgereift – wichtiger für ein Utility-Tool als moderne Optik.
- **Einfaches Packaging**: Auslieferung als self-contained EXE per `dotnet publish`, kein
  MSIX-/Store-Zwang.
- **Nur Windows gefordert**: Da die Cross-Plattform-Anforderung bereits über die Docker/Web-Variante
  abgedeckt ist, entfällt der Hauptvorteil von Cross-Plattform-Frameworks (MAUI, Avalonia) für die
  Desktop-Seite.
- **Verworfene Alternativen**: WinUI 3 (moderneres Fluent-Design, aber mehr Packaging-Aufwand und
  historisch mehr Kinderkrankheiten) und Avalonia (modern, cross-platform-fähig, aber kein
  offizielles Microsoft-Framework) wurden geprüft und zugunsten von WPF verworfen.

## 3. Offene Annahmen (bitte bei Bedarf korrigieren)

- **.NET-Version:** .NET 8 (LTS) als Zielplattform für alle Projekte.
- **Kein Login/Auth** für die Web-Variante – als lokales/Self-Hosted-Tool ohne Mehrbenutzer-Trennung.
  Falls das Tool öffentlich erreichbar gehostet werden soll, muss das nachgerüstet werden.
- **Unterstützte Formate:** JPEG, PNG, GIF (nur erstes Frame bei Konvertierung, s. Einschränkungen),
  BMP, WebP, TIFF – abgedeckt durch SkiaSharp-Codecs.
- **Animierte GIFs:** SkiaSharp behandelt GIF beim Decodieren/Encodieren nicht animationserhaltend.
  Eine Konvertierung eines animierten GIFs nimmt nur das erste Frame. Das wird im Ergebnis-Report
  als Warnung ausgewiesen. Volle Animations-Erhaltung wäre ein separates Feature (ggf. später
  Magick.NET nur für diesen Sonderfall nachrüsten).
- **Farbprofile/EXIF:** Metadaten (EXIF-Rotation, Farbprofil) werden beim Konvertieren nach
  Möglichkeit übernommen bzw. die EXIF-Rotation vor dem Speichern angewendet, damit Bilder nicht
  gedreht erscheinen. Andere Metadaten (GPS, Kamera-Infos) werden nicht speziell behandelt.

## 4. Lösungsstruktur

```
Imaginary.sln
src/
  Imaginary.Core/              -> Klassenbibliothek, keine UI-Abhängigkeit
  Imaginary.Desktop/            -> WPF-App (net8.0-windows), nutzt Core
  Imaginary.Web/                 -> ASP.NET Core Blazor Web App (net8.0), nutzt Core, Dockerfile
tests/
  Imaginary.Core.Tests/          -> xUnit-Tests für die Kernlogik
docs/
  PLAN.md                        -> dieses Dokument
```

### 4.1 Imaginary.Core

Enthält die gesamte Bild- und Batch-Logik, damit Desktop und Web nicht duplizieren.

**Modelle**
- `ConversionOptions` – Zielformat (oder "beibehalten"), Zielgröße (optional, in KB/MB),
  Resize-Modus (`None`, `Percentage`, `AbsolutePixels`), Fallback-Strategie bei Zielgröße
  (`ResizeDown`, `Quantize`, `WarnOnly`).
- `ImageJobInput` – Quelle (Datei- oder Stream-Referenz, erkannter Ursprungsformat via Magic
  Bytes, nicht nur Dateiendung).
- `ImageJobResult` – Zielpfad, finale Dateigröße, finale Pixelmaße, angewandte Strategie,
  Warnungen/Fehler.
- `BatchResult` – Liste aller `ImageJobResult`, Zusammenfassung (Anzahl OK/Fehler/Warnungen).

**Services**
- `IImageFormatDetector` – erkennt tatsächliches Format anhand Byte-Signatur (robust gegen falsche
  Dateiendungen, siehe Beispiel 1: "wild durcheinander").
- `IImageConverter` – lädt Bild via SkiaSharp (`SKCodec`/`SKBitmap`), wendet EXIF-Rotation an,
  encodiert im Zielformat.
- `IImageResizer` – Resize nach Prozent oder absoluten Pixelmaßen, Seitenverhältnis wahren.
- `ISizeConstraintSolver` – Kernstück für "Datei darf nicht größer als X sein":
  - Verlustbehaftete Formate (JPEG/WebP): iterative Qualitätsreduktion (binäre Suche zwischen
    Qualität 1–100), bis Zielgröße erreicht oder Qualitäts-Minimum erschöpft.
  - Verlustfreie Formate (PNG/BMP/TIFF): je nach gewählter Fallback-Strategie
    - `ResizeDown`: Auflösung schrittweise reduzieren, bis Ziel erreicht.
    - `Quantize`: Farbpalette reduzieren (eigene einfache Quantisierung, z. B. Octree- oder
      Median-Cut-Algorithmus, da SkiaSharp keine eingebaute Palettenreduktion liefert).
    - `WarnOnly`: nur maximale native Kompression versuchen, sonst Ergebnis mit Warnung
      "Zielgröße nicht erreichbar" markieren.
- `IBatchProcessor` – orchestriert mehrere `ImageJobInput`, verarbeitet mit begrenztem Grad an
  Parallelität (z. B. `Environment.ProcessorCount`), meldet Fortschritt über `IProgress<T>`,
  fängt Fehler pro Datei ab (ein kaputtes Bild bricht den Batch nicht ab).
- `IOutputPathResolver` – erzeugt Zielpfade in einem neuen Ausgabeordner, verhindert Überschreiben
  von Originalen, löst Namenskollisionen auf (z. B. Suffix `_1`, `_2`).

### 4.2 Imaginary.Desktop (WPF)

- MVVM-Pattern (`CommunityToolkit.Mvvm`).
- Hauptfenster:
  - Datei-/Ordnerauswahl inkl. Drag & Drop.
  - Optionen-Panel: Zielformat, Zielgröße (Checkbox + Wert + Einheit), Resize-Modus
    (Prozent/Pixel/keine), Fallback-Strategie-Auswahl (nur relevant/aktivierbar wenn Zielformat
    verlustfrei ist).
  - Zielordner-Auswahl (Default: Unterordner `converted` neben den Quelldateien).
  - Ergebnisliste mit Live-Fortschritt: Dateiname, Status, finale Größe, Warnungen.
- Paketierung: `dotnet publish` als self-contained oder framework-dependent EXE; optional später
  MSIX-Installer (nicht Teil der ersten Phase).

### 4.3 Imaginary.Web (Blazor)

- Blazor Web App (Server-Interaktivität, da Dateiverarbeitung serverseitig mit Streams passiert).
- Seite "Konvertieren":
  - Mehrfach-Datei-Upload (Drag & Drop + Dateiauswahl-Dialog).
  - Gleiches Optionen-Panel wie Desktop (gemeinsame Razor-Komponente wo möglich).
  - Fortschrittsanzeige via Blazor-Server-Events (SignalR ist bereits eingebaut).
  - Ergebnis: Liste mit Einzel-Download je Datei sowie "Alle als ZIP herunterladen".
- Kein persistenter Storage nötig: Verarbeitung im Arbeitsspeicher/Temp-Verzeichnis, Downloads
  laufen über temporäre Dateien, die nach Ablauf/Verlassen der Session aufgeräumt werden.
- `Dockerfile` (Multi-Stage: SDK-Image zum Build, ASP.NET-Runtime-Image für den Betrieb),
  `docker-compose.yml` optional für einfachen Start (`docker compose up`).

### 4.4 Imaginary.Core.Tests

- xUnit + kleine Test-Bild-Fixtures (wenige KB, eingecheckt).
- Schwerpunkte:
  - Formaterkennung unabhängig von (absichtlich falscher) Dateiendung.
  - `ISizeConstraintSolver`: Ergebnisdatei liegt nach Verarbeitung nachweislich unter Zielgröße
    (bei JPEG/WebP) bzw. Fallback-Strategie wird korrekt angewendet (bei PNG).
  - Resize erhält Seitenverhältnis korrekt (Beispiel 3: "Größe halbieren").
  - Batch mit einer absichtlich kaputten Datei: Restliche Dateien werden trotzdem verarbeitet,
    Fehler wird korrekt reportet.

## 5. Umsetzungsphasen

1. **Phase 1 – Core**: Solution-Grundgerüst, `Imaginary.Core` mit allen Services, Unit-Tests grün.
2. **Phase 2 – Desktop**: WPF-UI auf Core aufsetzen, manuelle Tests mit allen drei Beispiel-Szenarien.
3. **Phase 3 – Web**: Blazor-UI auf Core aufsetzen, Dockerfile, lokaler Docker-Test.
4. **Phase 4 – Politur**: Fehlerbehandlung/Logging vereinheitlichen (z. B. Serilog), UI-Feinschliff,
   README mit Bedienungsanleitung für beide Varianten.

## 6. Bekannte Einschränkungen

- Animierte GIFs verlieren beim Konvertieren alle Frames bis auf das erste (siehe Abschnitt 3).
- Farbreduktion (Quantisierung) für PNG ist ein Eigenbau, da SkiaSharp das nicht nativ mitbringt –
  Qualität/Geschwindigkeit ist ein guter, aber kein High-End-Quantisierer (kein Vergleich zu z. B.
  pngquant). Bei Bedarf könnte später ein natives Tool wie `pngquant` per Prozessaufruf
  eingebunden werden.
- Web-Variante ohne Auth ist nur für vertrauenswürdige/lokale Netzwerke gedacht.
