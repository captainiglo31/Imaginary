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


