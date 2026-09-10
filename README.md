# Imaginary – Bild-Konvertierungs- und Verkleinerungs-Tool

**Imaginary** ist eine moderne .NET 8-Lösung zum Konvertieren (Formatwechsel) und Verkleinern (Auflösung und/oder Dateigröße) von Bildern – einzeln oder im Stapelbetrieb (Batch).

Die Anwendung teilt eine gemeinsame, hochperformante SkiaSharp-basierte Kernbibliothek (`Imaginary.Core`) zwischen zwei vollwertigen Benutzeroberflächen:
- **Desktop (WPF)**: Native Windows-Anwendung mit Drag & Drop, Live-Fortschritt und direkter Ordnerverarbeitung.
- **Web (Blazor)**: Docker-fähige ASP.NET Core Blazor Server App für browserbasierten Multi-Upload, ZIP-Download und Container-Betrieb.

---

## 🏗️ Lösungsstruktur

```
Imaginary.sln
├── src/
│   ├── Imaginary.Core/          -> Plattformunabhängige Bibliothek (SkiaSharp, Formaterkennung, Resizer, SizeConstraintSolver)
│   ├── Imaginary.Desktop/       -> Native WPF-App (.NET 8 Windows, CommunityToolkit.Mvvm)
│   └── Imaginary.Web/           -> Blazor Web App (.NET 8, Dockerfile, Docker Compose)
├── tests/
│   └── Imaginary.Core.Tests/    -> xUnit-Testsuite (Format-Erkennung, Solver, Resizer, Batch-Fehlertoleranz)
└── docs/
    └── PLAN.md                  -> Ursprüngliches Architektur- und Planungsdokument
```

---

## 🚀 Features

1. **Format-Konvertierung & Magic-Byte-Erkennung**:
   - Automatische Erkennung des tatsächlichen Dateityps anhand der Magic Bytes (unabhängig von Dateiendungen wie `.jpg` für PNG-Dateien).
   - Unterstützt **JPEG, PNG, WebP, GIF, BMP, TIFF**.
   - Automatische EXIF-Rotationskorrektur (`SKEncodedOrigin`).
2. **Dateigrößen-Begrenzung (SizeConstraintSolver)**:
   - **Verlustbehaftet (JPEG, WebP)**: Binäre Suche der Qualitätsstufe (1–100%), um die Wunschgröße präzise zu treffen.
   - **Verlustfrei (PNG, BMP, TIFF)**: Konfigurierbare Fallback-Strategie:
     - `Auflösung verkleinern (Resize)`: Schrittweise Skalierung, bis die Zielgröße eingehalten wird.
     - `Farbreduktion (Quantisierung)`: Reduktion der Farbpalette (Octree-Quantisierer) für dramatisch kleinere PNG-Dateien.
     - `Nur warnen`: Keine Auflösungsveränderung, Warnmeldung bei Überschreitung.
3. **Auflösungsskalierung (Resizer) & Feinskalierung**:
   - Prozentuale Skalierung (z. B. Pixelmaße halbieren / 50%).
   - Feste Pixelmaße mit optionaler Erhaltung des Seitenverhältnisses.
   - **Einpassen mit Rand (`Pad`)**: Zentriert das Bild und füllt den Hintergrund mit Wunschfarbe (z. B. `#FFFFFF`).
   - **Füllen & Beschneiden (`FillCrop`)**: Zentrierter Zuschnitt auf exakte Zielmaße ohne Verzerrung.
   - **Maximale Kantenlänge (`MaxEdge`)**: Begrenzung der längsten Kante (ideal für Web- und Social-Media-Uploads).
4. **Wasserzeichen-Engine**:
   - Text-Wasserzeichen (einstellbare Schriftart, Größe, Farbe, Schatten und Deckkraft 0–100%).
   - Bild-/Logo-Wasserzeichen mit transparenter Überlagerung.
   - Flexible Positionierung (unten rechts, unten links, oben, zentriert).
5. **Metadaten-Stripping (Datenschutz & Performance)**:
   - Entfernt EXIF-, GPS- und Kameradaten auf Knopfdruck zur Wahrung der Privatsphäre und Reduzierung der Dateigröße.
6. **Profile / Presets**:
   - Vordefinierte Profile für E-Mail, Web-Shop (WebP), Social Media (Quadrat 1080x1080) und Archiv/Druck.
   - Speichern und Löschen eigener benutzerdefinierter Profile in portabler `presets.json`.
7. **Windows Explorer Kontextmenü-Integration**:
   - Schnelles Konvertieren direkt per Rechtsklick auf Bilder oder Ordner ("Mit Imaginary konvertieren...").
   - Keine Administratorrechte erforderlich (Registrierung in `HKCU`).
8. **Hotfolder (Ordnerüberwachung)**:
   - Automatische Überwachung von Verzeichnissen (`FileSystemWatcher`) mit Debounce für Schreibsperren.
   - Neu abgelegte Bilder werden sofort im Hintergrund konvertiert.
9. **Interaktiver Vorher-Nachher-Vergleich**:
   - Integriertes Vergleichsfenster mit flüssigem Split-Slider und Side-by-Side-Ansicht.
10. **Modernes Design & Dark Mode**:
    - Umschaltbarer Dunkelmodus für augenschonendes Arbeiten.
11. **Batch-Verarbeitung & Fehlertoleranz**:
    - Parallele Verarbeitung (`Parallel.ForEachAsync`) mit maximaler Kernauslastung.
    - Fehler werden pro Datei isoliert und dokumentiert, Originaldateien bleiben unberührt.

---

## 📦 Portables Paket erstellen (Weitergabe)

Um Imaginary als autarkes, portables Paket für Windows ohne .NET-Installationspflicht und ohne Administratorrechte zu erstellen:

```powershell
.\build-portable.ps1
```

Ergebnis:
- **`dist/Imaginary-Portable/`**: Autarker Ordner mit `Imaginary.exe`, `presets.json`, `settings.json` und `README-PORTABLE.txt`.
- **`dist/Imaginary-Portable-win-x64.zip`**: Komprimiertes ZIP-Archiv, fertig zum Versenden oder Kopieren auf USB-Sticks.

---

## 💻 Ausführung & Nutzung

### 1. Desktop-Anwendung (WPF)

Start über das Terminal:
```powershell
dotnet run --project src/Imaginary.Desktop
```

Oder portable Version direkt starten:
```powershell
.\dist\Imaginary-Portable\Imaginary.exe
```

---

### 2. Web-Anwendung (Blazor & Docker)

#### Lokal starten:
```powershell
dotnet run --project src/Imaginary.Web
```
Anschließend im Browser unter `http://localhost:5000` öffnen.

#### Mit Docker starten:
```powershell
docker compose up -d --build
```
Anschließend im Browser unter **`http://localhost:8080`** öffnen.

---

### 3. Unit-Tests ausführen

Alle 27 automatisierten Tests für Core, Resizer, Format-Erkennung, Solver, Presets, Wasserzeichen und Batch-Orchestrierung ausführen:
```powershell
dotnet test Imaginary.sln
```
