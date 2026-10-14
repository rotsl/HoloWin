# Application artwork

HoloWin.svg is the editable logo. HoloWin.png is the 512-pixel README image, and HoloWin.ico contains 16, 24, 32, 48, 64, 128 and 256-pixel versions for Windows.

To regenerate the PNG and icon on Windows:

~~~powershell
.\scripts\build-logo.ps1
~~~

The script renders the rectangles and circle from the SVG using System.Drawing. Commit all three assets after changing the design; normal builds use the committed files.

The desktop app and standalone launcher both embed HoloWin.ico. The desktop window and tray load the same icon from Assets/. README places the PNG at the top right. Other PNG assets remain available for the retained package manifest; the app is distributed without Microsoft Store or MSIX installation.

See [Packaging](../docs/PACKAGING.md).
