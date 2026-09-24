#!/usr/bin/env bash
# Starts/stops the throwaway Fedora container DnfIntegrationTests
# (updatewatch2-agent#8's own long-standing "not live-verified against a
# real dnf/yum host" gap) runs against — real docker CLI calls, matching
# server/scripts/run-ldap-test-server.sh's own precedent (deliberately not
# Testcontainers — see that script's comment for why). Used both by CI
# (see ../.github/workflows/ci.yml's dnf-integration-test job) and by hand
# for local development — same script either way, so there is exactly one
# place this container's setup is defined.
#
# Unlike the LDAP container (a network service reached over a TCP port),
# dnf/yum is invoked as a local subprocess by DnfUpdateSession — the test
# code has to actually run *inside* the container for it to shell out to a
# real dnf. The repo is therefore bind-mounted in (no image entrypoint
# here fights a bind mount the way the LDAP image's does), and a real
# .NET SDK is installed straight from Fedora's own official repos so
# `dotnet test` can run there directly, with no separate publish/copy step.
#
# Usage: scripts/run-fedora-test-server.sh up|down
set -euo pipefail

CONTAINER_NAME="updatewatch2-fedora-test"
FEDORA_IMAGE="${UPDATEWATCH2_TEST_FEDORA_IMAGE:-fedora:44}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR/.."

case "${1:-}" in
  up)
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

    docker run -d \
      --name "$CONTAINER_NAME" \
      -v "$REPO_ROOT:/workspace" \
      -w /workspace \
      "$FEDORA_IMAGE" sleep infinity >/dev/null

    echo "Installing .NET SDK and dnf-utils/yum-utils inside the Fedora container..."
    # dnf-utils/yum-utils is installed for parity with an older, classic
    # dnf/yum host — on Fedora's current dnf5, `needs-restarting` is
    # already bundled as a plugin and this is a no-op/already-satisfied,
    # but a genuinely older RPM-based target might still need it (see
    # DnfIntegrationTests' own doc comment on the dnf5-vs-classic-dnf
    # distinction found running this the first time).
    if ! docker exec "$CONTAINER_NAME" dnf install -y dotnet-sdk-10.0 dnf-utils; then
      echo "Failed to install dotnet-sdk-10.0/dnf-utils in the Fedora container — see 'docker logs ${CONTAINER_NAME}'." >&2
      exit 1
    fi

    echo "Fedora test container is up."
    ;;
  down)
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
    ;;
  *)
    echo "Usage: $0 up|down" >&2
    exit 1
    ;;
esac
