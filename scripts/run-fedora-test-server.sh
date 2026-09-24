#!/usr/bin/env bash
# Starts/stops the throwaway RPM-based-distro container DnfIntegrationTests
# (updatewatch2-agent#8's own long-standing "not live-verified against a
# real dnf/yum host" gap) runs against — real docker CLI calls, matching
# server/scripts/run-ldap-test-server.sh's own precedent (deliberately not
# Testcontainers — see that script's comment for why). Used both by CI
# (see ../.github/workflows/ci.yml's dnf-integration-test matrix job) and
# by hand for local development — same script either way, so there is
# exactly one place this container's setup is defined.
#
# Despite the filename (kept for continuity with the first, Fedora-only
# version of this script), this now runs against whichever image
# UPDATEWATCH2_TEST_RPM_IMAGE names — CI's matrix passes both a Fedora
# (dnf5) and a Rocky Linux (classic dnf4) image through it, closing the
# "only dnf5 has ever been live-verified" gap the first version of this
# script left open. A genuinely older yum-only host (no `dnf` binary at
# all, e.g. CentOS 7) was verified once by hand instead — see
# DnfUpdateSession's own doc comment for why it isn't part of this
# permanent rotation (CentOS 7 is EOL, and its glibc/libstdc++ can't run
# the .NET SDK this script installs at all).
#
# Unlike the LDAP container (a network service reached over a TCP port),
# dnf/yum is invoked as a local subprocess by DnfUpdateSession — the test
# code has to actually run *inside* the container for it to shell out to a
# real dnf. The repo is therefore bind-mounted in (no image entrypoint
# here fights a bind mount the way the LDAP image's does), and a real
# .NET SDK is installed straight from the target distro's own official
# repos so `dotnet test` can run there directly, with no separate
# publish/copy step — confirmed to work identically via plain `dnf
# install -y dotnet-sdk-10.0` on both Fedora (native package) and Rocky
# Linux 9 (Red Hat's own AppStream build, which Rocky mirrors — no extra
# Microsoft repo needed).
#
# Usage: scripts/run-fedora-test-server.sh up|down
set -euo pipefail

CONTAINER_NAME="updatewatch2-rpm-test"
RPM_IMAGE="${UPDATEWATCH2_TEST_RPM_IMAGE:-fedora:44}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR/.."

case "${1:-}" in
  up)
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

    docker run -d \
      --name "$CONTAINER_NAME" \
      -v "$REPO_ROOT:/workspace" \
      -w /workspace \
      "$RPM_IMAGE" sleep infinity >/dev/null

    echo "Installing .NET SDK and dnf-utils/yum-utils inside the ${RPM_IMAGE} container..."
    # dnf-utils/yum-utils is installed for parity with an older, classic
    # dnf/yum host — on Fedora's current dnf5, `needs-restarting` is
    # already bundled as a plugin and this is a no-op/already-satisfied,
    # but a genuinely older RPM-based target might still need it (see
    # DnfIntegrationTests' own doc comment on the dnf5-vs-classic-dnf
    # distinction found running this the first time).
    if ! docker exec "$CONTAINER_NAME" dnf install -y dotnet-sdk-10.0 dnf-utils; then
      echo "Failed to install dotnet-sdk-10.0/dnf-utils in the ${RPM_IMAGE} container — see 'docker logs ${CONTAINER_NAME}'." >&2
      exit 1
    fi

    echo "RPM test container (${RPM_IMAGE}) is up."
    ;;
  down)
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
    ;;
  *)
    echo "Usage: $0 up|down" >&2
    exit 1
    ;;
esac
