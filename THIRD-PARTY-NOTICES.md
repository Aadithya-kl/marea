# Third-party notices

Marea includes the following third-party material.

## Fluent UI System Icons

- Source: https://github.com/microsoft/fluentui-system-icons
- Used for: the icons of the desktop app (`src/Marea.App/Assets/Icons`, converted to XAML
  geometries in `src/Marea.App/Icons.xaml` by `tools/fetch_icons.py`).
- License: MIT. Full text in
  [`src/Marea.App/Assets/Icons/LICENSE-FluentUI-System-Icons.txt`](src/Marea.App/Assets/Icons/LICENSE-FluentUI-System-Icons.txt).

> Copyright (c) 2020 Microsoft Corporation

## NuGet packages

The app and the command line also use NuGet packages (Microsoft.Data.Sqlite, SQLitePCLRaw,
Microsoft.Windows.CsWin32 and their dependencies), each under its own license as listed on
nuget.org. They are restored at build time and are not redistributed as source here.
