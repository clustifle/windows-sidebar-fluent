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

A continuous translucent rail inspired by [build 4074](https://betawiki.net/wiki/Windows_Longhorn_build_4074#Gallery). Search and pinned launchers sit at the top, tiles scroll through the middle, and the clock can dock at the bottom. Its default width is 168 logical pixels. Width and tile order are saved separately from ordinary sidebar mode. When the Windows taskbar is hidden, the rail covers the full monitor height.

**Keep Windows taskbar visible** lets both interfaces run together. Start and Apps buttons disappear from the rail in this mode. **Hide Windows taskbar tray and clock** clips the original system area while leaving Start and app buttons visible. This compatibility option preserves the original window region and restores it when disabled, on exit, or during crash recovery. If Windows does not expose a reliable tray region, no clipping is applied. Turn **Keep Windows taskbar visible** off to show the rail's blue Windows 11 Start logo and Apps launcher while hiding the Windows taskbar. **Restore Windows taskbar** shows it again without turning Longhorn mode off. Explorer keeps the desktop, files, and real Windows Start menu; the login shell is never changed.

Running-app and background-app tray strips are removed. The helper does not host the Windows tray or track running windows. It handles optional taskbar hiding, reversible system-area clipping, parent-process monitoring, and taskbar recovery. FluentSidebar's own settings/exit tray icon remains in the Windows notification area.

Quick Settings, Notifications, and Input Language open FluentSidebar's own WinUI flyouts. Quick Settings controls the default output volume and mute, shows connection status, and offers Wi-Fi/Bluetooth radio controls when Windows permits them. Radio access is requested only after the user changes a toggle. The language picker lists loaded Windows keyboard layouts and applies the selected layout to the previous application. There is no Windows Settings button in the rail. Flyouts open outside the narrow sidebar and use their own explicit light/dark surface; controls and notification icons use the Microsoft Fluent Options and Alert glyphs.

Notifications shows FluentSidebar alerts, including timer completion. Reading other apps' Windows notifications requires a signed packaged build with the notification-listener capability and user permission. The current unpackaged MSI cannot provide that access, and its panel says so instead of showing an unusable permission button.

Drag the clock into the tile area to undock it, or back to the bottom dock. Clock customization also includes this setting and a silver Longhorn face. Card heights stay fixed while live content changes; excess content scrolls inside each card. The calendar always reserves six weeks. The puzzle stays square and animates tile moves without recreating its buttons.

With auto-hide enabled, focusing the desktop keeps the sidebar visible. Switching to an app resumes edge auto-hide. A foreground-window event hook avoids continuous polling. The native window and backdrop move together; interrupted transitions reverse from their current position. Windows' reduced-animation preference is respected.

## Settings and card customization

Settings uses four tabs: Appearance, Gadgets, Longhorn, and Behavior. Each tab scrolls separately beneath its fixed header. Settings remains a native WinUI window with normal minimize, maximize, and close controls. Reopening a minimized Settings window restores it. Light/Dark/System content and backdrop themes stay synchronized.

Expand any gadget under Gadgets, or right-click its card and choose Customize. Every card offers locked height, padding, corner radius, and theme-based background opacity. Titled cards also support a custom title and hiding the heading. Reset affects only that card's appearance.

Content controls include clock faces/time zones/display; calendar week start, adjacent dates, and Today; note text size and writing height; performance refresh rate; timer duration and sound; storage drive; slideshow folder/interval/fit/file name; puzzle picture/numbered tiles/move count; feed address/count/interval; and Quick Launch columns/icon size/apps/websites.

Quick Launch has Pinned, Apps, and Web tabs. Import copies accessible taskbar `.lnk` shortcuts without changing Windows' pins. The Apps launcher is a searchable icon grid with lazily loaded icons. Typing in the rail search box suggests installed apps; selecting a suggestion launches it, while submitting another query opens Windows file search.

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

Publishing also builds the shell helper and includes its Windows desktop runtime. The two executables share the bundled .NET runtime files. The helper is not loaded in ordinary sidebar mode.

The MSI is written to `artifacts\installer`. `tools\Build-Icon.ps1` regenerates the multi-resolution app icon from the included Fluent artwork.

## Data and startup

Preferences, shortcut icons, and notes are stored in `%LOCALAPPDATA%\ClassicSidebar\settings.json`. Startup is optional and uses the current user's Run entry named `FluentSidebar` (the previous startup entry migrates automatically). The app does not upload preferences or notes. Network requests are made to a feed address only after the user configures it; article and website links open when clicked.

## Scope and verification

This is a new application, not Microsoft's original Sidebar or an original `.gadget` package runtime. Changing the Windows login shell, detached gadgets, and drag-out desktop placement are not implemented. Apps that choose explicit bounds or full-screen mode may cover reserved space.

Local checks cover repeated Light/Dark/System changes through the settings selector, build/startup, shell edge reservation and release, taskbar exclusion, app discovery and icon transparency, solvable puzzle generation/restoration, and bounded RSS/Atom parsing. Broader interactive testing is still needed for multiple monitors/DPI, Explorer restarts, accessibility themes, and every pointer/animation path.

## License

MIT. See [LICENSE](LICENSE). Microsoft Fluent System Icons artwork is separately attributed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Theme regression: publish with `-p:DefineConstants=THEME_REGRESSION` to a separate test folder and run that build with no other Sidebar instance open. It cycles the settings selector 24 times across Acrylic and Mica, checks content/backdrop themes, settings contrast, and gadget identity, restores the original theme, and writes `theme-test-result.txt` beside the test executable. Normal builds exclude this test.
