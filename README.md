<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Agent

A .NET Worker Service, targeting both Windows and Linux from one codebase, that runs on endpoints managed by UpdateWatch2.

- Periodically checks for available updates (interval configurable, with random jitter) and reports findings — plus whether a reboot is required — to the server.
- Installs updates on remote trigger from the server, without initiating a reboot itself.
- Sends periodic alive messages to the server.
- Identified by hostname; authenticates to the server via a client certificate issued after manual admin approval during onboarding. On first contact this agent registers, pins the server's CA certificate (trust-on-first-use — see `Certificates/PinnedServerCertificateValidator`), and polls until an admin approves it and a certificate arrives; once received, the certificate is stored (Windows: the machine certificate store; Linux: `/etc/updatewatch2/agent.pfx`) and presented on every subsequent request. See `updatewatch2-agent#1`/`updatewatch2-server#1`.
- Windows configuration (server address/port, etc.) is stored in the registry, set via an NSIS installer; the Linux build reads an equivalent local config file at `/etc/updatewatch2/agent.conf`. `ServerAddress` must match the server's own `UPDATEWATCH2_SERVER_HOSTNAME` (see the server repo's README/`.env.example`) — this agent validates the server's certificate SAN against it, not just that it chains to the pinned CA.

See the project CLAUDE.md for the full architectural briefing, module layout, and configurable-behavior contract, and this repo's own open issues for what's still outstanding.

Companion repository: `updatewatch2-server`.

## License

Copyright (C) 2026 Thorsten Schröpel.

UpdateWatch2 Agent is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE), or <https://www.gnu.org/licenses/agpl-3.0.html> for the full text.
