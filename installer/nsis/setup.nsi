; UpdateWatch2 Agent — NSIS installer.
;
; Installs the self-contained win-x64 published output (see the
; "windows-installer" job in .github/workflows/release.yml for the exact
; `dotnet publish` invocation) as the "UpdateWatch2 Agent" Windows service
; (Program.cs: AddWindowsService(o => o.ServiceName = "UpdateWatch2 Agent")
; — this script's SERVICE_NAME must stay byte-for-byte identical to that
; string, since Windows treats the service name as its primary key), asks
; for the server address/port on an interactive install (silently
; configurable via /SERVERADDRESS=/SERVERPORT= for unattended rollouts),
; and writes them to HKLM\SOFTWARE\UpdateWatch2\Agent — the same registry
; location WindowsRegistryConfigStore reads/writes at runtime, so nothing
; installer-specific leaks into the agent's own config model. Every other
; AgentOptions field (update-check interval/jitter, alive interval, log
; level, certificate-renewal lead time, ...) is deliberately left unset
; here; WindowsRegistryConfigStore.Load() already supplies the same
; defaults AgentOptions itself declares, so there is nothing for this
; installer to duplicate.
;
; Build with:
;   makensis /DVERSION=<semver, e.g. 0.5.0> setup.nsi
; run from this directory — VERSION defaults to 0.0.0 for local/manual
; testing when the define is omitted. PUBLISH_DIR (default: ..\..\publish\win-x64,
; relative to this script) must already contain a `dotnet publish -r win-x64
; --self-contained -p:PublishSingleFile=true` output before this runs.
;
; Uninstall removes the service, the install directory, the registry key
; tree below (all of it, not just the values this installer itself wrote —
; WindowsRegistryConfigStore.cs's doc comment requires no residue), the
; Add/Remove Programs entry, and — best-effort, since a missing/failed
; certutil call must never abort the uninstall — this agent's own client
; certificate from the machine store, looked up by the thumbprint the
; service itself recorded in the registry (see AgentOptions.ClientCertificateThumbprint).

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\..\publish\win-x64"
!endif

!define PRODUCT_NAME "UpdateWatch2 Agent"
!define SERVICE_NAME "UpdateWatch2 Agent"
!define COMPANY_NAME "Thorsten Schröpel"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\UpdateWatch2Agent"
!define CONFIG_KEY "SOFTWARE\UpdateWatch2\Agent"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"

Name "${PRODUCT_NAME}"
OutFile "UpdateWatch2Agent-Setup-${VERSION}-x64.exe"
InstallDir "$PROGRAMFILES64\UpdateWatch2 Agent"
InstallDirRegKey HKLM "${CONFIG_KEY}" "InstallDir"
RequestExecutionLevel admin
ShowInstDetails show
ShowUnInstDetails show

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName" "${COMPANY_NAME}"
VIAddVersionKey "LegalCopyright" "Copyright (C) 2026 ${COMPANY_NAME}"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} installer"

!define MUI_ICON "icon.ico"
!define MUI_UNICON "icon.ico"
!define MUI_ABORTWARNING

Var ServerAddress
Var ServerPort
Var ServerAddressField
Var ServerPortField

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
Page custom ServerConfigPageCreate ServerConfigPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; ---------------------------------------------------------------------
; .onInit — pre-fill $ServerAddress/$ServerPort, in priority order:
; existing registry values (so re-running the installer to upgrade an
; already-configured agent doesn't blank its config), then /SERVERADDRESS=
; and /SERVERPORT= command-line overrides (for unattended /S rollouts),
; then a hardcoded default port of 8443 (AgentOptions.ServerPort's own
; default) if nothing else supplied one.
; ---------------------------------------------------------------------
Function .onInit
  SetRegView 64

  StrCpy $ServerPort "8443"
  ReadRegStr $0 HKLM "${CONFIG_KEY}" "ServerAddress"
  ${IfNot} ${Errors}
    StrCpy $ServerAddress $0
  ${EndIf}
  ReadRegDWORD $0 HKLM "${CONFIG_KEY}" "ServerPort"
  ${IfNot} ${Errors}
    StrCpy $ServerPort $0
  ${EndIf}

  ${GetOptions} $CMDLINE "/SERVERADDRESS=" $0
  ${IfNot} ${Errors}
    StrCpy $ServerAddress $0
  ${EndIf}
  ${GetOptions} $CMDLINE "/SERVERPORT=" $0
  ${IfNot} ${Errors}
    StrCpy $ServerPort $0
  ${EndIf}
FunctionEnd

; ---------------------------------------------------------------------
; Custom page: server address/port. Skipped entirely in silent installs
; (values already resolved in .onInit above, from the registry and/or
; /SERVERADDRESS=//SERVERPORT=) — nsDialogs never shows a window in that
; case, this guard just avoids doing pointless control-creation work.
; ---------------------------------------------------------------------
Function ServerConfigPageCreate
  IfSilent skip_create

  !insertmacro MUI_HEADER_TEXT "Server Connection" "Configure the UpdateWatch2 server this agent reports to."
  nsDialogs::Create 1018
  Pop $0

  ${NSD_CreateLabel} 0 0 100% 12u "Server address (hostname or IP) — must match the server's UPDATEWATCH2_SERVER_HOSTNAME:"
  Pop $0
  ${NSD_CreateText} 0 14u 100% 12u "$ServerAddress"
  Pop $ServerAddressField

  ${NSD_CreateLabel} 0 34u 100% 12u "Server agent port (UPDATEWATCH2 mTLS port, default 8443):"
  Pop $0
  ${NSD_CreateText} 0 48u 60u 12u "$ServerPort"
  Pop $ServerPortField

  ${NSD_CreateLabel} 0 68u 100% 24u "Leave blank to install without connecting yet — configure HKLM\SOFTWARE\UpdateWatch2\Agent by hand (or re-run this installer) before starting the service."
  Pop $0

  nsDialogs::Show
  skip_create:
FunctionEnd

Function ServerConfigPageLeave
  IfSilent skip_leave

  ${NSD_GetText} $ServerAddressField $ServerAddress
  ${NSD_GetText} $ServerPortField $ServerPort

  ${If} $ServerPort != ""
    IntOp $0 $ServerPort + 0
    IntCmp $0 0 port_invalid port_invalid port_ok
    port_invalid:
      MessageBox MB_ICONEXCLAMATION "Server port must be a positive number."
      Abort
    port_ok:
  ${EndIf}
  skip_leave:
FunctionEnd

; ---------------------------------------------------------------------
Section "UpdateWatch2 Agent" SEC_MAIN
  SetRegView 64
  SetOutPath "$INSTDIR"

  ; Existing service, if any (upgrade case) — stop and remove before
  ; overwriting its binary, which a running Windows service holds locked.
  nsExec::ExecToLog 'sc.exe stop "${SERVICE_NAME}"'
  Pop $0
  Sleep 1000
  nsExec::ExecToLog 'sc.exe delete "${SERVICE_NAME}"'
  Pop $0

  File "${PUBLISH_DIR}\UpdateWatch2.Agent.exe"
  File "${PUBLISH_DIR}\appsettings.json"

  ${If} $ServerAddress != ""
    WriteRegStr HKLM "${CONFIG_KEY}" "ServerAddress" "$ServerAddress"
  ${EndIf}
  ${If} $ServerPort != ""
    WriteRegDWORD HKLM "${CONFIG_KEY}" "ServerPort" "$ServerPort"
  ${EndIf}
  WriteRegStr HKLM "${CONFIG_KEY}" "InstallDir" "$INSTDIR"

  nsExec::ExecToLog 'sc.exe create "${SERVICE_NAME}" binPath= "$INSTDIR\UpdateWatch2.Agent.exe" start= auto DisplayName= "${SERVICE_NAME}" obj= LocalSystem'
  Pop $0
  nsExec::ExecToLog 'sc.exe description "${SERVICE_NAME}" "Checks for and reports Windows updates to the UpdateWatch2 server; installs updates on remote trigger. See https://github.com/vulture20/updatewatch2-agent"'
  Pop $0
  nsExec::ExecToLog 'sc.exe failure "${SERVICE_NAME}" reset= 86400 actions= restart/10000/restart/60000/restart/60000'
  Pop $0
  nsExec::ExecToLog 'sc.exe start "${SERVICE_NAME}"'
  Pop $0

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${COMPANY_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\UpdateWatch2.Agent.exe"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

; ---------------------------------------------------------------------
Section "Uninstall"
  SetRegView 64

  nsExec::ExecToLog 'sc.exe stop "${SERVICE_NAME}"'
  Pop $0
  Sleep 1000
  nsExec::ExecToLog 'sc.exe delete "${SERVICE_NAME}"'
  Pop $0

  ; Best-effort: remove this agent's own client certificate from the
  ; machine store (WindowsClientCertificateStore) by the thumbprint the
  ; running service recorded — never lets a certutil failure (e.g. the
  ; cert was already gone) abort the rest of the uninstall.
  ReadRegStr $0 HKLM "${CONFIG_KEY}" "ClientCertificateThumbprint"
  ${If} $0 != ""
    nsExec::ExecToLog 'certutil -delstore My "$0"'
    Pop $1
  ${EndIf}

  Delete "$INSTDIR\UpdateWatch2.Agent.exe"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  ; The pinned server CA certificate FileCaTrustStore writes to
  ; %ProgramData%\UpdateWatch2\ca.pem — agent-owned, not shared with
  ; anything else; safe to remove wholesale on uninstall. $APPDATA
  ; resolves to the shared (CSIDL_COMMON_APPDATA / %ProgramData%) path
  ; here, not the per-user one, because of SetShellVarContext all in
  ; .onInit — NSIS has no separate $COMMONAPPDATA constant.
  SetShellVarContext all
  RMDir /r "$APPDATA\UpdateWatch2"

  DeleteRegKey HKLM "${CONFIG_KEY}"
  DeleteRegKey /ifempty HKLM "SOFTWARE\UpdateWatch2"
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
SectionEnd
