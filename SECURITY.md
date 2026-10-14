# Security

HoloWin is an experimental desktop application running with the current Windows user's permissions. It does not install a service or driver, and it does not provide a sandbox for configured actions.

## Actions and input

Scripts, executable actions, HTTP requests, screenshots and macros require approval in the action editor. Per-execution confirmation is optional. Cooldowns and confidence thresholds reduce unintended triggers but cannot guarantee that a sound is an intentional tap.

Once configured and enabled, a desk tap, camera gesture, light rule or presence rule can run its assigned command. Gesture, light and presence actions must be armed for each session. Only configure actions whose effects you understand. Text and keyboard actions target the active application; verify focus before using them. Timeouts request cancellation and do not guarantee that every native side effect is reversible or immediately stopped.

Microphone capture, camera preview and sensor readings have explicit controls. Startup at sign-in is opt-in. Dynamic plugin loading is not implemented. No inbound network listener is configured by the application; user-configured actions can make outbound requests or launch other software.

Connection and proximity signals do not authenticate a person. Presence rules are for convenience actions, not access control.

## Files and trust

Profiles and action settings are local JSON, not a security boundary. Schema validation checks compatibility; it does not authenticate files. Profile import disables zone mappings, clears gesture action references and removes light and presence rules. Action parameters and logs are not encrypted.

Use application packages from sources you trust. The single EXE extracts native libraries and resources into the user's local release cache. Do not replace those files with untrusted binaries. SHA-256 files verify that downloads match the published checksum; they do not establish publisher identity. The build scripts do not sign executables or install certificates.

## Reporting vulnerabilities

Avoid posting exploit details, credentials or private data in public issues. If private vulnerability reporting is enabled on the repository's Security tab, use that channel. Otherwise, request a private contact channel from the maintainer without disclosing the vulnerability publicly. No response-time commitment is currently published.

Include the affected version, impact, reproduction steps and any proposed mitigation. Test only on systems you own or are authorized to assess.

See [Privacy](PRIVACY.md), [Automation](docs/AUTOMATION.md) and [Contributing](CONTRIBUTING.md).
