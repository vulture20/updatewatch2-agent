#!/bin/sh
# fpm --after-remove hook (deb postrm / rpm %postun). Deliberately does NOT
# remove /etc/updatewatch2 (this agent's registration/certificate state) —
# the package never shipped that directory, only postinst.sh created it, so
# dpkg/rpm have no opinion on it either way; leaving it behind on removal
# matches normal config-preservation expectations and avoids silently
# invalidating an agent's identity from an accidental "apt remove" (as
# opposed to a deliberate purge).
set -e

if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload || true
fi

exit 0
