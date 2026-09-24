<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Agent

**Autor:** Thorsten Schröpel · [🇬🇧 English version](README.md)

[![Latest Release](https://img.shields.io/github/v/release/vulture20/updatewatch2-agent?logo=github&color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/vulture20/updatewatch2-agent/total?color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/vulture20/updatewatch2-agent/ci.yml?branch=main&label=build)](https://github.com/vulture20/updatewatch2-agent/actions/workflows/ci.yml)
[![Status](https://img.shields.io/badge/status-stable-brightgreen)](#-projektstatus)
[![Lizenz: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

UpdateWatch2 Agent ist die Endgeräte-Hälfte von **UpdateWatch2**: ein .NET-Worker-Service, der aus einer einzigen Codebasis heraus sowohl Windows als auch Linux bedient, auf Betriebssystem-Updates prüft, sie (samt Information, ob ein Neustart nötig ist) an den Server meldet und sie nur auf Fernauslösung hin installiert — nie von sich aus neu startet.

> ✅ **Stabil.** Das zertifikatsbasierte Onboarding und der Heartbeat sind durchgängig implementiert und gegen einen echten laufenden Server getestet; die Verhandlungshälfte des Selbst-Updates (Angebot, Download, SHA-256-Prüfung) ebenfalls. Die echte Windows-Update-API-Integration (WUApiLib), das Installations-/Deinstallationsverhalten des Windows-Installers, der Windows-Selbst-Update-Anwendungsschritt, die Remote-Neustart-Planung auf beiden Plattformen sowie die Windows-Ereignisprotokoll-Ausgabe wurden inzwischen allesamt gegen echte Hosts bestätigt, inklusive einer selektiven (Pro-Update-)Installation. Der Linux-`dnf`/`yum`-Update-Pfad (Suche, Vorab-Download, selektive und vollständige Installation, Neustart-Erkennung) wurde inzwischen ebenfalls gegen einen echten Fedora-Host bestätigt. Der Linux-Selbst-Update-Anwendungsschritt sowie der arm64-Installationspfad unter Windows und Linux wurden **noch nicht** gegen ein echtes Zielsystem verifiziert. Siehe [Projektstatus](#-projektstatus) weiter unten.

Begleit-Repository: [updatewatch2-server](https://github.com/vulture20/updatewatch2-server) — der Verwaltungsserver, an den dieser Agent meldet.

## ✨ Funktionsumfang

### 🔐 Zertifikatsbasiertes Onboarding
- Identifikation über den Hostnamen. Beim ersten Kontakt registriert sich der Agent, verankert das CA-Zertifikat des Servers (Trust-on-First-Use) und fragt so lange nach, bis ein Admin ihn freigegeben und ein Client-Zertifikat ausgestellt hat.
- Das Zertifikat wird sicher gespeichert (Windows: im Zertifikatsspeicher des Computers, nicht exportierbar; Linux: `/etc/updatewatch2/agent.pfx`, nur für den Besitzer lesbar) und danach bei jeder Anfrage vorgelegt.
- Erneuert sich selbst proaktiv vor Ablauf, erholt sich automatisch, wenn ein Admin ein frisches Registrierungstoken neu ausstellt — in beiden Fällen ohne Dienst-Neustart — und übernimmt eine rotierte CA-Wurzel, sobald der Server sie meldet, statt den normalen Zeitplan abzuwarten.

### 💓 Heartbeat & Update-Prüfung
- Eine regelmäßige Alive-Meldung hält die Geräteübersicht des Servers aktuell (zuletzt gesehen, DNS-Name, Betriebssystem, IP, Agent-Version — bei jedem Heartbeat neu gesendet, nicht nur einmalig bei der Registrierung).
- Die Update-Prüfung läuft auf einem eigenen, konfigurierbaren Intervall mit zufälligem Jitter, damit nicht viele Agents gleichzeitig auf den Server treffen.
- Erkennt eine Protokollversions-Abweichung gegenüber dem Server und protokolliert eine Warnung, ohne hart abzubrechen.

### 📦 Echte Update-Erkennung & -Installation
- **Windows:** die echte Windows-Update-API (WUApiLib) per Late-Bound-COM — Suche, Download und Installation, wobei Treiber-Updates standardmäßig bewusst ausgeschlossen werden, derselbe konservative Standard wie in der Windows-Update-Oberfläche selbst. Kann anstehende Updates außerdem proaktiv vorab herunterladen, bevor eine Installation tatsächlich ausgelöst wird — gesteuert über eine admin-konfigurierbare, flottenweite Option (Einstellungen → Allgemein auf dem Server) —, sodass eine Installation aus dem lokalen Cache erfolgt statt erst zum Installationszeitpunkt herunterzuladen.
- **Linux:** `apt`/`dpkg` auf Debian-basierten Distributionen, `dnf`/`yum` auf RPM-basierten — automatisch beim Start erkannt; fällt auf einen No-Op-Checker zurück, falls keins von beiden vorhanden ist. Dieselbe unabhängige Vorab-Download-Option wie bei Windows oben (Einstellungen → Allgemein auf dem Server hat für jede Plattform eine eigene Checkbox).
- Die Installation löst nie selbst einen Neustart aus — "Neustart erforderlich" ist immer ein separates, unabhängig gemeldetes Signal. Ein vollständiger Neustart der Maschine (nicht nur des eigenen Dienstes) kann von einem Admin trotzdem aus der Ferne ausgelöst werden — Zustellung und Bestätigung laufen genauso wie bei einer ausgelösten Installation.

### 🔄 Agent-Selbst-Update
- Reagiert darauf, wenn der Server über den bestehenden Heartbeat-Kanal ein neueres Agent-Release anbietet — keine separate Abfrageschleife.
- Lädt das Update von **dem Server selbst** herunter, nie direkt von GitHub — der Agent braucht dafür also keinen eigenen Internetzugang.
- Prüft den SHA-256-Wert des Downloads, bevor er überhaupt angewendet wird — bei Abweichung wird abgebrochen und der Download gelöscht, ohne irgendetwas Plattformspezifisches anzufassen.
- Windows: führt den NSIS-Installer still erneut aus. Linux: `dpkg -i`/`rpm -U` des Pakets, danach Neustart des eigenen systemd-Dienstes.

## ✅ Projektstatus

UpdateWatch2 wurde per **Vibe-Coding** entwickelt: implementiert und iteriert im Dialog mit [Claude Code](https://claude.com/claude-code) (Anthropic), statt Zeile für Zeile von Hand geschrieben, angetrieben von einem menschlich verfassten Architektur-Briefing. Der Zertifikats-Lebenszyklus (Registrierung, Freigabe, Erneuerung, Neuausstellung, CA-Wurzel-Rotation) und der Alive-Heartbeat wurden live gegen einen echten Server ausgeführt; sowohl der Linux-`apt`- als auch der `dnf`/`yum`-Update-*Erkennungs*- und *Installationspfad* wurden live ausgeführt — `apt` gegen einen echten Paket-Cache, `dnf`/`yum` durchgängig gegen einen echten Fedora-Host (Suche, Vorab-Download, selektive und vollständige Installation, Neustart-Erkennung, über den Wegwerf-Container, den `scripts/run-fedora-test-server.sh` aufsetzt); alles ist durch eine automatisierte (xUnit-)Testsuite abgedeckt. Die Windows-Update-API-Integration (WUApiLib-COM) wurde inzwischen ebenfalls vollständig live gegen einen echten Windows-Host ausgeführt — Suche, Download, Installation (inklusive einer selektiven Pro-Update-Installation) und Neustart-Erkennung allesamt bestätigt funktionierend — und auch das tatsächliche Installations-/Deinstallationsverhalten des NSIS-Installers über `sc.exe` wurde gegen einen echten Windows-Host bestätigt, womit [updatewatch2-agent#13](https://github.com/vulture20/updatewatch2-agent/issues/13) geschlossen wurde. Die Verhandlungshälfte des Agent-Selbst-Updates (der Server bietet ein Release an, der Agent lädt es herunter und prüft den SHA-256-Wert) wurde ebenfalls live ausgeführt, und unter Windows inzwischen auch das tatsächliche *Anwenden*: Das stille erneute Ausführen des NSIS-Installers durch `WindowsInstallerApplier`, inklusive der Dienst-Lösch-/Neuanlage-Sequenz, wurde gegen einen echten Windows-Host bestätigt funktionierend. Ebenso bestätigt: die `shutdown.exe`-basierte Planung eines Remote-Neustarts unter Windows, die `systemd-run --on-active=... -- systemctl reboot`-basierte Planung unter Linux (beide Plattformen sind damit vollständig live verifiziert), sowie die Windows-Ereignisprotokoll-Ausgabe. Eine Reihe von Teilen ist ausdrücklich **noch nicht gegen ein echtes Zielsystem live verifiziert**, im Code entsprechend gekennzeichnet:

- **Windows**: Windows-on-ARM (`win-arm64`) vollständig — die Veröffentlichung selbst wurde bestätigt eine echte native ARM64-Programmdatei zu erzeugen, nur eben nie auf einem echten Gerät ausgeführt.
- **Linux**: ein wirklich älterer klassischer-dnf-/reiner-yum-Host — der oben verwendete Fedora-44-Host läuft mit dnf5, das für jeden von diesem Agenten genutzten Befehl CLI-kompatibel, aber eine andere Codebasis als klassisches dnf/yum ist; das tatsächliche *Anwenden* eines Selbst-Updates (`dpkg -i`/`rpm -U`), das auf einem echten Host vollständig durchläuft — ein *separater* Mechanismus vom oben verifizierten OS-Update-Installationspfad (`LinuxPackageApplier`, nicht `DnfUpdateSession`/`AptUpdateSession`) — der zugrunde liegende Cgroup-Escape-Mechanismus wurde live bestätigt, ein vollständiger Selbst-Update-Zyklus danach aber nicht erneut verifiziert, und [updatewatch2-agent#24](https://github.com/vulture20/updatewatch2-agent/issues/24) dokumentiert einen echten Bericht, der auf ein mögliches stilles Fehlschlagen hindeutet.
- **Paketierung**: `.rpm`-Installation/-Upgrade auf x86_64 (nur strukturell geprüft, nie über einen echten Paketmanager ausgeführt). Die arm64-`.deb`/`.rpm`-Pakete sind eine Teilausnahme: jede CI-Pipeline eines Releases startet die veröffentlichte `linux-arm64`-Programmdatei tatsächlich auf einem nativen (nicht emulierten) arm64-Runner und bestätigt, dass sie ihren echten Start-/Registrierungscode ausführt, bevor sie verpackt wird — das ist also live auf echter arm64-Hardware verifiziert; Installation/Upgrade über `dpkg`/`rpm` selbst auf einem arm64-Host dagegen nicht, genau wie die Lücke beim x86_64. Windows-on-ARM (`win-arm64`) ist ebenso unbestätigt auf einem echten Gerät.

Betrachte die oben genannten Teile als gut recherchiert, aber noch nicht gegen ein echtes Zielsystem bestätigt — alles andere, einschließlich der Windows-Update-API-Integration, des Installations-/Deinstallationsverhaltens des Installers, des Windows-Selbst-Update-Anwendungsschritts, des Remote-Neustarts auf beiden Plattformen sowie der Windows-Ereignisprotokoll-Ausgabe oben, wurde durchgängig live verifiziert.

## 🚀 Installation & Konfiguration

Jedes getaggte Release ([`release.yml`](.github/workflows/release.yml), ausgelöst durch einen `vX.Y.Z`-Push) baut und veröffentlicht installierbare Pakete als [GitHub-Release](https://github.com/vulture20/updatewatch2-agent/releases)-Anhänge — kein manueller Build nötig.

### Windows

Jedes Release veröffentlicht zwei Installer — `UpdateWatch2Agent-Setup-<Version>-x64.exe` für reguläres (x64-)Windows und `UpdateWatch2Agent-Setup-<Version>-arm64.exe` für Windows-on-ARM-Geräte (Snapdragon-basierte Laptops, Surface Pro X, ...), jeweils mit einer nativen, self-contained Veröffentlichung für diese Architektur. Den passenden herunterladen und ausführen — alles Folgende gilt identisch für beide, nur mit dem entsprechenden Dateinamen:

```powershell
# Interaktive Installation — fragt nach Serveradresse/-port
UpdateWatch2Agent-Setup-1.0.24-x64.exe

# Unbeaufsichtigte Installation (z. B. über ein Deployment-Tool)
UpdateWatch2Agent-Setup-1.0.24-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796
```

Dies installiert und startet den Windows-Dienst `UpdateWatch2 Agent` und schreibt Serveradresse/-port nach `HKLM\SOFTWARE\UpdateWatch2\Agent` (per ACL auf Administrators/SYSTEM beschränkt). Ein erneuter Lauf des Installers über eine bestehende Installation führt ein Upgrade an Ort und Stelle durch. Der Deinstaller entfernt Dienst, Installationsverzeichnis, Registry-Schlüssel und (nach bestem Bemühen) das eigene Client-Zertifikat des Agents aus dem Computer-Zertifikatsspeicher.

#### CA-Zertifikat vorab hinterlegen (schließt das Trust-on-First-Use-Fenster)

Standardmäßig vertraut ein frisch installierter Agent beim allerersten Kontakt einfach dem CA-Zertifikat, das der Server ihm gibt ("Trust-on-First-Use", TOFU) — ein Angreifer, der genau in diesem Moment im Netz sitzt, könnte die Verbindung abfangen und dem Agenten eine gefälschte CA unterschieben. Um dieses Fenster zu schließen, das aktuelle CA-Wurzelzertifikat des Servers vorab herunterladen — über den Certificates-Tab der Admin-Oberfläche ("CA-Wurzelzertifikat herunterladen") oder direkt per `GET /api/admin/certificate-authority/download`, beides session-authentifiziert — und per `/CACERT=` an den Installer übergeben:

```powershell
UpdateWatch2Agent-Setup-1.0.24-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796 /CACERT=C:\temp\updatewatch2-ca.crt
```

Das ist optional und vollständig abwärtskompatibel — ohne `/CACERT=` bleibt das ursprüngliche TOFU-Verhalten unverändert.

### Linux (`.deb` / `.rpm`, x86_64 oder arm64)

Jedes Release veröffentlicht beide Architekturen — `amd64`/`x86_64`-Pakete für reguläre Linux-Hosts und `arm64`/`aarch64` für arm64-Hosts (AWS Graviton, Ampere Altra, Raspberry Pi, ...), jeweils mit einer nativen, self-contained Veröffentlichung für diese Architektur. Die zur Host-Architektur passende Datei herunterladen (`uname -m`: `x86_64` → das `amd64`/`x86_64`-Paket, `aarch64` → das `arm64`/`aarch64`-Paket):

```bash
# Debian/Ubuntu, x86_64
sudo dpkg -i updatewatch2-agent_<Version>_amd64.deb
# Debian/Ubuntu, arm64
sudo dpkg -i updatewatch2-agent_<Version>_arm64.deb

# RHEL/Fedora/openSUSE, x86_64
sudo rpm -U updatewatch2-agent-<Version>-1.x86_64.rpm
# RHEL/Fedora/openSUSE, arm64
sudo rpm -U updatewatch2-agent-<Version>-1.aarch64.rpm
```

Dies installiert nach `/opt/updatewatch2-agent/`, legt eine Start-Konfiguration `/etc/updatewatch2/agent.conf` an, falls noch keine existiert, und liefert eine systemd-Unit (`updatewatch2-agent.service`) mit — **aktiviert, aber nicht gestartet**, bis eine Serveradresse gesetzt ist:

```bash
sudo nano /etc/updatewatch2/agent.conf   # "ServerAddress" setzen (und "ServerPort", falls nicht 8796)
sudo systemctl start updatewatch2-agent
```

Ein Upgrade über einen bereits konfigurierten, bereits laufenden Agent startet den Dienst automatisch neu, um die neue Binärdatei zu übernehmen — kein manueller Schritt nötig.

`.deb`/`.rpm`-Pakete haben keinen Mechanismus für Installations-Parameter, daher gibt es unter Linux kein Äquivalent zu `/CACERT=` oben — stattdessen das heruntergeladene CA-Zertifikat selbst unter `/etc/updatewatch2/ca.pem` ablegen, *bevor* `systemctl start updatewatch2-agent` zum ersten Mal läuft, um dasselbe Fenster zu schließen. `postinst.sh` normalisiert Besitzer/Rechte (`root:root`, world-readable), falls beim Paket-Install schon eine Datei dort liegt.

### Konfigurationsreferenz

Jeder Schlüssel unten wird wortgleich an beiden Stellen verwendet: als Registry-*Wertname* unter `HKLM\SOFTWARE\UpdateWatch2\Agent` unter Windows, und als JSON-*Feldname* in `/etc/updatewatch2/agent.conf` unter Linux.

| Einstellung | Schlüssel | Standard | Bedeutung |
|---|---|---|---|
| Serveradresse | `ServerAddress` | — (erforderlich) | Hostname/IP des UpdateWatch2-Servers — **muss exakt** dessen eigener `UPDATEWATCH2_SERVER_HOSTNAME` entsprechen; dieser Agent prüft das SAN des Server-Zertifikats dagegen. |
| Server-Port | `ServerPort` | `8796` | Der agent-seitige, gegenseitig TLS-authentifizierte Port des Servers. |
| Hostname-Override | `HostnameOverride` | — (nicht gesetzt) | Überschreibt den Hostnamen, den dieser Agent an den Server meldet — und unter dem er fortan identifiziert wird — anstelle des vom Betriebssystem gemeldeten Rechnernamens. Rein lokal, wird nie vom Server gepusht. Beeinflusst **nicht** das separate, rein informative DNS-Name-Feld in der Admin-Oberfläche. **Nur vor der allerersten Registrierung dieses Agents gefahrlos setzbar** — eine Änderung an einem bereits zertifizierten Agent wird erkannt (bei jedem Heartbeat wird eine Warnung geloggt, und es wird weder Heartbeat noch Zertifikatserneuerung versucht, solange der Zustand anhält), aber nicht automatisch behoben; um einen bereits eingebundenen Agent tatsächlich umzubenennen, muss dessen alter Eintrag in der Admin-Oberfläche gelöscht und das lokale Zertifikat dieses Agents entfernt werden (Windows: Computer-Zertifikatsspeicher; Linux: `/etc/updatewatch2/agent.pfx`), damit er sich unter dem neuen Namen frisch registriert. |
| Update-Prüfintervall | `UpdateCheckIntervalMinutes` | `240` | Basisintervall zwischen Update-Prüfungen, in Minuten. |
| Update-Prüf-Jitter | `UpdateCheckJitterSeconds` | `300` | Zufälliger Zuschlag (0..N Sekunden), damit nicht viele Agents gleichzeitig auf den Server treffen. |
| Heartbeat-Intervall | `AliveIntervalMinutes` | `5` | Wie oft dieser Agent eine Alive-Meldung sendet. |
| Registrierungs-Abfrageintervall | `RegistrationRetryIntervalSeconds` | `30` | Wie oft dieser Agent den Server abfragt, während er auf Admin-Freigabe/Zertifikatsausstellung beim Onboarding wartet — getrennt vom (deutlich längeren) Heartbeat-Intervall, da in dieser Phase typischerweise ein Mensch zusieht. |
| Log-Level | `LogLevel` | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR`. |
| Vorlaufzeit Zertifikatserneuerung | `CertificateRenewalLeadTimeDays` | `60` | Tage vor Ablauf seines Zertifikats, ab denen dieser Agent proaktiv ein neues anfordert. |
| Zertifikats-Wartungsintervall | `CertificateMaintenanceIntervalSeconds` | `900` | Obergrenze, wie oft dieser Agent sein lokales Zertifikat erneut prüft, sobald eines vorhanden ist (z. B. um ein frisches Neuausstellungs-Token zu bemerken). Nur eine Obergrenze — ein abgelehntes Zertifikat löst diese Prüfung sofort aus, statt das Intervall abzuwarten. |
| Nicht authentifizierte Pakete zulassen | `AllowUnauthenticatedPackages` | `false` | **Nur Linux.** Übergibt bei Installation und Vorab-Download apt-gets `--allow-unauthenticated` bzw. dnfs/yums `--nogpgcheck`, damit ein Repository mit ungültiger oder fehlender Signatur nicht die gesamte Transaktion scheitern lässt. Sicherheitsrelevant — nur aktivieren, wenn bewusst entschieden wurde, einem unsignierten/lokalen Repository zu vertrauen; der übliche Fix für einen Fehler wegen "unauthenticated packages" ist der Import des GPG-Schlüssels dieses Repositories, nicht diese Option. Rein lokal, wird nie vom Server gepusht. |
| Aufbewahrung Selbst-Update-Staging | `SelfUpdateStagingRetentionDays` | `90` | Wie lange ein heruntergeladenes Selbst-Update-Paket im lokalen Staging-Verzeichnis verbleibt, bevor es aufgeräumt wird. Das zuletzt heruntergeladene Paket bleibt unabhängig von diesem Wert immer erhalten. |

`RegistrationToken` und `ClientCertificateThumbprint` liegen ebenfalls hier, werden aber vom Agent selbst automatisch verwaltet — von Hand nur setzen, wenn ein Admin dir für eine Neuausstellung ein frisches Token gegeben hat (siehe die Admin-Oberfläche des Servers). Nach Änderung eines dieser Werte ist kein Dienst-Neustart nötig — der Agent übernimmt Konfigurationsänderungen von selbst im nächsten Wartungs-/Heartbeat-Takt.

> **Ein Neuausstellungs-Token setzen:** es genügt, das frische `RegistrationToken` einzutragen — mehr ist nicht nötig. Ab Agent-Version 0.14.3 erkennt `RegistrationWorker` selbstständig, dass sich das Token im Config-Store vom bereits verwendeten unterscheidet, und löscht daraufhin automatisch das lokal noch vorhandene alte Zertifikat (inklusive `ClientCertificateThumbprint`), bevor er sich mit dem neuen Token registriert — das deckt auch den Fall ab, dass ein Zertifikat für einen Agent neu ausgestellt wird, der lokal noch einwandfrei läuft (Verdacht auf Kompromittierung, nicht Verlust), nicht nur ein tatsächlich verlorenes/gelöschtes Zertifikat. Bei einer älteren Agent-Version musst du `ClientCertificateThumbprint` (Windows-Registry) selbst löschen bzw. `/etc/updatewatch2/agent.pfx` (Linux) entfernen — bleibt der alte Wert dort stehen, bleibt das frische Token stillschweigend ungenutzt liegen: `RegistrationWorker` findet weiterhin das alte Zertifikat vor und registriert sich nie neu; eine Wiederherstellung passiert dann erst, wenn der Server das alte Zertifikat oft genug ablehnt, damit das Self-Heal von `HeartbeatWorker` es bemerkt und löscht.

## 🧱 Technischer Stack

- .NET-10-Generic-Host-Worker-Service, der aus einer einzigen Codebasis heraus Windows und Linux bedient — plattformspezifische Teile (Registry vs. Konfigurationsdatei, WUApiLib vs. `apt`/`dnf`, die beiden Client-Zertifikatsspeicher) werden beim Start ausgewählt, nicht über getrennte Build-Konfigurationen.
- Paketierung: NSIS (Windows-Installer), [`fpm`](https://fpm.readthedocs.io/) (`.deb`/`.rpm`) aus einer systemd-Unit sowie Pre-/Postinst-Skripten.

## 📁 Verzeichnisstruktur

```
src/UpdateWatch2.Agent/    Certificates/, Communication/, Configuration/, Reboot/, SelfUpdate/, UpdateCheck/ (Windows/, Linux/), RegistrationWorker.cs, HeartbeatWorker.cs, UpdateCheckWorker.cs
tests/                     xUnit — handgeschriebene Fakes, keine Mocking-Bibliothek
installer/nsis/            setup.nsi — der Windows-Installer
installer/linux/           systemd-Unit + postinst-/prerm-/postrm-Skripte, paketiert via fpm
```

## 🛠️ Lokale Entwicklung

Erfordert das .NET-10-SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/UpdateWatch2.Agent   # läuft im Vordergrund, nicht als Dienst installiert
```

## 📜 Änderungsprotokoll

Siehe [`CHANGELOG.md`](CHANGELOG.md) (Englisch) für eine versionsweise Historie aller nennenswerten Änderungen.

## ⚖️ Lizenz

Copyright (C) 2026 Thorsten Schröpel.

UpdateWatch2 Agent ist freie Software: Du darfst sie unter den Bedingungen der [GNU Affero General Public License](LICENSE), wie von der Free Software Foundation veröffentlicht, weitergeben und/oder verändern, entweder gemäß Version 3 der Lizenz oder (nach deiner Wahl) jeder späteren Version. Siehe [LICENSE](LICENSE) oder <https://www.gnu.org/licenses/agpl-3.0.html> für den vollständigen Text.

Kurz gefasst: Du darfst UpdateWatch2 frei betreiben, verändern und selbst hosten. Die zusätzliche Pflicht, die AGPL im Vergleich zu einer gewöhnlichen GPL-Lizenz mit sich bringt: Wenn du eine **veränderte** Version betreibst und anderen Nutzern über ein Netzwerk zugänglich machst, musst du diesen Nutzern auch Zugriff auf deinen veränderten Quellcode geben — nicht nur Personen, denen du die Software direkt aushändigst. Der reine Betrieb einer unveränderten Kopie für dich selbst bringt über die üblichen Copyleft-Bedingungen hinaus keine zusätzliche Pflicht mit sich.
