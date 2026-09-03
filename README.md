# UpdateWatch2 Agent

Windows service (with a Linux agent planned as a future addition) that runs on endpoints managed by UpdateWatch2.

- Periodically checks for available updates (interval configurable, with random jitter) and reports findings — plus whether a reboot is required — to the server.
- Installs updates on remote trigger from the server, without initiating a reboot itself.
- Sends periodic alive messages to the server.
- Identified by hostname; authenticates to the server via a client certificate issued after manual admin approval during onboarding.
- Windows configuration (server address/port, etc.) is stored in the registry, set via an NSIS installer; a future Linux agent will use an equivalent local config file.

This repository is in the pre-implementation / planning stage — see the project CLAUDE.md for the full architectural briefing, module layout, and configurable-behavior contract this repo is expected to implement.

Companion repository: `updatewatch2-server`.
