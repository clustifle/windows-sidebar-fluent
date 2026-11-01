# FluentSidebar 2.0.0 — final feature release outline

Status: proposed scope, not implemented. Based on the current 1.3.4 application. “Final” means feature-complete for this release; security and Windows compatibility fixes continue in 2.0.x.

## Implementation status

The 2.0.0 launcher work adds the native app grid, pinning, an on-demand window list, a searchable categorized gadget gallery, independent operating-mode preferences, and Fluent/Vista/Longhorn appearance presets. Validation and packaging are tracked separately. Replacement desktop hosting, tray hosting, sign-in shell integration, detached/multiple gadget instances, and the additional gadget providers below remain future work. The 2.0.0 version number does not indicate that these replacement-shell gates have passed.

## Product direction

A native WinUI 3 desktop shell and gadget platform combining Longhorn's continuous information rail, Vista's individual desktop gadgets, and Fluent controls. Keep the name FluentSidebar. Avoid a large dashboard, oversized rounded cards, generic icon substitutes, and permanently visible editing controls.

Appearance and shell ownership are independent settings. A Longhorn appearance must never silently hide the Windows taskbar or change the sign-in shell.

## Reference findings

- The user's earlier build 4074 screenshot shows a narrow continuous rail, compact search and launcher content, thin separators, and a clock near the bottom. A horizontal Windows taskbar is still present. Reproduce this structure as an appearance preset, independently of replacement-shell behavior.
- The Vista screenshot inspected in the browser shows individually styled gadgets, an analog clock without a large rectangular container, small add/page controls, and a searchable gallery with named gadget previews. Preserve each gadget's identity and offer desktop placement.
- Historical Sidebar designs included information tiles, actionable flyouts, Quick Launch, clocks, and tray-related functionality. These are useful design references, not a promise that discontinued Windows services or binary gadget packages will work.
- BetaWiki's supplied pages blocked direct retrieval. Its indexed article, the user's supplied 4074 screenshot, BetaArchive's historical description, and a contemporary Vista screenshot were used. Fan-made Longhorn mockups are not reference evidence.

References:

- https://betawiki.net/wiki/Windows_Sidebar
- https://betawiki.net/wiki/Windows_Longhorn_build_4074#Gallery
- https://www.betaarchive.com/wiki/index.php/Windows_Sidebar
- https://www.bit-tech.net/reviews/Windows_Vista_review/2/
- Vista screenshot: https://images.bit-tech.net/content_images/2007/01/Windows_Vista_review/sidebar1_large.jpg

## Operating modes

| Mode | Responsibilities | Default |
| --- | --- | --- |
| Desktop Sidebar | Gadgets, app search, pins, desktop placement; Explorer owns the desktop and taskbar | Existing users migrate here, preserving their appearance |
| Launcher | Sidebar plus a full app launcher and optional task switcher; Explorer remains available | Explicit opt-in; taskbar stays visible unless separately changed |
| Replacement Shell | FluentSidebar owns desktop surface, launch surface, running-window list, tray host, and system controls | Advanced opt-in after compatibility and recovery checks |

Replacement Shell is the full v2 objective. It must be an actual working shell, not merely Explorer's taskbar hidden beneath a sidebar. The existing shell helper currently restores legacy changes and exits; it does not provide these replacement functions.

Microsoft's built-in Shell Launcher provisioning is supported on Enterprise, Education, and IoT Enterprise editions. Do not advertise that mechanism as a Home/Pro feature. Any separate Home/Pro sign-in integration needs its own documented implementation, compatibility tests, and rollback path before being listed as supported.

Reference: https://learn.microsoft.com/en-us/windows/configuration/shell-launcher/

## Appearance and interaction

Three presets share one layout engine:

- Fluent: restrained surfaces, native typography, clear focus and selection states.
- Longhorn 4074: a slim full-height rail, continuous material, small separators, compact tiles, optional bottom clock, search and pinned icons at the top.
- Vista: distinct gadget faces, a subtle sidebar gradient/material, minimal page/add controls, optional detached gadgets.

All presets support Light, Dark, and System; left/right docking; per-monitor placement; saved rail width; and exportable layouts. Use licensed Windows 11 artwork where appropriate, real app icons, and Microsoft Fluent System Icons for commands. In companion modes, do not duplicate Start or taskbar controls by default. In replacement mode, provide a Windows 11-style Start entry without claiming to embed Microsoft's Start menu.

Gadgets retain fixed chosen sizes while updating. Drag their background or heading; interactive controls keep their normal behavior. Editing commands appear on right-click or keyboard focus. Docking and reordering animate without shifting unrelated cards. Flyouts open into available desktop space, stay on the correct monitor, and close safely when docking changes.

Auto-hide keeps the rail visible on the desktop, holds while a flyout or drag is active, reverses smoothly, yields to genuine fullscreen applications, and respects reduced motion. Mica/Acrylic must have legible solid fallbacks for inactive windows, disabled transparency, high contrast, and system policy; continuous blur is not guaranteed by Windows.

Reference: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/system-backdrop-controller

## Complete shell functions

1. **Launcher and search:** searchable installed-app grid with authentic icons, keyboard navigation, pins and folders, app names and tooltips, websites, files/folders, and clear result categories. Reuse Windows indexing where available rather than recursively scanning the disk on every keystroke.
2. **Running windows:** optional compact task tile in launcher mode and a complete task surface in replacement mode. Group windows by application, activate/minimize/restore, close, show previews where supported, and handle multiple monitors, elevated windows, and app restarts. Do not embed arbitrary application windows inside gadget cards.
3. **Desktop:** wallpaper, desktop shortcuts and file icons, selection and context actions, Recycle Bin access, drag/drop, and Show Desktop behavior. File Explorer remains a file manager that can be launched when needed. Its behavior under replacement mode needs explicit testing.
4. **Tray:** a real tray-host implementation in the isolated shell broker, including overflow, clicks, context menus, tooltips, icon replacement/removal, and application re-registration after host restart. Do not crop or reparent Explorer's tray as the production solution. A compatibility test must cover classic and modern apps before Explorer's taskbar can be removed.
5. **System controls:** native FluentSidebar panels for volume/output devices, brightness where supported, network and battery state, power/session commands, and links to complete Windows settings. Each control needs a capability check and a clear unavailable state. A panel that only opens Windows Quick Settings is a shortcut, not a replacement.
6. **Notifications:** separate FluentSidebar alerts from notifications exposed by Windows' notification-listener APIs. Request access only when enabled; handle denial/revocation and test packaging requirements. Do not promise every Windows notification or inline action will be available. Notification completeness is a release gate for any replacement configuration that removes the native notification surface.
7. **Recovery:** a separate minimal recovery host, bounded restart attempts, a documented emergency route through Task Manager, and an always-accessible Restore Windows shell action. Failed startup, upgrades, uninstall, and corrupt settings must return the user to a usable desktop.

The previous removals of Start, Apps, tray, language, Quick Settings, and notifications remain intact in Desktop Sidebar mode. Replacement mode introduces its own required equivalents. Do not restore the abandoned tray-masking approach.

Notification reference: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener

## Gadget platform and gallery

One searchable native gallery with previews, categories, descriptions, enabled state, and per-gadget customization. Support multiple instances, drag to desktop and redock, lock position, compact/expanded sizes, and per-instance settings.

| Category | v2 scope |
| --- | --- |
| Time | Clock faces, digital/analog variants, world clocks, alarms, calendar, timer and stopwatch |
| Personal | Notes, checklist, pinned folders and optional local contacts |
| System | CPU/RAM, storage, battery, network status/throughput and Recycle Bin |
| Media | Slideshow, picture puzzle with completion merge, and media-session playback controls where supported |
| Information | Multiple RSS/Atom subscriptions with thumbnails; optional weather, currency and stocks only with a chosen working provider and clear timestamps |
| Launch | App/web pins, searchable app grid, folders and compact running-window tile |

Keep current settings and puzzle progress. Add reset-one-card, reset-layout, export/import, and unavailable-source recovery. Weather, financial data, contacts, and other external services must disclose their provider and any account/key requirements; do not display fabricated live values.

Define a versioned gadget contract: identity, configuration, supported sizes, visibility, refresh schedule, cancellation, disposal, accessible name and commands. Built-ins can share the UI process; third-party providers should execute out of process behind explicit permissions and resource limits. Original Vista HTML/JavaScript/ActiveX `.gadget` compatibility is outside the v2 release scope; classic gadgets are native recreations.

## Architecture and performance

- Move the growing MainWindow partial classes toward separate layout, gadget, launcher, settings, and shell services.
- Keep WinUI 3 as the user interface. Use a separate Win32 shell broker only for replacement-shell duties, plus a small recovery host that does not depend on a working WinUI window.
- Use event-driven window/device updates, visibility-aware gadget scheduling, cancellation, bounded caches, and lazy popup creation. No permanent high-frequency process/window scans, forced garbage collections, or working-set trimming.
- Load only enabled gadget providers. Dispose detached instances, image sources, event subscriptions, and shell hooks when their owning surface closes.
- Proposed performance targets, to be measured on a documented reference PC: under 0.5% average idle CPU; under 150 MB private memory in the default companion layout and under 250 MB combined across replacement-shell processes. Active media, image decoding, second hands, and third-party gadgets are measured separately. These are targets, not current guarantees.
- Measure cold/warm startup, search latency, animation frame pacing, total process-tree memory, and a 24-hour idle/use session. A budget regression blocks release unless justified and documented.

## Delivery sequence and acceptance

1. **2.0 alpha 1 — foundation:** separate services, versioned settings migration, shared layout engine, gallery, native gadget customization and desktop placement.
2. **2.0 alpha 2 — launcher:** app catalog/search, pins, optional task switching, keyboard/accessibility and multi-monitor behavior.
3. **2.0 beta — shell:** desktop host, tray broker, system capabilities, notification integration and recovery. Test first in disposable VMs and a separate test account.
4. **2.0 release candidate — stabilization:** feature freeze, installer/upgrade/uninstall rehearsals, Explorer and broker restart tests, DPI/monitor hotplug, Show Desktop, fullscreen, sleep/resume, offline providers, denied permissions, and theme/material transitions.
5. **2.0.0 — final feature release:** publish the supported configuration matrix, measured performance, known limitations, source, license notices, checksums, release notes, and full installer. Code-sign the release when a signing identity is available; do not describe unsigned builds as signed.

The installer offers operating mode and appearance separately. Default to the existing desktop companion experience. Sign-in-shell changes are an explicit advanced step, with a verified rollback and servicing account. Repair preserves settings; uninstall restores ownership and desktop work areas without deleting notes.

Release gates: no inaccessible desktop after a crash; no taskbar gaps or duplicate appbar reservations; no lost settings; correct contrast and focus; no stale popup after monitor/layout changes; safe image/feed handling; no persistent idle leak; and proven app/tray compatibility for each advertised replacement-shell configuration. If a gate fails, that configuration stays preview rather than being labeled complete.
