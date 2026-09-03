<img src="docs/logo.png" alt="UpdateWatch2" width="96" height="96" />

# UpdateWatch2 Agent

A .NET Worker Service, targeting both Windows and Linux from one codebase, that runs on endpoints managed by UpdateWatch2.

- Periodically checks for available updates (interval configurable, with random jitter) and reports findings — plus whether a reboot is required — to the server.
- Installs updates on remote trigger from the server, without initiating a reboot itself.
- Sends periodic alive messages to the server.
- Identified by hostname; authenticates to the server via a client certificate issued after manual admin approval during onboarding (not yet implemented — see this repo's open issues).
- Windows configuration (server address/port, etc.) is stored in the registry, set via an NSIS installer; the Linux build reads an equivalent local config file.

See the project CLAUDE.md for the full architectural briefing, module layout, and configurable-behavior contract, and this repo's own open issues for what's still outstanding.

Companion repository: `updatewatch2-server`.
