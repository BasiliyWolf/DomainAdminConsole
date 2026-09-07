# Domain Admin Console v0.3.4

UI/UX update for the administration tables and domain status area.

- Table context menus now mirror the action buttons located above the corresponding table (refresh, start/stop, close, delete, export, etc.) while keeping copy-cell/row/table actions.
- Right-click selects the row before a contextual action is executed.
- Added a WinRM loading overlay with an animated marquee progress bar while remote data is being loaded.
- Added standard Windows system icons to application buttons and mirrored context-menu commands.
- Moved Active Directory scan progress and statistics to the bottom StatusStrip.
- The StatusStrip now also shows the current WinRM connection and Active Directory domain name.
- Simplified the left computer pane by removing scan/progress rows from its header.
- Added Refresh domain / Stop actions to the domain-computer context menu.

All fixes from v0.3.3 are retained.
