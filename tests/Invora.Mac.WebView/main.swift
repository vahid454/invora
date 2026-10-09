import AppKit
import WebKit

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
let args = CommandLine.arguments
let origin = URL(string: args[1])!
let folder = URL(fileURLWithPath: args[2])
let live = args.count > 3 && args[3] == "--live-preview"
let shop = ShopWindow(origin: origin, persistent: false)
shop.testDownloadDirectory = live ? nil : folder
var stage = 0
var saved = Set<String>()
var complete = false
let deadline = Date().addingTimeInterval(45)
func fail(_ message: String) -> Never { fputs(message + "\n", stderr); exit(1) }
func evaluate(_ script: String, then: @escaping (Any?) -> Void) {
    shop.web.evaluateJavaScript(script) { value, error in
        if let error { fail("WebView check failed: \(error.localizedDescription)") }
        then(value)
    }
}
func preview() {
    shop.web.takeSnapshot(with: nil) { image, error in
        guard let image, error == nil, let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff), let data = bitmap.representation(using: .png, properties: [:]) else { fail("WebView snapshot failed") }
        do { try data.write(to: folder.appendingPathComponent(live ? "invora-desktop-login.png" : "invora-desktop-fixture.png")) }
        catch { fail("Snapshot write failed") }
        print(live ? "PASS: Real Invora login renders inside native WKWebView." : "PASS: Native WebView input, same-origin navigation, PDF/CSV downloads and snapshot.")
        complete = true
    }
}
shop.onReady = {
    print("Navigation finished"); fflush(stdout)
    if stage != 0 { return }; stage = 1
    DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
        evaluate("document.body.innerText") { value in
            guard let text = value as? String else { fail("No page text") }
            if live {
                guard text.lowercased().contains("sign in") || text.lowercased().contains("welcome") else { fail("Login screen did not render") }
                preview(); return
            }
            guard text.contains("Desktop fixture") else { fail("Wrong page") }
            evaluate("document.querySelector('input').value='customer search'; document.querySelector('input').value") { value in
                guard value as? String == "customer search" else { fail("Input did not work") }
                evaluate("history.pushState({},'', '/sales'); location.pathname") { value in
                    guard value as? String == "/sales" else { fail("Navigation failed") }
                    evaluate("downloadFixture('invoice.pdf','application/pdf','%PDF-1.4 synthetic fixture'); true") { _ in }
                }
            }
        }
    }
}
shop.onDownload = { file in
    print("Downloaded " + file.lastPathComponent); fflush(stdout)
    do {
        let contents = try String(contentsOf: file, encoding: .utf8)
        guard contents.contains(file.lastPathComponent == "invoice.pdf" ? "%PDF-1.4" : "Fixture,1") else { fail("Download content differs") }
        saved.insert(file.lastPathComponent)
        if file.lastPathComponent == "invoice.pdf" {
            evaluate("downloadFixture('stock.csv','text/csv','Name,Quantity\\nFixture,1'); true") { _ in }
        }
        if saved.count == 2 { preview() }
    } catch { fail("Downloaded file not readable") }
}
for invalid in ["https://example.com", "file:///etc/passwd", "http://localhost:9192/", "http://127.0.0.1.evil.test/", "blob:blob:\(origin.absoluteString)"] {
    guard !ShopNavigation.isInternal(URL(string: invalid)!, origin: origin) else { fail("Cross-origin navigation allowed") }
}
guard ShopNavigation.isInternal(origin.appendingPathComponent("sales"), origin: origin), !ShopNavigation.isExternal(URL(string: "file:///tmp/file")!) else { fail("Navigation policy failed") }
shop.show()
while !complete && Date() < deadline { RunLoop.current.run(until: Date().addingTimeInterval(0.05)) }
guard complete else { fail("Desktop acceptance timed out: " + shop.status.stringValue + "; stage=\(stage); saved=\(saved.count); url=" + (shop.web.url?.absoluteString ?? "none")) }
shop.window.close()
