# Contributing

HoloWin is an experimental Windows desktop application. Contributions should distinguish working application features from prototypes and extension contracts.

## Development setup

Use Windows 11 build 22621 or later and the .NET 10 SDK. See [README](README.md) for development runtime requirements.

```powershell
git clone https://github.com/rotsl/HoloWin.git
cd HoloWin
dotnet restore HoloWin.sln
.\scripts\download-hand-model.ps1
dotnet build HoloWin.sln -c Debug --no-restore
dotnet test HoloWin.sln -c Debug --no-build --no-restore
```

To build the distributable single EXE:

```powershell
.\scripts\publish-single-exe.ps1
```

The script builds the launcher project separately from the solution.

## Issues and pull requests

Report bugs in the [issue tracker](https://github.com/rotsl/HoloWin/issues). Include the application version, Windows build, relevant hardware, steps to reproduce, expected result, and actual result. Review logs before attaching them; remove private paths, action content, and credentials. See [Security](SECURITY.md) for vulnerability reporting.

A pull request should explain the problem, resulting behavior, validation performed, and remaining limitations. Keep unrelated changes separate. Update public documentation when behavior or commands change.

- Build successfully and resolve warnings introduced by the change. Nullable warnings are errors; not every compiler warning is currently treated as an error.
- Add meaningful regression tests for behavioral changes. Use the existing test projects under `tests/`; cross-module tests belong in `HoloWin.Integration.Tests`.
- Keep DSP and contracts independent of the UI. Connect application behavior through the shared `WorkspaceController`.
- Preserve explicit sensor activation, action approval, and confirmation controls.
- Handle profile compatibility deliberately. Include migration and round-trip coverage for persisted-format changes; do not discard existing user data.
- Report synthetic testing separately from physical device testing.

Use file-scoped namespaces, nullable reference types, and clear C# names. Prefer small changes with tests that verify observable behavior. Camera changes should state model requirements, licensing, activation behavior and hardware validation. Keep runtime capture opt-in.

## Licensing

Contributions must be compatible with the [MIT License](LICENSE), and contributors must have permission to submit them. Preserve applicable third-party copyright and license notices.

## Website

The public site uses MkDocs. Install requirements-docs.txt with Python, then run:

~~~powershell
python -m pip install -r requirements-docs.txt
python -m mkdocs serve
~~~

Edit website/pages/index.md for the page text and website/theme/ for its layout. The build copies the application logo from assets/. Check changes with python -m mkdocs build --strict. The website workflow deploys main through GitHub Pages; pull requests only build the site.