## 🚀 Was ist neu in Version 2.3.0 (Profi-Modus RAW-Entwickler, Windows-Integration & Feinschliff):

### 🎛️ Neuer Profi-Modus (Kamera-RAW-Entwickler & Color-Grading Studio)
- **Umfassende RAW-Unterstützung:**
  - Direkte Erkennung und Dekodierung aller gängigen Kamera-RAW-Formate: Canon (`.cr2`, `.cr3`), Nikon (`.nef`), Sony (`.arw`), Adobe DNG (`.dng`), Olympus (`.orf`), Panasonic (`.rw2`), Pentax (`.pef`) und Fujifilm (`.raf`).
  - Funktioniert natürlich auch mit allen Standard-Formaten: JPEG, PNG, WebP, AVIF, TIFF und BMP.
- **60 FPS SkiaSharp Multi-Core LUT-Engine:**
  - Sämtliche Tonwert- und Farbanpassungen werden in vorberechneten 1D/3D-Lookup-Tabellen (LUTs) zusammengefasst.
  - Transformationen auf 1080p-Arbeitskopien benötigen nur 2–5 ms. Butterweiche Echtzeit-Vorschau beim Ziehen an Schiebereglern ohne Ruckeln.
  - Beim Speichern und Exportieren wird das Grading verlustfrei auf die volle native Sensorauflösung angewendet.
- **Echtzeit-Histogramm & Farbmischung:**
  - Echtes 256-Kanal RGB- und Luminanz-Histogramm mit additiver Farbmischung (Rot, Grün, Blau, Gelb, Cyan, Magenta, Weiß).
  - Dynamische Clipping-Indikatoren warnen visuell bei absaufenden Tiefen (Blau) oder ausfressenden Lichtern (Rot) mit genauer Pixel- und Prozentangabe.
- **Vollständige Werkzeugpalette:**
  - **Licht & Belichtung:** Belichtung (-3 bis +3 EV), Kontrast, Lichter/Highlights, Tiefen/Shadows, Weiß- und Schwarztöne.
  - **Farbe & Weißabgleich:** Farbtemperatur, Tönung, Dynamik (Vibrance mit automatischem Hautton-Schutz), Sättigung (-100 bis +100) und Farbton-Verschiebung (Hue Shift).
  - **Schärfe & Details:** Klarheit (Clarity), Dunst entfernen (Dehaze), Unsharp-Mask Schärfung und sanfte Rauschreduzierung.
  - **Color-Grading / Split-Toning:** Getrennte Farbton- und Sättigungssteuerung für Schatten und Lichter inklusive Balance-Regler.
  - **Effekte & Vignette:** Vignettierung mit weichem Auslauf sowie filmisches Korn (Grain) mit Luminanz-Maskierung.
  - **Tonwertkurve:** Parametrische 4-Zonen-Kurve für Tiefen, dunkle Mitten, helle Mitten und Lichter.
- **Workflow & Bedienkomfort:**
  - **10 professionelle 1-Klick-Looks (Presets):** Vivid Pop, Teal & Orange, Moody Urban, Golden Hour, Cold Fade, Vintage 35mm, Dramatisch B&W, HDR Landschaft, Porträt Sanft und Neutral.
  - **Vorher/Nachher-Vergleich:** Sofortige Vorschau des Originals per Taste `\` oder Klick auf `👁️ Vorher` sowie interaktiver Split-Vergleich (`🪟`) mit verschiebbarer Trennlinie.
  - **Rezept-Zwischenablage:** Einstellungen kopieren & einfügen (`Strg+C` / `Strg+V`) für schnelle Serienbearbeitung.
  - **Reset:** Doppelklick auf jeden Slider oder Klick auf die Beschriftung setzt den Wert auf `0` zurück; einzelne Sektionen lassen sich separat per `↺` zurücksetzen.
  - **Pixel-Inspektor:** Lupe mit RGB-, Hex- und Koordinatenanzeige unter dem Cursor.

---

### 🗂️ Windows Explorer Multi-Verb Kontextmenü & Standard-App Registrierung
- **Erweitertes Windows-Explorer-Kontextmenü:**
  - Rechtsklick auf Bilddateien bietet nun 3 dedizierte Aktionen:
    - `⚡ Mit Imaginary konvertieren` (Batch-Konvertierung)
    - `✏️ Mit Imaginary bearbeiten` (Paint / Quick-Edit Studio)
    - `🎛️ Im Profi-Modus (RAW) entwickeln` (RAW-Entwickler & Color-Grading)
  - Ordner-Kontextmenü: `⚡ Ordner mit Imaginary konvertieren`.
  - Unterstützt sowohl den Kaltstart als auch die nahtlose Übergabe an bereits geöffnete Instanzen via Single-Instance IPC.
- **Windows „Standard-App“ (Default App) & Dateizuordnungen:**
  - Offizielle Windows-Registrierung für 18 Dateiformate nach Microsoft-Spezifikation (`ProgID`, `OpenWithProgids`, `Capabilities`).
  - Imaginary erscheint nun offiziell in Windows unter *„Öffnen mit...“* und in der Liste der Standard-Bildbetrachter.
  - Direkter 1-Klick-Button in den Einstellungen unter *System & Hintergrundbetrieb* zum Öffnen der Windows Standard-Apps (`ms-settings:defaultapps`).
  - Konfigurierbare Standard-Aktion beim Doppelklick auf Bilder (Konvertier-Studio, Profi-Modus oder Quick-Edit).

---

### 🎨 Bugfixes & Detailverbesserungen im Bild-Editor
- **Zoom im normalen Bearbeiten-Fenster:**
  - Stufenloses Zoomen mit dem Mausrad direkt auf den Mauszeiger (`PreviewMouseWheel`).
  - Shortcuts: `Strg + Plus` (Vergrößern), `Strg + Minus` (Verkleinern), `Strg + 0` (Fenster einpassen), `Strg + 1` (100% 1:1).
  - Panning: Verschieben des Bildes mit gedrückter **rechter Maustaste**, mittlerer Maustaste oder `Leertaste + linke Maustaste`.
  - Intelligentes Auto-Fit beim ersten Öffnen großer Bilder.
- **Hintergrund-Entfernen Seitenleiste:**
  - Reaktivierung behoben: Nach dem Schließen des Seitenpanels über `✕` lässt sich das Freistellen-Menü nun problemlos wieder öffnen.
  - Neuer Umschalt-Button `🪄 Freistellen` in der Kopfleiste.
- **DataGrid Zeilenauswahl:**
  - Rechtsklick auf eine Datei in der Tabellenansicht selektiert diese Datei nun sofort und zuverlässig, sodass Kontextaktionen immer exakt das gewünschte Bild bearbeiten.

---

## 🚀 Was ist neu in Version 2.2.5 (Rollback-Resilienz & Windows-Snipping Integration):

### 🛡️ Crash-Loop & Rollback-Schleife vollständig behoben
- **Rollback-Garantie:** Bei einem Rollback auf eine vorherige Version wird der Crash-Tracker (`StartupHealthTracker`) nun sauber zurückgesetzt, sodass die Vorgängerversion sofort gesund startet.
- **Parameter `--after-rollback`:** Wird beim Start zuverlässig erkannt und verhindert das Wiederauftreten der Notfall-Wiederherstellung.
- **Neuer Button „🚀 Trotzdem normal starten“:** Im Notfall-Dialog (`CrashRecoveryWindow`) kann der Anwender die Warnung jederzeit manuell übersteuern und normal ins Programm starten.
- **Keine Falsch-Positiv-Schleifen:** Das Öffnen oder Schließen des Notfallfensters hinterlässt keinen blockierenden Absturzzustand mehr in `startup_health.json`.

### 📸 Natives Windows-Snipping (`Win + Shift + S`) & Zwischenablage
- **100% natives Windows-Erlebnis:** Das alte benutzerdefinierte Screenshot-Overlay wurde entfernt und nahtlos durch das Windows Snipping Tool (`ms-screenclip:` / `Win + Shift + S`) ersetzt.
- **Echtzeit-Erkennung der Zwischenablage:**
  - Imaginary registriert einen Windows-Clipboard-Listener (`WM_CLIPBOARDUPDATE`). Sobald ein Screenshot mit `Win + Shift + S`, der Druck-Taste oder Snipping Tool erstellt wird, erkennt Imaginary diesen automatisch.
  - **In-App Toast-Banner:** Zeigt direkt im Fenster eine dezente Meldung mit Schnellzugriff: *„🎨 Im Editor bearbeiten“*, *„➕ Zu Studio hinzufügen“* oder *„✕ Schließen“*.
  - **Windows-Benachrichtigung:** Bei minimiertem Fenster oder im Hintergrundbetrieb erscheint ein Info-Balloon – ein Klick darauf öffnet den Screenshot direkt im Bild-Editor.
- **Einfügen in Studio (`Strg + V`):**
  - Neuer Button *„📋 Aus Zwischenablage (Strg+V)“* und *„📸 Screenshot (Win+Shift+S)“* im Studio.
  - Wenn kein Bild in der Zwischenablage liegt, startet ein Klick direkt das Windows Snipping Tool.
- **Loop-Schutz:** Kopieren aus dem Image-Editor in die Zwischenablage (*„📋 In Zwischenablage“*) unterdrückt automatisch die Selbstauslösung des Listeners.
- **Einstellung:** In den Einstellungen unter *„📸 Screenshots & Zwischenablage“* kann die automatische Erkennung jederzeit ein- oder ausgeschaltet werden.

---

## 🚀 Was ist neu in Version 2.2.4 (Startmenü & Windows-Suche Integration):

### 🔍 Windows-Startmenü & Windows-Suche (`Win + S`)
- **Vollständige Auffindbarkeit als portable App:**
  - Automatische Registrierung einer Startmenü-Verknüpfung unter `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Imaginary.lnk`.
  - Registrierung in der Windows-Registry (`HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\Imaginary.exe`), wodurch Imaginary sofort über die Windows-Suche (`Win + S`), das Startmenü oder den Ausführen-Dialog (`Win + R -> imaginary`) gestartet werden kann.
  - Funktioniert vollständig im Benutzerbereich ohne Administratorrechte.
- **Auto-Synchronisation & Self-Healing:**
  - Beim Verschieben oder Aktualisieren der portable `.exe` aktualisiert Imaginary beim Starten automatisch den Verknüpfungspfad.
- **Einstellungskarte unter System & Hintergrundbetrieb:**
  - Neue Einstellungskarte mit Status-Badge (`✔ Registriert` / `⚪ Nicht registriert`) und 1-Klick-Button zum Aktivieren bzw. sauberen Deaktivieren/Entfernen.

---

## 🚀 Was ist neu in Version 2.2.3 (Studio-Fixes & Dark Mode Kontextmenü):

### ✏️ Quick-Edit & Studio-Fixes
- **Exakte Text-Auswahlbox:**
  - Die gestrichelte Auswahlbox und die 4 Ziehgriffe umschließen platzierte Texte und deren Hintergrund-Pille nun pixelgenau.
  - Typografische Baseline-Berechnung korrigiert, kein Durchschneiden der Schrift oder vertikaler Versatz mehr.
- **Flüssige Freihand-Vorschau (Stift):**
  - Beim Zeichnen mit dem Freihandstift folgt nun eine dynamische, runde Pfadvorschau (`PreviewPolyline`) in Echtzeit der Mausbewegung.
  - Keine starre gerade Linie mehr während des Ziehens.

### 🎨 Dark Mode Kontextmenü
- **Neues modernes Kontextmenü-Design:**
  - Rechtsklick auf Bilder in der Liste öffnet nun ein vollständig gestyltes, dunkles Menü (`#151E32`) mit dezentem Schatten und klarem blauem Hover-Effekt.
  - Kein weißer Kasten mit unlesbarem Text mehr.
- **Intelligente Zeilenauswahl per Rechtsklick:**
  - Ein Rechtsklick auf eine beliebige Tabellenzeile wählt diese Datei sofort aktiv aus, sodass Kontextaktionen direkt auf das gewünschte Bild angewendet werden.

---

## 🚀 Was ist neu in Version 2.2.2 (Dark Mode Kontrast & UI-Optimierungen):

### 🎨 Dunkles Design (Dark Mode) Perfektionierung & Hoher Kontrast
- **Lesbarkeit aller eingeklappten Bereiche (Studio / Batches):**
  - Alle auf- und zuklappbaren Karten (`Expander`) besitzen jetzt ein konsistentes Card-Design mit klar abgehobenem Header (`CardHeaderBackgroundBrush`), kontrastreicher weißer Schrift (`TextPrimaryBrush`) und sauberer visueller Hierarchie.
  - Kein schwarzer Standard-Text mehr auf dunklem Hintergrund in Aufklapp-Panels.
- **Windows-Explorer Integration in den Einstellungen:**
  - Der Button zeigt nun professionelle deutsche Handlungsverben („Aktivieren“ / „Deaktivieren“) anstelle von kryptischem `True`/`False`.
  - Ergänzt durch sichtbare Status-Badges (`✔ Aktiviert` in Smaragdgrün / `⚪ Nicht aktiv` in Schiefergrau) und optimal lesbare Beschriftung.
- **Vollständige DynamicResource-Harmonisierung:**
  - Alle Buttons, RadioButtons und Toolbars in `MainWindow`, `ImageEditorWindow`, `PreviewWindow` und `LogViewerWindow` wurden auf dynamische Ressourcen umgestellt.
  - Schneller, fehlerfreier Theme-Wechsel zwischen Hell und Dunkel in Echtzeit ohne Neustart.
- **Globale Vererbung von Schriftfarben:**
  - Globale WPF-Stile für `Window`, `TextBlock`, `CheckBox` und `Expander` garantieren optimale Kontrastwerte und verhindern, dass unformatierter Text auf Standard-Schwarz zurückfällt.

---

## 🚀 Was ist neu in Version 2.2.1 (Hotfix & Resilienz-Update):

### 🛡️ Robuste Notfall-Wiederherstellung & Automatischer Rollback
- **Startup Crash behoben:** Ein Argument-Typ-Konflikt (`string` vs `int`) im Navigations-Befehl des Hauptfensters wurde behoben.
- **🚨 Notfall-Wiederherstellungsfenster (Crash Recovery):**
  - Fängt Startabstürze oder Crash-Schleifen sicher ab, ohne dass die Anwendung spurlos verschwindet.
  - **1-Klick Rollback:** Setzt die Installation mit einem Klick auf die zuvor lauffähige Version zurück (`.previous.exe` / `.old`).
  - **Online-Hotfix-Suche:** Sucht direkt aus dem Notfallfenster nach neuen Updates auf GitHub und installiert sie nahtlos.
  - **Log-Inspektion:** Direkter Zugriff auf die Diagnose-Logdateien zur Fehleranalyse.
- **Watchdog für Crash-Loops (`StartupHealthTracker`):** Erkennt aufeinanderfolgende Startabbrüche automatisch und startet präventiv im Rettungsmodus.
- **Sichere Backup-Verwaltung:** Vorherige Versionen werden beim Update dauerhaft als `.previous.exe` und `.old` gesichert und nicht mehr gelöscht.
- **CLI-Notfallbefehle:**
  - `Imaginary.exe --rollback`: Führt sofort einen Rollback zur vorherigen Version durch.
  - `Imaginary.exe --recovery`: Öffnet das Notfall-Wiederherstellungsfenster.

---

## 🚀 Was ist neu in Version 2.2.0:

### 🤖 Model Context Protocol (MCP) Server Integration
- **Offizielle MCP-Server Schnittstelle (JSON-RPC 2.0 via Stdio):** Imaginary kann nun direkt als lokales Werkzeug von **Claude Desktop, ChatGPT, Cursor, Antigravity** und KI-Agenten gesteuert werden!
- **10 mächtige KI- und Bildbearbeitungswerkzeuge:**
  - `convert_image`: Konvertiert zwischen JPEG, PNG, WebP, AVIF, GIF, BMP, TIFF und ICO mit Skalierung und Qualitätsstufen.
  - `remove_background`: Freistellen mit lokalem Deep-Learning Modell (U-2-Net ONNX) oder Farbschlüsselung (Magic Wand).
  - `segment_object_by_region`: Gezieltes KI-Ausschneiden von Objekten innerhalb einer Bounding-Box `[x, y, w, h]`.
  - `redact_region`: DSGVO-konforme Bildzensur durch Verpixeln (`pixelate`), Weichzeichnen (`blur`) oder Schwärzen (`blackout`).
  - `crop_image`: Verlustfreies Zuschneiden von Bildbereichen.
  - `apply_watermark`: Text-Wasserzeichen mit wählbarer Position, Transparenz und Schriftgröße.
  - `generate_icon_set`: Generierung kompletter Windows `.ico`-Dateien (16 bis 256 px).
  - `inspect_image`: Detaillierte Analyse von Abmessungen, Format, Farbraum und Alphakanal.
  - `check_ai_model` & `download_ai_model`: Verwaltung des lokalen U-2-Net KI-Modells.
- **MCP Resources & Vordefinierte Prompts:**
  - Resources `imaginary://system/capabilities` und `imaginary://models/u2net`.
  - Vordefinierte Workflows für E-Commerce Produktbilder und DSGVO-Dokumentenzensur.
- **CLI & 1-Klick Installation:**
  - `Imaginary.exe --mcp`: Startet den MCP-Server.
  - `Imaginary.exe --mcp-config`: Gibt Claude-Desktop Konfigurations-JSON aus.
  - `Imaginary.exe --mcp-install-claude`: Automatische 1-Klick Installation in `%APPDATA%\Claude\claude_desktop_config.json`.
- **Zukunftssichere Auto-Discovery:**
  - Neue Werkzeuge werden über Attribute automatisch erkannt und bei jedem Update für alle angebundenen KIs verfügbar gemacht.

---

## 🚀 Was ist neu in Version 2.1.0:

### 🎯 Interaktiver KI-Pinsel & Gezielte Objektauswahl (Hintergrund entfernen)
- **Gezielte Objektauswahl per Pinsel:** Im Studio für Hintergrundentfernung gibt es nun den Modus „🎯 KI-Pinsel & Objektauswahl“. Anstatt nur das gesamte Bild automatisch zu verarbeiten, kann der Benutzer mit dem Pinsel grob über das gewünschte Objekt malen – die Deep-Learning KI isoliert und schneidet exakt dieses Motiv frei.
- **🟢 Kanten-Wiederherstellen-Pinsel:** Falls bei der Freistellung feine Details verloren gingen, können Pixel mit dem Wiederherstellungs-Pinsel weich und pixelgenau aus dem Original zurückgemalt werden.
- **🔴 Kanten-Radieren-Pinsel:** Verbleibende Hintergrundreste oder Kantenfransen lassen sich mit dem Radier-Pinsel transparent wegradieren.
- **Stufenlose Pinselgröße:** Die Pinselgröße lässt sich flexibel von 6 bis 120 Pixel einstellen.

### 📐 Optimiertes Studio-Layout & Feste Aktionsleiste
- **Aufklappbare Einstellungskarten:** Selten genutzte Bereiche wie *4. Wasserzeichen* und *2. Maximale Dateigröße* sind nun standardmäßig kompakt und können bei Bedarf mit einem Klick auf- und zugeklappt werden.
- **Dauerhaft sichtbare Start-Buttons:** Die Aktions-Buttons „🚀 Konvertierung starten“ und „Abbrechen“ sind nun fest am unteren Rand des linken Panels fixiert – kein Scrollen mehr nötig, egal welche Bildschirmauflösung oder Fenstergröße verwendet wird.

### 📁 Ausgabeordner unter die Dateiliste verlegt
- **Bessere Übersicht & volle Pfadlänge:** Der Ausgabeordner befindet sich nun direkt unter der Dateitabelle auf der rechten Seite.
- **Direktzugriff:** Mit dem neuen Button „📂 Öffnen“ kann der Zielordner sofort mit einem Klick im Windows Explorer geöffnet werden.


