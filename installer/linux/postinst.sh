#!/bin/sh
# fpm --after-install hook (deb postinst / rpm %post — same script, both
# package types). Never fails the package install/upgrade over anything
# non-essential here: only `set -e` around the steps that must succeed.
set -e

CONFIG_DIR=/etc/updatewatch2
CONFIG_FILE="$CONFIG_DIR/agent.conf"

# LinuxFileConfigStore.Save() only ever runs once this agent has
# successfully registered with a server (see RegistrationWorker), so
# without a ServerAddress configured there is otherwise no config file at
# all to point an admin at. Write a starter one here instead — same JSON
# shape and same default values AgentOptions itself declares (so this is
# purely for discoverability, not a second source of truth) — but only if
# nothing is there yet, so a reinstall/upgrade never clobbers an
# already-configured agent.
if [ ! -e "$CONFIG_FILE" ]; then
    mkdir -p "$CONFIG_DIR"
    cat > "$CONFIG_FILE" <<'EOF'
{
  "ServerAddress": "",
  "ServerPort": 8796,
  "UpdateCheckIntervalMinutes": 240,
  "UpdateCheckJitterSeconds": 300,
  "AliveIntervalMinutes": 5,
  "LogLevel": "INFO",
  "RegistrationRetryIntervalSeconds": 30,
  "RegistrationToken": null,
  "ClientCertificateThumbprint": null,
  "CertificateRenewalLeadTimeDays": 60,
  "CertificateMaintenanceIntervalSeconds": 900
}
EOF
    # Matches the restriction LinuxFileConfigStore.Save() itself applies —
    # this file can carry a RegistrationToken bearer secret later.
    chmod 600 "$CONFIG_FILE"
    chown root:root "$CONFIG_FILE"
fi

# Unlike agent.conf/agent.pfx, this is a PUBLIC certificate — FileCaTrustStore
# only ever holds public key material — so world-readable is correct, not a
# secret-exposure concern. This block only NORMALIZES an already-present
# file; it never creates or fetches one itself. fpm-built .deb/.rpm
# packages have no install-time parameter mechanism (unlike the NSIS
# Windows installer's /CACERT= switch), so pre-seeding the CA here to
# close the trust-on-first-use (TOFU) window is a manual step: an admin
# downloads the server's CA root (admin UI's Certificates tab, or
# GET /api/admin/certificate-authority/download) and scp's it to this
# path themselves, before this package's first `systemctl start` —
# normalizing ownership/permissions here just makes that hand-placed file
# forgiving of whatever perms it happened to arrive with.
CA_FILE="$CONFIG_DIR/ca.pem"
if [ -e "$CA_FILE" ]; then
    chown root:root "$CA_FILE"
    chmod 644 "$CA_FILE"
fi

if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
    systemctl enable updatewatch2-agent.service || true

    # prerm.sh stops (and disables) the OLD package's service before dpkg/
    # rpm unpacks the new files, on the documented assumption that this
    # script starts it back up afterward — a real bug, not just a stale
    # comment, found by an actual `dpkg -i` upgrade on a real host: this
    # script used to print the "no server configured yet" message and
    # leave the service stopped UNCONDITIONALLY, even when
    # /etc/updatewatch2/agent.conf already had a real ServerAddress from
    # before the upgrade (LinuxFileConfigStore.Save() and this script's own
    # starter file above both write the same "ServerAddress": "..." shape,
    # so grepping for a non-empty value reliably tells the two cases
    # apart). Only a config that genuinely has no server configured yet —
    # a brand-new install, or an admin who hasn't filled in the starter
    # file this script just seeded — should stay stopped with that
    # guidance; an already-configured agent being upgraded must come back
    # up on the new binary on its own.
    if grep -Eq '"ServerAddress"[[:space:]]*:[[:space:]]*"[^"]+"' "$CONFIG_FILE" 2>/dev/null; then
        systemctl restart updatewatch2-agent.service || true
    else
        cat <<'EOF'
UpdateWatch2 Agent installed but not started: it has no server to talk to
yet. Set "ServerAddress" (and "ServerPort" if not 8796) in
/etc/updatewatch2/agent.conf, then:

    systemctl start updatewatch2-agent
EOF
    fi
fi

exit 0
