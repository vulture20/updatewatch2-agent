#!/bin/sh
# fpm --before-remove hook (deb prerm / rpm %preun). Runs on both removal
# and upgrade (fpm-built packages don't distinguish); stopping is harmless
# either way since --after-install starts the service again post-upgrade.
set -e

if command -v systemctl >/dev/null 2>&1; then
    systemctl stop updatewatch2-agent.service 2>/dev/null || true
    systemctl disable updatewatch2-agent.service 2>/dev/null || true
fi

exit 0
