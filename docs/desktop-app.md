# Invora in its own application window

Both releases can open the shop in a dedicated window with an app icon, Back/Reload controls and downloads. There is no browser address bar. Mac uses system **WKWebView**; Windows uses **Microsoft Edge WebView2**. This is the existing Invora UI in a native window, with the same accounts, permissions, invoices and stock records. The window can connect to a hosted HTTPS shop without Docker on the customer's computer; see [deployment modes](deployment-modes.md).

| Platform | Install and open |
|---|---|
| Mac, hosted | Open `Invora-Mac.dmg`, drag Invora to Applications, choose **Connect to hosted shop…** and enter its HTTPS address. Docker is not needed on this Mac. |
| Windows, hosted | Run the standalone `Invora.Desktop.exe` and enter the shop's HTTPS address. Docker is not needed on this PC. WebView2 Runtime is required. |
| Mac, local | Open `Invora-Mac.dmg`, drag Invora to Applications, start Docker Desktop, open Invora and choose **Start shop**. [Mac guide](macos-installation.md) |
| Windows, local | Install Docker Desktop, run `InvoraSetup.exe`, then use **Start Invora**. [Windows guide](windows-installation.md) |

Local installations still communicate internally through `http://127.0.0.1:8080` (or your configured loopback port). You do not need to type that address for daily use. Docker runs the API and PostgreSQL in the background. Closing the window leaves those services running; **Stop shop / Stop Invora** stops them while retaining records. Docker must be running and the computer awake.

Use **Open in browser** for a regular browser tab. Windows also supplies `Open-InvoraBrowser.cmd`. Downloads show a native save dialog; open a saved invoice or sticker PDF to print with the normal system PDF viewer. External WhatsApp/web/email/phone links leave the embedded shop window and use the OS handler. Neither host exposes a JavaScript filesystem bridge.

A dedicated HTTPS domain requires the server deployment described in [deployment](deployment.md). The desktop packages do not provision a domain or hosting. Multiple PCs can access that hosted shop in the desktop window or a browser. Installing independent local copies creates separate databases, with no automatic sync; choose one shared server when multiple computers must use the same live stock.

For local installations, records stay in Docker's database/documents volumes. For hosted installations, records stay on the configured shop server. Window profiles hold sessions/cache only. [Storage and security](data-storage-security.md) explains disk protection, permissions and backups. This release does not make local database files encrypted automatically.

The Mac app is currently ad-hoc signed, not Developer ID signed/notarized. Windows executables require vendor code signing for public distribution. Mac native-window navigation, form entry and synthetic PDF/CSV downloads are tested on Apple silicon. Intel runtime and native Windows WebView2 execution still require target-machine acceptance.
