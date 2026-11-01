FluentSidebar 1.2.0 — preview

Windows Sidebar Fluent is now FluentSidebar. The installer upgrades the previous app and creates a FluentSidebar desktop shortcut. Existing notes and preferences are kept.

Longhorn mode has been rebuilt around the layout in build 4074: a narrow translucent rail, search and launchers at the top, scrolling tiles, and an optional bottom clock. The rail keeps its own width and tile order. Drag the clock into the tile area or back to its bottom dock.

Keep Windows taskbar visible lets both interfaces run together. Start and Apps disappear from the rail while that option is on. With the taskbar hidden, the rail shows the blue Windows 11 Start logo and Apps launcher. Restore Windows taskbar leaves Longhorn mode enabled. Hide Windows taskbar tray and clock removes the original system area using reversible window clipping; Start and app buttons remain visible. The original region is saved for exit and crash recovery.

The running-app and background-app tray strips are gone. The helper no longer hosts the Windows tray or tracks app windows. Quick Settings, Notifications, and Input Language now use FluentSidebar's own WinUI panels. The Windows Settings button has been removed. All three panels can extend outside the rail, and their surfaces follow the selected theme. Quick Settings and Notifications now use the proper Fluent controls and bell icons.

Quick Settings includes output volume, mute, connection status, and supported Wi-Fi/Bluetooth controls. The input picker selects a loaded Windows keyboard layout. Notifications includes FluentSidebar alerts; access to other apps' Windows notifications still requires a signed packaged build and Windows permission, which this MSI does not provide.

Settings is organized into Appearance, Gadgets, Longhorn, and Behavior. Loading saved settings no longer triggers unnecessary gadget rebuilds. Minimized Settings windows restore when reopened, and settings materials follow the sidebar selection.

Every gadget has a customization panel. Cards offer a locked height, padding, corners, and background opacity, plus relevant content options. The calendar keeps six weeks, supports date selection, and can change its first weekday. The puzzle stays square, keeps keyboard focus through tile moves, and animates its existing tiles.

Auto-hide keeps the sidebar visible on the desktop and resumes when an app becomes active. The window and backdrop move together, and transitions reverse from their current position.

This is an unsigned preview. Multiple monitors, unusual taskbar modifications, radio permissions, and app-specific input behavior still need broader testing.
