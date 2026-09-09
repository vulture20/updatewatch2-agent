<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Agent

**Autor:** Thorsten Schröpel · [🇬🇧 English version](README.md)

[![Latest Release](https://img.shields.io/github/v/release/vulture20/updatewatch2-agent?logo=github&color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/vulture20/updatewatch2-agent/total?color=2496ED)](https://github.com/vulture20/updatewatch2-agent/releases)
[![Build](https://img.shields.io/github/actions/workflow/status/vulture20/updatewatch2-agent/ci.yml?branch=main&label=build)](https://github.com/vulture20/updatewatch2-agent/actions/workflows/ci.yml)
[![Status](https://img.shields.io/badge/status-Beta-orange)](#-projektstatus)
[![Lizenz: AGPL v3](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](LICENSE)

UpdateWatch2 Agent ist die Endgeräte-Hälfte von **UpdateWatch2**: ein .NET-Worker-Service, der aus einer einzigen Codebasis heraus sowohl Windows als auch Linux bedient, auf Betriebssystem-Updates prüft, sie (samt Information, ob ein Neustart nötig ist) an den Server meldet und sie nur auf Fernauslösung hin installiert — nie von sich aus neu startet.

> ⚠️ **Beta.** Das zertifikatsbasierte Onboarding, der Heartbeat und der Selbst-Update-Mechanismus sind durchgängig implementiert und gegen einen echten laufenden Server getestet. Die echte Windows-Update-API-Integration (WUApiLib), der Linux-`dnf`/`yum`-Update-Pfad sowie das Installations-/Deinstallationsverhalten des Windows-Installers wurden **noch nicht** gegen ein echtes Zielsystem verifiziert. Siehe [Projektstatus](#-projektstatus) weiter unten.

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
- **Windows:** die echte Windows-Update-API (WUApiLib) per Late-Bound-COM — Suche, Download und Installation, wobei Treiber-Updates standardmäßig bewusst ausgeschlossen werden, derselbe konservative Standard wie in der Windows-Update-Oberfläche selbst.
- **Linux:** `apt`/`dpkg` auf Debian-basierten Distributionen, `dnf`/`yum` auf RPM-basierten — automatisch beim Start erkannt; fällt auf einen No-Op-Checker zurück, falls keins von beiden vorhanden ist.
- Die Installation löst nie selbst einen Neustart aus — "Neustart erforderlich" ist immer ein separates, unabhängig gemeldetes Signal.

### 🔄 Agent-Selbst-Update
- Reagiert darauf, wenn der Server über den bestehenden Heartbeat-Kanal ein neueres Agent-Release anbietet — keine separate Abfrageschleife.
- Lädt das Update von **dem Server selbst** herunter, nie direkt von GitHub — der Agent braucht dafür also keinen eigenen Internetzugang.
- Prüft den SHA-256-Wert des Downloads, bevor er überhaupt angewendet wird — bei Abweichung wird abgebrochen und der Download gelöscht, ohne irgendetwas Plattformspezifisches anzufassen.
- Windows: führt den NSIS-Installer still erneut aus. Linux: `dpkg -i`/`rpm -U` des Pakets, danach Neustart des eigenen systemd-Dienstes.

## 🚧 Projektstatus

UpdateWatch2 wurde per **Vibe-Coding** entwickelt: implementiert und iteriert im Dialog mit [Claude Code](https://claude.com/claude-code) (Anthropic), statt Zeile für Zeile von Hand geschrieben, angetrieben von einem menschlich verfassten Architektur-Briefing. Der Zertifikats-Lebenszyklus, das Registrierungs-/Heartbeat-/Selbst-Update-Protokoll sowie der Linux-`apt`-Update-Erkennungspfad wurden live gegen einen echten Server und einen echten Paket-Cache ausgeführt und sind durch eine automatisierte (xUnit-)Testsuite abgedeckt. Einige Teile sind ausdrücklich **noch nicht gegen ein echtes Zielsystem live verifiziert**, im Code entsprechend gekennzeichnet: die Windows-Update-API-Integration (WUApiLib-COM), der Linux-`dnf`/`yum`-Pfad (die eigene Entwicklungsumgebung dieses Projekts ist Debian-basiert) sowie das tatsächliche Installations-/Deinstallationsverhalten des NSIS-Windows-Installers über `sc.exe`/einen Paketmanager. Betrachte dies als eine gut recherchierte, aktiv getestete Implementierung zum Weiterbauen — noch nicht als produktionserprobte Software.

## 🚀 Installation & Konfiguration

Jedes getaggte Release ([`release.yml`](.github/workflows/release.yml), ausgelöst durch einen `vX.Y.Z`-Push) baut und veröffentlicht installierbare Pakete als [GitHub-Release](https://github.com/vulture20/updatewatch2-agent/releases)-Anhänge — kein manueller Build nötig.

### Windows

`UpdateWatch2Agent-Setup-<Version>-x64.exe` herunterladen und ausführen:

```powershell
# Interaktive Installation — fragt nach Serveradresse/-port
UpdateWatch2Agent-Setup-0.12.0-x64.exe

# Unbeaufsichtigte Installation (z. B. über ein Deployment-Tool)
UpdateWatch2Agent-Setup-0.12.0-x64.exe /S /SERVERADDRESS=updatewatch2.example.com /SERVERPORT=8796
```

Dies installiert und startet den Windows-Dienst `UpdateWatch2 Agent` und schreibt Serveradresse/-port nach `HKLM\SOFTWARE\UpdateWatch2\Agent` (per ACL auf Administrators/SYSTEM beschränkt). Ein erneuter Lauf des Installers über eine bestehende Installation führt ein Upgrade an Ort und Stelle durch. Der Deinstaller entfernt Dienst, Installationsverzeichnis, Registry-Schlüssel und (nach bestem Bemühen) das eigene Client-Zertifikat des Agents aus dem Computer-Zertifikatsspeicher.

### Linux (`.deb` / `.rpm`, x86_64)

```bash
# Debian/Ubuntu
sudo dpkg -i updatewatch2-agent_<Version>_amd64.deb

# RHEL/Fedora/openSUSE
sudo rpm -U updatewatch2-agent-<Version>-1.x86_64.rpm
```

Dies installiert nach `/opt/updatewatch2-agent/`, legt eine Start-Konfiguration `/etc/updatewatch2/agent.conf` an, falls noch keine existiert, und liefert eine systemd-Unit (`updatewatch2-agent.service`) mit — **aktiviert, aber nicht gestartet**, bis eine Serveradresse gesetzt ist:

```bash
sudo nano /etc/updatewatch2/agent.conf   # "ServerAddress" setzen (und "ServerPort", falls nicht 8796)
sudo systemctl start updatewatch2-agent
```

Ein Upgrade über einen bereits konfigurierten, bereits laufenden Agent startet den Dienst automatisch neu, um die neue Binärdatei zu übernehmen — kein manueller Schritt nötig.

### Konfigurationsreferenz

| Einstellung | Windows (Registry, `HKLM\SOFTWARE\UpdateWatch2\Agent`) | Linux (`/etc/updatewatch2/agent.conf`, JSON) | Standard | Bedeutung |
|---|---|---|---|---|
| Serveradresse | `ServerAddress` | `ServerAddress` | — (erforderlich) | Hostname/IP des UpdateWatch2-Servers — **muss exakt** dessen eigener `UPDATEWATCH2_SERVER_HOSTNAME` entsprechen; dieser Agent prüft das SAN des Server-Zertifikats dagegen. |
| Server-Port | `ServerPort` | `ServerPort` | `8796` | Der agent-seitige, gegenseitig TLS-authentifizierte Port des Servers. |
| Update-Prüfintervall | `UpdateCheckIntervalMinutes` | `UpdateCheckIntervalMinutes` | `240` | Basisintervall zwischen Update-Prüfungen, in Minuten. |
| Update-Prüf-Jitter | `UpdateCheckJitterSeconds` | `UpdateCheckJitterSeconds` | `300` | Zufälliger Zuschlag (0..N Sekunden), damit nicht viele Agents gleichzeitig auf den Server treffen. |
| Heartbeat-Intervall | `AliveIntervalMinutes` | `AliveIntervalMinutes` | `5` | Wie oft dieser Agent eine Alive-Meldung sendet. |
| Log-Level | `LogLevel` | `LogLevel` | `INFO` | `DEBUG`/`INFO`/`WARNING`/`ERROR`. |
| Vorlaufzeit Zertifikatserneuerung | `CertificateRenewalLeadTimeDays` | `CertificateRenewalLeadTimeDays` | `60` | Tage vor Ablauf seines Zertifikats, ab denen dieser Agent proaktiv ein neues anfordert. |

`RegistrationToken` und `ClientCertificateThumbprint` liegen ebenfalls hier, werden aber vom Agent selbst automatisch verwaltet — von Hand nur setzen, wenn ein Admin dir für eine Neuausstellung ein frisches Token gegeben hat (siehe die Admin-Oberfläche des Servers). Nach Änderung eines dieser Werte ist kein Dienst-Neustart nötig — der Agent übernimmt Konfigurationsänderungen von selbst im nächsten Wartungs-/Heartbeat-Takt.

> **Ein Neuausstellungs-Token setzen:** es genügt, das frische `RegistrationToken` einzutragen — mehr ist nicht nötig. Ab Agent-Version 0.14.3 erkennt `RegistrationWorker` selbstständig, dass sich das Token im Config-Store vom bereits verwendeten unterscheidet, und löscht daraufhin automatisch das lokal noch vorhandene alte Zertifikat (inklusive `ClientCertificateThumbprint`), bevor er sich mit dem neuen Token registriert — das deckt auch den Fall ab, dass ein Zertifikat für einen Agent neu ausgestellt wird, der lokal noch einwandfrei läuft (Verdacht auf Kompromittierung, nicht Verlust), nicht nur ein tatsächlich verlorenes/gelöschtes Zertifikat. Bei einer älteren Agent-Version musst du `ClientCertificateThumbprint` (Windows-Registry) selbst löschen bzw. `/etc/updatewatch2/agent.pfx` (Linux) entfernen — bleibt der alte Wert dort stehen, bleibt das frische Token stillschweigend ungenutzt liegen: `RegistrationWorker` findet weiterhin das alte Zertifikat vor und registriert sich nie neu; eine Wiederherstellung passiert dann erst, wenn der Server das alte Zertifikat oft genug ablehnt, damit das Self-Heal von `HeartbeatWorker` es bemerkt und löscht.

## 🧱 Technischer Stack

- .NET-10-Generic-Host-Worker-Service, der aus einer einzigen Codebasis heraus Windows und Linux bedient — plattformspezifische Teile (Registry vs. Konfigurationsdatei, WUApiLib vs. `apt`/`dnf`, die beiden Client-Zertifikatsspeicher) werden beim Start ausgewählt, nicht über getrennte Build-Konfigurationen.
- Paketierung: NSIS (Windows-Installer), [`fpm`](https://fpm.readthedocs.io/) (`.deb`/`.rpm`) aus einer systemd-Unit sowie Pre-/Postinst-Skripten.

## 📁 Verzeichnisstruktur

```
src/UpdateWatch2.Agent/    Certificates/, Communication/, Configuration/, SelfUpdate/, UpdateCheck/ (Windows/, Linux/), RegistrationWorker.cs, HeartbeatWorker.cs, UpdateCheckWorker.cs
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
