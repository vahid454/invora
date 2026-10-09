import AppKit
import Darwin

umask(0o077)
let resourceRoot = Bundle.main.resourceURL ?? URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let engine = InvoraLauncher(release: resourceRoot.appendingPathComponent("release"))

if CommandLine.arguments.count > 1 && CommandLine.arguments[1] != "--preview" {
    do {
        engine.report = { print($0) }
        switch CommandLine.arguments[1] {
        case "--verify-release":
            let checker = CommandLine.arguments.count > 2 ? InvoraLauncher(release: URL(fileURLWithPath: CommandLine.arguments[2])) : engine
            print("Verified \(try checker.verifyRelease().count) release files.")
        case "--check": _ = try engine.check()
        case "--start-no-browser": _ = try engine.start()
        default: throw LauncherError(message: "Use --verify-release [folder], --check or --start-no-browser.")
        }
        exit(0)
    } catch { fputs("Invora: \(error.localizedDescription)\n", stderr); exit(1) }
}

final class ShopSurface: NSView {
    override var isOpaque: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedRed: 0.965, green: 0.965, blue: 0.938, alpha: 1).setFill()
        dirtyRect.fill()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    var window: NSWindow!
    var shop: ShopWindow?
    let status = NSTextField(wrappingLabelWithString: "Start Docker Desktop, then choose Start shop. Your shop opens in its own Invora window.")
    let output = NSTextView()
    var actions: [NSButton] = []
    var busy = false
    let green = NSColor(calibratedRed: 0.08, green: 0.25, blue: 0.20, alpha: 1)

    func applicationDidFinishLaunching(_ notification: Notification) {
        let mainMenu = NSMenu()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "Quit Invora", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let item = NSMenuItem(); item.submenu = appMenu; mainMenu.addItem(item)
        let edit = NSMenu(title: "Edit")
        for (title, action, key) in [("Undo", "undo:", "z"), ("Redo", "redo:", "Z"), ("Cut", "cut:", "x"), ("Copy", "copy:", "c"), ("Paste", "paste:", "v"), ("Select All", "selectAll:", "a")] { edit.addItem(withTitle: title, action: NSSelectorFromString(action), keyEquivalent: key) }
        let editItem = NSMenuItem(title: "Edit", action: nil, keyEquivalent: ""); editItem.submenu = edit; mainMenu.addItem(editItem)
        NSApp.mainMenu = mainMenu
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 660, height: 540),
                          styleMask: [.titled, .closable, .miniaturizable], backing: .buffered, defer: false)
        window.title = "Invora · Your shop on this Mac"
        window.appearance = NSAppearance(named: .aqua)
        window.delegate = self
        window.isReleasedWhenClosed = false
        let view = ShopSurface(); window.contentView = view
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false; view.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 28), stack.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -28), stack.topAnchor.constraint(equalTo: view.topAnchor, constant: 24), stack.bottomAnchor.constraint(equalTo: view.bottomAnchor, constant: -24)])
        let title = NSTextField(labelWithString: "invora."); title.font = .systemFont(ofSize: 38, weight: .bold); title.textColor = green
        stack.addArrangedSubview(title)
        let subtitle = NSTextField(wrappingLabelWithString: "Inventory, billing and LenDen. Stored on your Mac.")
        subtitle.font = .systemFont(ofSize: 15); stack.addArrangedSubview(subtitle)
        status.font = .systemFont(ofSize: 13); status.textColor = .secondaryLabelColor
        status.heightAnchor.constraint(greaterThanOrEqualToConstant: 38).isActive = true
        stack.addArrangedSubview(status)
        let primary = NSStackView(); primary.orientation = .horizontal; primary.spacing = 10
        let start = button("Start shop", #selector(startShop)); start.keyEquivalent = "\r"; start.bezelColor = green
        primary.addArrangedSubview(start)
        primary.addArrangedSubview(button("Check status", #selector(checkStatus)))
        primary.addArrangedSubview(button("Stop shop", #selector(stopShop)))
        stack.addArrangedSubview(primary)
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.borderType = .bezelBorder
        output.isEditable = false; output.isSelectable = true; output.font = .monospacedSystemFont(ofSize: 12, weight: .regular)
        output.textContainerInset = NSSize(width: 10, height: 10)
        output.string = "Welcome to Invora.\n\nStart shop opens your billing workspace.\nCheck status helps diagnose startup problems.\nStop shop keeps your database and uploaded files.\n\nFirst installation may take several minutes.\nProgress and any next steps appear here."
        output.autoresizingMask = [.width]; output.isVerticallyResizable = true
        output.textContainer?.widthTracksTextView = true
        scroll.documentView = output; stack.addArrangedSubview(scroll)
        scroll.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 190).isActive = true
        let secondary = NSStackView(); secondary.orientation = .horizontal; secondary.spacing = 10
        secondary.addArrangedSubview(button("Update release…", #selector(updateRelease)))
        secondary.addArrangedSubview(button("Application folder", #selector(openFolder)))
        secondary.addArrangedSubview(button("Installation guide", #selector(openGuide)))
        stack.addArrangedSubview(secondary)
        let note = NSTextField(wrappingLabelWithString: "Stopping or closing this launcher keeps your records. Keep Docker running while using the shop.")
        note.font = .systemFont(ofSize: 11); note.textColor = .secondaryLabelColor; stack.addArrangedSubview(note)
        for label in [subtitle, status, note] { label.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true }
        engine.report = { [weak self] line in DispatchQueue.main.async { self?.append(line) } }
        window.center(); window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        if CommandLine.arguments.count == 3 && CommandLine.arguments[1] == "--preview" {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) {
                view.layoutSubtreeIfNeeded()
                if let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                    view.cacheDisplay(in: view.bounds, to: bitmap)
                    if let png = bitmap.representation(using: .png, properties: [:]) {
                        do { try png.write(to: URL(fileURLWithPath: CommandLine.arguments[2])) }
                        catch { fputs("Preview could not be written.\n", stderr); exit(1) }
                    }
                }
                NSApp.terminate(nil)
            }
        }
    }

    func button(_ title: String, _ selector: Selector) -> NSButton {
        let b = NSButton(title: title, target: self, action: selector)
        b.bezelStyle = .rounded; b.controlSize = .large; actions.append(b); return b
    }
    func append(_ text: String) {
        output.textStorage?.append(NSAttributedString(string: text + "\n", attributes: [.font: NSFont.monospacedSystemFont(ofSize: 12, weight: .regular), .foregroundColor: NSColor.labelColor]))
        output.scrollToEndOfDocument(nil); status.stringValue = text
    }
    func perform(_ action: @escaping () throws -> String?) {
        guard !busy else { return }; busy = true; actions.forEach { $0.isEnabled = false }
        append("Working…")
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let url = try action()
                DispatchQueue.main.async {
                    if let url, let address = URL(string: url) { self.openShop(address) }
                    self.busy = false; self.actions.forEach { $0.isEnabled = true }
                }
            } catch {
                DispatchQueue.main.async {
                    self.append(error.localizedDescription)
                    self.busy = false; self.actions.forEach { $0.isEnabled = true }
                    let alert = NSAlert(); alert.messageText = "Invora needs attention"; alert.informativeText = error.localizedDescription
                    alert.alertStyle = .warning; alert.beginSheetModal(for: self.window)
                }
            }
        }
    }
    func openShop(_ address: URL) {
        if shop?.origin != address {
            shop?.window.close()
            shop = ShopWindow(origin: address)
            shop?.onClose = { [weak self] in self?.window.makeKeyAndOrderFront(nil) }
        }
        shop?.show(); window.orderOut(nil)
    }
    @objc func startShop() { perform { try engine.start() } }
    @objc func checkStatus() { perform { _ = try engine.check(); return nil } }
    @objc func stopShop() { shop?.window.close(); shop = nil; perform { try engine.stop(); return nil } }
    @objc func updateRelease() {
        let alert = NSAlert(); alert.messageText = "Install this release into your shop?"
        alert.informativeText = "Take a current database and uploads backup first. This replaces application files, rebuilds the release and briefly stops billing for migrations. Your private .env and data volumes are retained."
        alert.addButton(withTitle: "Update release"); alert.addButton(withTitle: "Cancel")
        alert.beginSheetModal(for: window) { result in if result == .alertFirstButtonReturn { self.shop?.window.close(); self.shop = nil; self.perform { try engine.start(update: true) } } }
    }
    @objc func openFolder() {
        perform {
            try engine.connect(); let root = try engine.resolveInstallation()
            guard FileManager.default.fileExists(atPath: root.path) else { throw LauncherError(message: "Choose Start shop first to install application files.") }
            DispatchQueue.main.async { NSWorkspace.shared.open(root) }; engine.report("Opened application folder. Keep .env private; use Shift–Command–. in Finder to show hidden files."); return nil
        }
    }
    @objc func openGuide() { NSWorkspace.shared.open(resourceRoot.appendingPathComponent("Install Invora.html")) }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if busy { NSSound.beep(); append("Please wait for the current operation to finish before closing Invora."); return .terminateCancel }
        return .terminateNow
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if busy { NSSound.beep(); return false }; NSApp.terminate(nil); return false
    }
}
let application = NSApplication.shared
let delegate = AppDelegate()
NSApp.setActivationPolicy(.regular)
NSApp.delegate = delegate
NSApp.run()
