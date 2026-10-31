# Windows Sidebar Fluent

<img src="Assets/Sidebar.svg" width="80" alt="Windows Sidebar Fluent icon">

A native C# / WinUI 3 desktop sidebar inspired by Windows Vista and Windows 7, with modern Windows materials and controls.

## Install

Download the x64 MSI from [Releases](https://github.com/clustifle/windows-sidebar-fluent/releases). It installs for the current user, creates desktop and Start menu shortcuts, and supports uninstall through Windows Settings. User notes and preferences are kept when uninstalling.

Windows 10 version 2004 or newer is required; Windows 11 is recommended for Mica. The package includes the .NET and Windows App SDK runtimes. The installer is not code-signed.

## Using the sidebar

- Use the small previous/next buttons to change gadget pages; **+** opens the gadget manager.
- Drag a gadget by its grip to reorder it, including the clock. Hold over a page arrow to move between pages.
- Right-click for Settings, gadget selection, clock and Quick Launch customization, startup, and Exit.
- The app stays out of the taskbar and Alt+Tab. Its tray icon may appear in the Windows tray overflow.
- Settings offer Light, Dark, or System theme; Acrylic/Mica; width; left/right docking; and optional animated auto-hide.
- Docked mode reserves desktop space. Auto-hide keeps only a thin reveal strip and overlays the sidebar when opened.

## Gadgets

| Gadget | Features |
| --- | --- |
| Clock | Minimal, Classic, and Roman faces; analog/digital; size; time zone; 12/24-hour time; date, seconds, second hand, and label |
| Calendar | Month navigation and Today |
| Notes | Local autosave and adjustable height |
| CPU meter | CPU and physical-memory gauges |
| Slide Show | Local pictures, previous/next, pause/play, adjustable interval |
| Picture Puzzle | Solvable sliding puzzle, optional local picture, saved progress |
| Feed Headlines | RSS/Atom, five headlines, visible-only refresh |
| Quick Launch | Search installed apps, embedded app icons, website shortcuts, rename/reorder/remove |
| Timer | Start, pause/resume, reset, completion sound |
| Storage | System-drive usage and free space |

Additional gadgets can be enabled from **+**. Picture folders, puzzle pictures, and feed URLs are chosen by the user. No example headlines or weather values are presented as live data.

## Build from source

Install the .NET 10 SDK and Visual Studio WinUI/Windows SDK build tools. Then:

```powershell
dotnet publish Sidebar.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o 'artifacts\Windows Sidebar Fluent'
```

Run `artifacts\Windows Sidebar Fluent\Windows Sidebar Fluent.exe`. Keep the supporting files together.

### Build the MSI

Install WiX 6.0.2 and run the installer authoring script:

```powershell
dotnet tool install --global wix --version 6.0.2 --allow-roll-forward
.\tools\Build-Installer.ps1
```

The MSI is written to `artifacts\installer`. `tools\Build-Icon.ps1` regenerates the multi-resolution app icon from the included Fluent artwork.

## Data and startup

Preferences, shortcut icons, and notes are stored in `%LOCALAPPDATA%\ClassicSidebar\settings.json`. Startup is optional and uses the current user's Run entry named `ClassicFluentSidebar`. The app does not upload preferences or notes. Network requests are made to a feed address only after the user configures it; article and website links open when clicked.

## Scope and verification

This is a new application, not Microsoft's original Sidebar or an original `.gadget` package runtime. Explorer replacement, detached gadgets, and drag-out desktop placement are not implemented. Apps that choose explicit bounds or full-screen mode may cover reserved space.

Local checks cover repeated Light/Dark/System changes through the settings selector, build/startup, shell edge reservation and release, taskbar exclusion, app discovery and icon transparency, solvable puzzle generation/restoration, and bounded RSS/Atom parsing. Broader interactive testing is still needed for multiple monitors/DPI, Explorer restarts, accessibility themes, and every pointer/animation path.

## License

MIT. See [LICENSE](LICENSE). Microsoft Fluent System Icons artwork is separately attributed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Theme regression: publish with `-p:DefineConstants=THEME_REGRESSION` to a separate test folder and run that build with no other Sidebar instance open. It cycles the settings selector six times, checks actual themes and gadget identity, restores the original theme, and writes `theme-test-result.txt` beside the test executable. Normal builds exclude this test.
