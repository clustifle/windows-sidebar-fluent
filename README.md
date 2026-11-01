# FluentSidebar

<img src="Assets/Sidebar.svg" width="80" alt="FluentSidebar icon">

A native C# / WinUI 3 desktop sidebar inspired by Windows Vista and Windows 7, with modern Windows materials and controls.

## Install

Download the x64 MSI from [Releases](https://github.com/clustifle/windows-sidebar-fluent/releases). It installs for the current user, creates desktop and Start menu shortcuts, and supports uninstall through Windows Settings. User notes and preferences are kept when uninstalling.

Windows 10 version 2004 or newer is required; Windows 11 is recommended for Mica. The package includes the .NET and Windows App SDK runtimes. The installer is not code-signed.

## Using the sidebar

- Use the small previous/next buttons to change gadget pages; **+** opens the gadget manager.
- Drag a gadget by its card background or heading to reorder it, including the clock. Hold over a page arrow to move between pages.
- Right-click for Settings, gadget selection, clock and Quick Launch customization, startup, and Exit.
- The app stays out of the taskbar and Alt+Tab. Its tray icon may appear in the Windows tray overflow.
- Settings offer Light, Dark, or System theme; Acrylic/Mica; width; left/right docking; and optional animated auto-hide.
- Docked mode reserves desktop space. Auto-hide keeps only a thin reveal strip and overlays the sidebar when opened.

## Longhorn mode

A continuous translucent rail inspired by [build 4074](https://betawiki.net/wiki/Windows_Longhorn_build_4074#Gallery). Search and pinned launchers sit at the top, tiles scroll through the middle, and the clock can dock at the bottom. Its default width is 168 logical pixels. Width and tile order are saved separately from ordinary sidebar mode.

The original Windows taskbar stays visible and intact, including Start, running apps, tray, clock, Quick Settings, notifications, and input controls. The rail has no Start or Apps button, Quick Settings, Notifications, or Language Input Switcher. Old taskbar-hiding and tray-clipping preferences are reset during startup. A recovery helper runs briefly only if an older version left taskbar recovery data; neither sidebar mode keeps a shell helper running.

Drag the clock into the tile area to undock it, or back to the bottom dock. Clock customization includes this setting and a silver Longhorn face. Card heights stay fixed while live content changes; excess content scrolls inside each card. The calendar reserves six weeks, and the puzzle stays square.

With auto-hide enabled, focusing the desktop keeps the sidebar visible. Switching to an app resumes edge auto-hide. A foreground-window event hook avoids continuous polling. Transitions reverse from their current position and respect Windows' reduced-animation preference.

## Settings and card customization

Settings uses four tabs: Appearance, Gadgets, Longhorn, and Behavior. Each tab scrolls separately beneath its fixed header. Settings remains a native WinUI window with normal minimize, maximize, and close controls. Reopening a minimized Settings window restores it. Light/Dark/System content and backdrop themes stay synchronized.

Expand any gadget under Gadgets, or right-click its card and choose Customize. Every card offers locked height, padding, corner radius, and theme-based background opacity. Titled cards also support a custom title and hiding the heading. Reset affects only that card's appearance.

Content controls include clock faces/time zones/display; calendar week start, adjacent dates, and Today; note text size and writing height; performance refresh rate; timer duration and sound; storage drive; slideshow folder/interval/fit/file name; puzzle picture/numbered tiles/move count; feed address/count/interval; and Quick Launch columns/icon size/apps/websites.

Quick Launch has Pinned, Apps, and Web tabs. Import copies accessible taskbar `.lnk` shortcuts without changing Windows' pins. Installed apps can be selected in Quick Launch customization. Rail search suggestions include app icons. Selecting an app launches it; submitting another query opens Windows file search.

Background work follows visibility: offscreen gadgets skip live updates, hidden auto-hide mode slows the gadget timer, and slideshow/panel polling stops when unused. App icons use a bounded memory cache. A visible second hand or active countdown still requires one-second updates. Neither mode keeps a background shell helper running.

Sidebar renegotiates screen space on taskbar/work-area changes, including top, bottom, left, and right taskbars. Broader testing on movable-taskbar releases and multiple displays is still needed.

## Gadgets

| Gadget | Features |
| --- | --- |
| Clock | Minimal, Classic, Roman, Railway, Midnight, Ivory, and Longhorn faces; analog/digital; size; time zone; 12/24-hour time; date, seconds, second hand, and label |
| Calendar | Fixed six-week grid, adjacent-month dates, selection, month navigation, and Today |
| Notes | Local autosave and adjustable height |
| CPU meter | CPU and physical-memory gauges |
| Slide Show | Local pictures, previous/next, pause/play, adjustable interval |
| Picture Puzzle | Solvable sliding puzzle, optional local picture, saved progress |
| Feed Headlines | RSS/Atom, configurable headline count and interval, visible-only refresh |
| Quick Launch | Search installed apps, embedded app icons, website shortcuts, rename/reorder/remove |
| Timer | Start, pause/resume, reset, completion sound |
| Storage | System-drive usage and free space |

Additional gadgets can be enabled from **+**. Picture folders, puzzle pictures, and feed URLs are chosen by the user. No example headlines or weather values are presented as live data.

App search opens its results toward the desktop, with a fixed readable panel width, app icons, and full-name tooltips. Auto-hide waits while the results are open. Moving the rail closes the results first.

## Build from source

Install the .NET 10 SDK and Visual Studio WinUI/Windows SDK build tools. Then:

```powershell
dotnet publish Sidebar.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o 'artifacts\FluentSidebar'
```

Run `artifacts\FluentSidebar\FluentSidebar.exe`. Keep the supporting files together.

### Build the MSI

Install WiX 6.0.2 and run the installer authoring script:

```powershell
dotnet tool install --global wix --version 6.0.2 --allow-roll-forward
.\tools\Build-Installer.ps1
```

Publishing also builds the shell helper and includes its Windows desktop runtime. The two executables share the bundled .NET runtime files. The helper is used only to restore taskbar state left by an older version, then exits.

The MSI is written to `artifacts\installer`. `tools\Build-Icon.ps1` regenerates the multi-resolution app icon from the included Fluent artwork.

## Data and startup

Preferences, shortcut icons, and notes are stored in `%LOCALAPPDATA%\ClassicSidebar\settings.json`. Startup is optional and uses the current user's Run entry named `FluentSidebar` (the previous startup entry migrates automatically). The app does not upload preferences or notes. Network requests are made to a feed address only after the user configures it; article and website links open when clicked.

## Scope and verification

This is a new application, not Microsoft's original Sidebar or an original `.gadget` package runtime. Changing the Windows login shell, detached gadgets, and drag-out desktop placement are not implemented. Apps that choose explicit bounds or full-screen mode may cover reserved space.

Local checks cover repeated Light/Dark/System changes through the settings selector, build/startup, shell edge reservation and release, taskbar exclusion, app discovery and icon transparency, solvable puzzle generation/restoration, and bounded RSS/Atom parsing. Broader interactive testing is still needed for multiple monitors/DPI, Explorer restarts, accessibility themes, and every pointer/animation path.

## License

MIT. See [LICENSE](LICENSE). Microsoft Fluent System Icons artwork is separately attributed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Theme regression: publish with `-p:DefineConstants=THEME_REGRESSION` to a separate test folder and run that build with no other Sidebar instance open. It cycles the settings selector 24 times across Acrylic and Mica, checks content/backdrop themes, settings contrast, and gadget identity, restores the original theme, and writes `theme-test-result.txt` beside the test executable. Normal builds exclude this test.
