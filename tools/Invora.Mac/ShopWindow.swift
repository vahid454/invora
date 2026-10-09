import AppKit
import WebKit

struct ShopNavigation {
    static func isInternal(_ url: URL, origin: URL) -> Bool {
        let candidate = url.scheme == "blob" ? URL(string: String(url.absoluteString.dropFirst(5))) : url
        guard let candidate else { return false }
        return candidate.scheme == origin.scheme && candidate.host == origin.host && (candidate.port ?? 80) == (origin.port ?? 80) && candidate.user == nil && candidate.password == nil
    }
    static func isExternal(_ url: URL) -> Bool {
        ["http", "https", "mailto", "tel", "sms"].contains(url.scheme ?? "") && url.user == nil && url.password == nil
    }
}

final class ShopWindow: NSObject, NSWindowDelegate, WKNavigationDelegate, WKUIDelegate, WKDownloadDelegate {
    let origin: URL
    let window: NSWindow
    let web: WKWebView
    let status = NSTextField(labelWithString: "Opening your shop…")
    var onClose: (() -> Void)?
    var onReady: (() -> Void)?
    var onDownload: ((URL) -> Void)?
    var testDownloadDirectory: URL?
    private var destinations: [ObjectIdentifier: URL] = [:]
    private var stagedFiles: [ObjectIdentifier: URL] = [:]
    private var downloads: [ObjectIdentifier: WKDownload] = [:]

    init(origin: URL, persistent: Bool = true) {
        self.origin = origin
        let configuration = WKWebViewConfiguration()
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = false
        configuration.websiteDataStore = persistent ? .default() : .nonPersistent()
        // No script bridge or local filesystem access is granted to shop JavaScript.
        web = WKWebView(frame: .zero, configuration: configuration)
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1280, height: 850), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        super.init()
        window.title = "Invora · Your shop"; window.minSize = NSSize(width: 800, height: 600)
        window.isReleasedWhenClosed = false; window.delegate = self
        window.appearance = NSAppearance(named: .aqua)
        web.navigationDelegate = self; web.uiDelegate = self
        let container = NSStackView(); container.orientation = .vertical; container.spacing = 0
        let toolbar = NSStackView(); toolbar.orientation = .horizontal; toolbar.spacing = 10
        toolbar.edgeInsets = NSEdgeInsets(top: 6, left: 12, bottom: 6, right: 12)
        for (title, selector) in [("Back", #selector(goBack)), ("Reload", #selector(reload)), ("Open in browser", #selector(openBrowser)), ("Downloads", #selector(openDownloads)), ("Launcher", #selector(openLauncher))] {
            let button = NSButton(title: title, target: self, action: selector); button.bezelStyle = .rounded; toolbar.addArrangedSubview(button)
        }
        container.addArrangedSubview(toolbar); container.addArrangedSubview(web)
        status.font = .systemFont(ofSize: 11); status.textColor = .secondaryLabelColor
        let footer = NSStackView(views: [status]); footer.edgeInsets = NSEdgeInsets(top: 5, left: 12, bottom: 5, right: 12)
        container.addArrangedSubview(footer)
        window.contentView = container
        toolbar.widthAnchor.constraint(equalTo: container.widthAnchor).isActive = true
        web.widthAnchor.constraint(equalTo: container.widthAnchor).isActive = true
        footer.widthAnchor.constraint(equalTo: container.widthAnchor).isActive = true
        web.setContentHuggingPriority(.defaultLow, for: .vertical)
        web.setContentCompressionResistancePriority(.defaultLow, for: .vertical)
        window.center()
    }

    func show() {
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
        if web.url == nil { web.load(URLRequest(url: origin)) }
    }
    @objc private func goBack() { if web.canGoBack { web.goBack() } }
    @objc private func reload() { if web.url == nil { web.load(URLRequest(url: origin)) } else { web.reload() } }
    @objc private func openBrowser() { NSWorkspace.shared.open(origin) }
    @objc private func openDownloads() { NSWorkspace.shared.open(FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0]) }
    @objc private func openLauncher() { onClose?() }
    func windowWillClose(_ notification: Notification) { onClose?() }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        if ShopNavigation.isInternal(url, origin: origin) {
            decisionHandler(navigationAction.shouldPerformDownload ? .download : .allow); return
        }
        // External links leave the embedded window; never navigate it to arbitrary websites.
        if (navigationAction.navigationType == .linkActivated || navigationAction.targetFrame == nil), ShopNavigation.isExternal(url) {
            NSWorkspace.shared.open(url)
        }
        decisionHandler(.cancel)
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        guard let url = navigationResponse.response.url, ShopNavigation.isInternal(url, origin: origin) else { decisionHandler(.cancel); return }
        let disposition = (navigationResponse.response as? HTTPURLResponse)?.value(forHTTPHeaderField: "Content-Disposition") ?? ""
        decisionHandler(!navigationResponse.canShowMIMEType || disposition.lowercased().hasPrefix("attachment") ? .download : .allow)
    }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url, ShopNavigation.isInternal(url, origin: origin) { webView.load(navigationAction.request) }
        return nil
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        status.stringValue = "Your shop · Stored on this Mac"; onReady?()
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { failed(error) }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { failed(error) }
    private func failed(_ error: Error) {
        if (error as NSError).code == NSURLErrorCancelled { return }
        status.stringValue = "Shop unavailable. Use Launcher → Start shop, then Reload. Saved records are retained."
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { status.stringValue = "The app window needs to reload. Choose Reload; saved records are retained." }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) { track(download) }
    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) { track(download) }
    private func track(_ download: WKDownload) { downloads[ObjectIdentifier(download)] = download; download.delegate = self }
    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String, completionHandler: @escaping (URL?) -> Void) {
        let key = ObjectIdentifier(download)
        let name = URL(fileURLWithPath: suggestedFilename).lastPathComponent
        if let directory = testDownloadDirectory {
            let url = directory.appendingPathComponent(name); destinations[key] = url; completionHandler(url); return
        }
        let panel = NSSavePanel(); panel.nameFieldStringValue = name; panel.canCreateDirectories = true; panel.title = "Save from Invora"
        panel.directoryURL = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0]
        panel.beginSheetModal(for: window) { result in
            guard result == .OK, let url = panel.url else { self.downloads.removeValue(forKey: key); completionHandler(nil); return }
            // Download beside the chosen destination, then replace atomically only after success.
            // A failed download must not erase an older invoice or statement.
            let staging = url.deletingLastPathComponent().appendingPathComponent(".invora-download-" + UUID().uuidString)
            self.stagedFiles[key] = staging; self.destinations[key] = url
            self.status.stringValue = "Downloading \(url.lastPathComponent)…"; completionHandler(staging)
        }
    }
    func download(_ download: WKDownload, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, decisionHandler: @escaping (WKDownload.RedirectPolicy) -> Void) {
        decisionHandler(request.url.map { ShopNavigation.isInternal($0, origin: origin) } == true ? .allow : .cancel)
    }
    func downloadDidFinish(_ download: WKDownload) {
        let key = ObjectIdentifier(download)
        if let url = destinations.removeValue(forKey: key) {
            do {
                if let staging = stagedFiles.removeValue(forKey: key) {
                    if FileManager.default.fileExists(atPath: url.path) { _ = try FileManager.default.replaceItemAt(url, withItemAt: staging) }
                    else { try FileManager.default.moveItem(at: staging, to: url) }
                }
                status.stringValue = "Saved \(url.lastPathComponent)"; onDownload?(url)
            } catch { status.stringValue = "The download finished but could not be saved. Try another folder." }
        }
        downloads.removeValue(forKey: key)
    }
    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        let key = ObjectIdentifier(download)
        if let staging = stagedFiles.removeValue(forKey: key) { try? FileManager.default.removeItem(at: staging) }
        destinations.removeValue(forKey: key); downloads.removeValue(forKey: key)
        status.stringValue = "Download did not finish. Please try again."
    }
    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping ([URL]?) -> Void) {
        let panel = NSOpenPanel(); panel.canChooseDirectories = false; panel.canChooseFiles = true; panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        panel.beginSheetModal(for: window) { result in completionHandler(result == .OK ? panel.urls : nil) }
    }
    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        let alert = NSAlert(); alert.messageText = "Invora"; alert.informativeText = message
        alert.beginSheetModal(for: window) { _ in completionHandler() }
    }
    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        let alert = NSAlert(); alert.messageText = "Invora"; alert.informativeText = message; alert.addButton(withTitle: "Continue"); alert.addButton(withTitle: "Cancel")
        alert.beginSheetModal(for: window) { result in completionHandler(result == .alertFirstButtonReturn) }
    }
    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) {
        let alert = NSAlert(); alert.messageText = "Invora"; alert.informativeText = prompt; alert.addButton(withTitle: "OK"); alert.addButton(withTitle: "Cancel")
        let input = NSTextField(frame: NSRect(x: 0, y: 0, width: 320, height: 24)); input.stringValue = defaultText ?? ""; alert.accessoryView = input
        alert.beginSheetModal(for: window) { result in completionHandler(result == .alertFirstButtonReturn ? input.stringValue : nil) }
    }
}
