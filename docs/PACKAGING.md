# Portable Windows distribution

HoloWin is a local, unpackaged desktop application. Microsoft Store publication and MSIX installation are not required.

## Single EXE distribution

```powershell
.\scripts\publish-single-exe.ps1
```

Share `artifacts/standalone/HoloWin.exe` by itself. This self-contained launcher embeds the complete application ZIP and extracts it on first launch into `%LOCALAPPDATA%\HoloWin\releases\<payload-hash>`. Subsequent launches reuse that release. The app still needs these extracted files internally, but users do not need to copy or manage companion files beside the downloaded EXE.

Camera libraries and the pinned OpenCV palm/hand models are included. An updated EXE uses a separate cache directory when its payload changes. Profiles and settings stay in the existing HoloWin data directory. Old cached releases are retained; close HoloWin before removing an unused release folder. If automatic startup points to an older extracted release, disable it there and re-enable it in the new release.

The launcher reports extraction/start failures in `releases/launcher-error.log`. `--extract-only` validates extraction without opening the GUI. A SHA-256 sidecar is produced beside the EXE.

## Folder / ZIP distribution

From the repository root:

```powershell
.\scripts\publish.ps1
```

This publishes Release x64 with the .NET and Windows App SDK runtimes included and creates `artifacts/HoloWin-win-x64.zip`. Extract the complete ZIP on Windows 11 (build 22621 or later), then run `HoloWin.exe`. Share the whole ZIP, not the EXE alone: WinUI, audio and camera components require companion files. ARM64 native dependencies have not been validated.

After running scripts/download-hand-model.ps1, the equivalent app-folder publish command is:

```powershell
dotnet publish src/HoloWin.App/HoloWin.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/HoloWin-win-x64
```

Each recipient creates their own microphone and desk calibration. Application data stays in `%LOCALAPPDATA%\HoloWin`; it is not included in the distribution. To remove the application, disable Start at sign-in in Settings, close HoloWin, and delete its extracted folder. Reset data in Settings before removal if desired.

Publishing first cleans the app with self-contained settings, then rebuilds its resource index. A MakePRI check rejects packages that lack the WinUI theme resource. This prevents a prior framework-dependent build from leaving an incomplete resources.pri in the package. The development build remains framework-dependent. No certificate is created or installed by the publish script.

## Release workflow

`.github/workflows/release.yml` runs on `v*` tags or manual dispatch. It builds and tests Release, then calls `scripts/publish-single-exe.ps1`, tests extraction and themed WinUI startup from an isolated EXE, compares required extracted files with the payload, and uploads `HoloWin.exe` plus `HoloWin.exe.sha256` as the `HoloWin-standalone-win-x64` workflow artifact. Download and extract the artifact to obtain the EXE. After verification, the workflow publishes the EXE and checksum as assets on the tagged GitHub Release. The website links to the latest release EXE. Binaries are not signed.

## Camera support and future updates

The inner WinUI application is untrimmed and uses separate native files. The outer launcher bundles those files, the camera models and their license. Publishing runs download-hand-model.ps1, which downloads only missing or mismatched models and checks their pinned SHA-256 hashes. Runtime camera use does not need a download.

The ZIP includes `START-HERE.txt`, the README and license. A companion `HoloWin-win-x64.zip.sha256` file records the archive's SHA-256 checksum.

For an update, close HoloWin and extract the new release into a separate folder. Keep each release's DLLs together. Profiles and settings remain under `%LOCALAPPDATA%\HoloWin`; back up this folder before upgrading. If Start at sign-in is enabled, disable it before moving the app and re-enable it from the new location.

## Build output privacy

Workflows use checkout and runner environment paths rather than a developer's folders. They mask runner-specific paths and machine names in logs. Compiler source paths are mapped to `/_/HoloWin/`, and raw test reports are not uploaded. Keep personal profiles, logs, recordings and generated build folders out of commits; `.gitignore` covers the standard output locations.

## Application icon

The desktop executable and standalone launcher embed assets/HoloWin.ico. The window and tray use that icon too; README displays its PNG version. To change the artwork, edit assets/HoloWin.svg, run scripts/build-logo.ps1 and commit the regenerated files before publishing. The publish script and release extraction check require the icon and PNG in the payload.
