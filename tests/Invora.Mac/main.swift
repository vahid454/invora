import Foundation
import CryptoKit
import Darwin

umask(0o077)
let fm = FileManager.default
let temp = URL(fileURLWithPath: "/private/tmp").appendingPathComponent("invora-mac-checks-" + UUID().uuidString)
try fm.createDirectory(at: temp, withIntermediateDirectories: true)
defer { try? fm.removeItem(at: temp) }
var passed = 0
func check(_ name: String, _ test: () throws -> Void) throws {
    print("RUN: \(name)"); fflush(stdout)
    try test(); passed += 1; print("PASS: \(name)")
}
func require(_ value: Bool, _ message: String) throws {
    if !value { throw LauncherError(message: message) }
}
func rejects(_ action: () throws -> Void) throws {
    var rejected = false
    do { try action() } catch { rejected = true }
    try require(rejected, "Expected rejection")
}
func write(_ url: URL, _ contents: String) throws {
    try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
    try contents.write(to: url, atomically: true, encoding: .utf8)
}
let valid = "INVORA_HTTP_PORT=8080\nINVORA_BROWSER_ORIGIN=http://127.0.0.1:8080\nINVORA_ENVIRONMENT=Development\n"
try check("Local endpoint and quoted custom port") {
    try require(try InvoraLauncher.localOrigin(valid) == "http://127.0.0.1:8080", "Origin changed")
    let body = "INVORA_HTTP_PORT='8181'\nINVORA_BROWSER_ORIGIN=\"http://localhost:8181\"\nINVORA_ENVIRONMENT=Development"
    try require(try InvoraLauncher.localOrigin(body) == "http://localhost:8181", "Quotes not accepted")
}
try check("Invalid, remote, duplicate and mismatched endpoints rejected") {
    for body in [valid + "INVORA_HTTP_PORT=8080\n", valid.replacingOccurrences(of: "127.0.0.1", with: "0.0.0.0"), valid.replacingOccurrences(of: "INVORA_HTTP_PORT=8080", with: "INVORA_HTTP_PORT=99999"), valid.replacingOccurrences(of: "INVORA_HTTP_PORT=8080", with: "INVORA_HTTP_PORT=008080"), valid.replacingOccurrences(of: "INVORA_HTTP_PORT=8080", with: "INVORA_HTTP_PORT=8081"), valid.replacingOccurrences(of: "Development", with: "Production")] {
        try rejects { _ = try InvoraLauncher.localOrigin(body) }
    }
}
try check("Unsafe release paths rejected") {
    for name in ["../.env", "/tmp/file", "src/../../data", "src//file", "src/./file", "src\\file", "C:/file", ".env", "src/.env.secret", ".private-backups/database.dump", "src/a\nfile", ".vendor-private/key.pem"] {
        try require(!InvoraLauncher.validReleasePath(name), "Accepted unsafe path")
    }
    try require(InvoraLauncher.validReleasePath(".env.example"), "Example rejected")
    try require(InvoraLauncher.validReleasePath("src/File With Space.cs"), "Ordinary path rejected")
}
func fixture(_ label: String) throws -> InvoraLauncher {
    let release = temp.appendingPathComponent(label + "/release")
    let entries = ["Dockerfile": "FROM fixture", "docker-compose.yml": "name: fixture", "deployment/license-policy.json": "{\"required\":true,\"publicKey\":\"" + Data(repeating: 1, count: 384).base64EncodedString() + "\"}"]
    var lines: [String] = []
    for (name, body) in entries {
        try write(release.appendingPathComponent(name), body)
        let hash = SHA256.hash(data: Data(body.utf8)).map { String(format: "%02x", $0) }.joined()
        lines.append(hash + "  " + name)
    }
    try write(release.appendingPathComponent("release-sha256.txt"), lines.sorted().joined(separator: "\n") + "\n")
    let app = InvoraLauncher(release: release, support: temp.appendingPathComponent(label + "/support"))
    let docker = temp.appendingPathComponent(label + "/docker")
    try write(docker, "#!/bin/sh\ncase \"$1\" in volume) printf '%s' \"$FIXTURE_VOLUMES\";; ps) printf '%s' \"$FIXTURE_FOLDER\";; *) exit 1;; esac\n")
    try fm.setAttributes([.posixPermissions: 0o700], ofItemAtPath: docker.path)
    app.docker = docker.path
    return app
}
try check("Valid release installs without overwriting configuration") {
    let app = try fixture("install"); let destination = temp.appendingPathComponent("shop with spaces")
    try write(destination.appendingPathComponent(".env"), "original private config")
    try require(try app.verifyRelease().count == 3, "Missing files")
    try app.install(at: destination)
    try require(try String(contentsOf: destination.appendingPathComponent(".env"), encoding: .utf8) == "original private config", "Configuration overwritten")
}
try check("Corrupt payload blocked before any destination writes") {
    let app = try fixture("corrupt"); let destination = temp.appendingPathComponent("not-installed")
    try write(app.release.appendingPathComponent("Dockerfile"), "modified")
    try rejects { try app.install(at: destination) }
    try require(!fm.fileExists(atPath: destination.path), "Wrote files before verification")
}
try check("Interrupted installation keeps a retry marker and private config") {
    let app = try fixture("retry"); let destination = temp.appendingPathComponent("interrupted-shop")
    try write(destination.appendingPathComponent(".env"), "original credentials")
    try fm.createDirectory(at: destination.appendingPathComponent("Dockerfile"), withIntermediateDirectories: true)
    try rejects { try app.install(at: destination) }
    try require(fm.fileExists(atPath: destination.appendingPathComponent(".invora-build-required").path), "Missing retry marker")
    try require(try String(contentsOf: destination.appendingPathComponent(".env"), encoding: .utf8) == "original credentials", "Configuration changed")
    try fm.removeItem(at: destination.appendingPathComponent("Dockerfile"))
    try app.install(at: destination)
    try require(try String(contentsOf: destination.appendingPathComponent("Dockerfile"), encoding: .utf8) == "FROM fixture", "Retry did not finish the release")
}
try check("Unlisted private file and duplicate manifest entry rejected") {
    let app = try fixture("unlisted")
    try write(app.release.appendingPathComponent(".env"), "private")
    try rejects { _ = try app.verifyRelease() }
    try fm.removeItem(at: app.release.appendingPathComponent(".env"))
    let manifest = app.release.appendingPathComponent("release-sha256.txt")
    let body = try String(contentsOf: manifest, encoding: .utf8)
    try write(manifest, body + body.components(separatedBy: .newlines)[0] + "\n")
    try rejects { _ = try app.verifyRelease() }
}
try check("Linked source and destination rejected without changing external files") {
    let app = try fixture("links"); let original = app.release.appendingPathComponent("Dockerfile")
    let outside = temp.appendingPathComponent("outside-file")
    try write(outside, "FROM fixture")
    try fm.removeItem(at: original); try fm.createSymbolicLink(at: original, withDestinationURL: outside)
    try rejects { _ = try app.verifyRelease() }
    try fm.removeItem(at: original); try write(original, "FROM fixture")
    let destination = temp.appendingPathComponent("linked-destination")
    try fm.createDirectory(at: destination, withIntermediateDirectories: true)
    try fm.createSymbolicLink(at: destination.appendingPathComponent("Dockerfile"), withDestinationURL: outside)
    try rejects { try app.install(at: destination) }
    try require(try String(contentsOf: outside, encoding: .utf8) == "FROM fixture", "External file changed")
}
try check("Fresh config uses independent secrets, licence policy and mode 0600") {
    let app = try fixture("fresh")
    let destination = temp.appendingPathComponent("fresh-shop"); try app.install(at: destination)
    try app.newConfiguration(at: destination)
    let config = destination.appendingPathComponent(".env")
    let body = try String(contentsOf: config, encoding: .utf8)
    let secrets = body.components(separatedBy: .newlines).filter { $0.hasPrefix("POSTGRES_PASSWORD=") || $0.hasPrefix("INVORA_SIGNING_KEY=") || $0.hasPrefix("INVORA_BOOTSTRAP_KEY=") }.map { $0.components(separatedBy: "=").last! }
    try require(secrets.count == 3 && Set(secrets).count == 3 && secrets.allSatisfy { $0.count >= 48 }, "Secrets not independent")
    try require(body.contains("INVORA_LICENSE_REQUIRED=true"), "Commercial policy not applied")
    let mode = try fm.attributesOfItem(atPath: config.path)[.posixPermissions] as! NSNumber
    try require(mode.intValue == 0o600, "Config permissions are not private")
    try app.newConfiguration(at: destination)
    try require(try String(contentsOf: config, encoding: .utf8) == body, "Existing credentials regenerated")
}
try check("Existing volumes block regeneration of missing credentials") {
    let app = try fixture("existing-data")
    app.environment["FIXTURE_VOLUMES"] = "invora_database\n"
    let destination = temp.appendingPathComponent("lost-config"); try app.install(at: destination)
    try rejects { try app.newConfiguration(at: destination) }
    try require(!fm.fileExists(atPath: destination.appendingPathComponent(".env").path), "Created replacement credentials")
}
try check("Existing installation reused, including stopped container results") {
    let app = try fixture("reuse"); let destination = temp.appendingPathComponent("existing-install")
    try write(destination.appendingPathComponent(".env"), valid)
    app.environment["FIXTURE_VOLUMES"] = "invora_database\ninvora_documents"
    app.environment["FIXTURE_FOLDER"] = destination.path + "\n" + destination.path
    try require(try app.resolveInstallation().path == destination.path, "Existing path not reused")
    try app.remember(destination)
    app.environment["FIXTURE_FOLDER"] = ""
    try require(try app.resolveInstallation().path == destination.path, "Remembered path lost")
}
try check("Missing or conflicting existing installation rejected") {
    let app = try fixture("ambiguous")
    app.environment["FIXTURE_VOLUMES"] = "invora_database"
    app.environment["FIXTURE_FOLDER"] = temp.appendingPathComponent("missing-original").path
    try rejects { _ = try app.resolveInstallation() }
    app.environment["FIXTURE_FOLDER"] = temp.appendingPathComponent("one").path + "\n" + temp.appendingPathComponent("two").path
    try rejects { _ = try app.resolveInstallation() }
}
print("\(passed) Mac launcher checks passed. No shop records touched.")
