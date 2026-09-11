## 🚀 Was ist neu in Version 2.0.3:

### 🐛 Kritische Fehlerbehebungen
- **Bild-Editor-Absturz behoben:** Beim Klick auf den Button „Bearbeiten“ kam es bisher zu einer unbehandelten Ausnahme (`NullReferenceException`), da Steuerelemente für den Hintergrundmodus noch während der Fenster-Initialisierung Ereignisse auslösten. Dies wurde vollständig behoben; der Bild-Editor öffnet sich nun blitzschnell und stabil.
- **Autostart-Fensteranzeige behoben:** Wenn Imaginary mit Windows minimiert im Infobereich gestartet wurde, reagierte ein erneuter Klick auf das Desktop-Icon oder die Anwendungsdatei bisher nicht. Das IPC-Aktivierungssignal (Single Instance Pipe) wurde grundlegend überarbeitet: Imaginary erkennt den Aufruf sofort, stellt das Fenster wieder her und bringt es zuverlässig in den Vordergrund.

### ⚙️ Verbesserungen für System & Autostart
- **Freie Wahl des Autostart-Verhaltens:** In den Einstellungen (*🖥️ System & Hintergrundbetrieb*) kann nun flexibel gewählt werden, ob Imaginary beim Windows-Login:
  - **Sichtbar** mit dem vollen Hauptfenster geöffnet werden soll (Standard).
  - Oder **lautlos minimiert** im Windows-Infobereich (System-Tray) startet.
- **Dezenter Tray-Hinweis:** Beim Start im Infobereich informiert nun eine kurze Windows-Benachrichtigung darüber, dass Imaginary im Hintergrund aktiv ist. Ein Klick darauf öffnet direkt die Oberfläche.
- **Klick-Aktivierung aus dem Infobereich:** Ein Klick auf Benachrichtigungen aus dem Infobereich stellt das Hauptfenster nun ebenfalls unmittelbar wieder her.

### 🔄 Automatische Updates im laufenden Betrieb
- **Periodische Hintergrundprüfung:** Imaginary prüft nun nicht mehr nur beim Start, sondern auch während des laufenden Betriebs (alle 4 Stunden dezent im Hintergrund) auf neue Releases und Aktualisierungen.
- **Einstellungsoption:** In den Einstellungen unter *🔄 Updates & Info* lässt sich diese automatische periodische Hintergrundsuche jederzeit nach Wunsch aktivieren oder deaktivieren.
- **Tray-Update-Benachrichtigung:** Wird ein neues Update gefunden während das Fenster minimiert ist, erscheint ein Hinweis im Infobereich, über den das Update sofort bezogen werden kann.
