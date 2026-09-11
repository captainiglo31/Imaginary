## 🚀 Was ist neu in Version 2.0.2:

### 🐛 Fehlerbehebungen (Vorher/Nachher-Vergleich)
- **Überlappung beim Zoomen behoben:** In der Nebeneinander-Ansicht konnten vergrößerte Bilder bisher über ihren Anzeigebereich hinausrendern und das jeweils andere Bild oder den Trenner überdecken. Durch striktes Layout-Clipping bleiben beide Bilder nun jederzeit sauber in ihren Spalten abgegrenzt.
- **Synchroner Zoom ohne Drift:** Der Fokuspunkt beim Hinein- und Herauszoomen mit dem Mausrad wird nun exakt innerhalb der jeweiligen Bildhälfte berechnet. Dadurch tritt kein seitliches Wegdriften der Bilder mehr auf.
- **Perfekte Zentrierung beim Reset:** Beim Herauszoomen auf 100% rasten beide Bilder automatisch wieder passgenau und zentriert ein, ohne dass ein Rest-Offset verbleibt.

### 🔍 Verbesserte Bedienung im Vergleichs-Viewer
- **Intuitives Verschieben (Pan):** In der Nebeneinander-Ansicht kann das Bild bei Vergrößerung nun neben der rechten/mittleren Maustaste auch bequem mit der **linken Maustaste** gegriffen und synchron verschoben werden.
- **Dynamischer Mauszeiger:** Bei aktivem Zoom signalisiert der Mauszeiger automatisch die Verschiebbarkeit (Pan-Symbol).
- **Doppelklick-Reset:** Ein Doppelklick auf eine beliebige Stelle des Bildes setzt den Zoom sofort wieder auf 100% zurück.
- **Kontextsensitive Hilfetexte:** Die untere Hinweisleiste zeigt stets die passenden Shortcuts für den aktuell gewählten Modus (Split-Slider vs. Nebeneinander).
