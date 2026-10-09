import Foundation
import CryptoKit
import Security

struct LauncherError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Local-only launcher. Never evaluates .env as shell code or removes Docker data.
final class InvoraLauncher {
    let files = FileManager.default
    let release: URL
    let support: URL
    var report: (String) -> Void = { _ in }
    var docker = ""
    var installation: URL?
    var environment: [String: String]

    init(release: URL, support: URL? = nil) {
        self.release = release
        self.support = support ?? FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/Invora Launcher")
        environment = ProcessInfo.processInfo.environment
        // .env is authoritative; shell overrides must not select a different shop/configuration.
        for key in Array(environment.keys) where key.hasPrefix("INVORA_") || key.hasPrefix("POSTGRES_") || key.hasPrefix("COMPOSE_") {
            environment.removeValue(forKey: key)
        }
        environment["PATH"] = "/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin"
    }

    @discardableResult
    func run(_ executable: String, _ arguments: [String], at directory: URL? = nil,
             timeout: Double = 180, failure: String) throws -> String {
        let task = Process()
        task.executableURL = URL(fileURLWithPath: executable)
        task.arguments = arguments
        task.currentDirectoryURL = directory
        task.environment = environment
        let pipe = Pipe()
        task.standardOutput = pipe
        task.standardError = pipe
        task.standardInput = FileHandle.nullDevice
        try task.run()
        let deadline = DispatchWorkItem { if task.isRunning { task.terminate() } }
        DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: deadline)
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        task.waitUntilExit()
        deadline.cancel()
        guard task.terminationStatus == 0 else { throw LauncherError(message: failure) }
        return String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func connect() throws {
        let candidates = ["/usr/local/bin/docker", "/opt/homebrew/bin/docker",
                          files.homeDirectoryForCurrentUser.appendingPathComponent(".docker/bin/docker").path,
                          "/Applications/Docker.app/Contents/Resources/bin/docker"]
        guard let found = candidates.first(where: { files.isExecutableFile(atPath: $0) }) else {
            throw LauncherError(message: "Install Docker Desktop for your Mac, start it, then try again. See the installation guide.")
        }
        docker = found
        let context = try run(docker, ["context", "show"], failure: "Docker context could not be checked.")
        let contextHost = try run(docker, ["context", "inspect", context, "--format", "{{.Endpoints.docker.Host}}"], failure: "Docker endpoint could not be checked.")
        let host = environment["DOCKER_CONTEXT"].map { $0.isEmpty ? (environment["DOCKER_HOST"] ?? contextHost) : contextHost } ?? (environment["DOCKER_HOST"] ?? contextHost)
        guard host.hasPrefix("unix://") else { throw LauncherError(message: "Invora for Mac requires a local Docker Desktop engine. Remote Docker contexts are not supported by this launcher.") }
        let os = try run(docker, ["info", "--format", "{{.OSType}}"], timeout: 30,
                         failure: "Docker Desktop is not ready. Start Docker Desktop, wait for the engine, then try again.")
        guard os == "linux" else { throw LauncherError(message: "Docker must be running Linux containers.") }
        try run(docker, ["compose", "version"], failure: "Docker Compose is missing. Update Docker Desktop.")
        report("Docker Desktop is ready.")
    }

    func hasVolumes() throws -> Bool {
        let volumes = try run(docker, ["volume", "ls", "--format", "{{.Name}}"], failure: "Existing data volumes could not be checked.")
        return volumes.components(separatedBy: .newlines).contains { ["invora_database", "invora_documents"].contains($0) }
    }

    func resolveInstallation() throws -> URL {
        let containers = try run(docker, ["ps", "--all", "--filter", "label=com.docker.compose.project=invora", "--format", "{{.Label \"com.docker.compose.project.working_dir\"}}"], failure: "Existing Invora installations could not be checked.")
        let paths = Set(containers.components(separatedBy: .newlines).filter { !$0.isEmpty })
        guard paths.count <= 1 else { throw LauncherError(message: "Invora containers point to different folders. Ask support to resolve the installation paths before starting.") }
        let remembered = support.appendingPathComponent("installation.txt")
        let saved = files.fileExists(atPath: remembered.path) ? try String(contentsOf: remembered, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines) : ""
        let chosen = paths.first ?? (saved.isEmpty ? files.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/Invora").path : saved)
        guard chosen.hasPrefix("/"), !chosen.contains("\n"), !chosen.hasPrefix("/Volumes/"),
              !chosen.split(separator: "/").contains(where: { $0 == "." || $0 == ".." }) else {
            throw LauncherError(message: "Use a permanent installation folder on this Mac, outside the mounted installer disk image.")
        }
        let root = URL(fileURLWithPath: chosen, isDirectory: true)
        try rejectSymlinks(root)
        try rejectSymlinks(root.appendingPathComponent(".env"))
        if try hasVolumes(), !files.fileExists(atPath: root.appendingPathComponent(".env").path) {
            throw LauncherError(message: "Existing shop data was found but its private .env is missing. Recover the original configuration and installation folder. New passwords cannot unlock existing volumes.")
        }
        installation = root
        return root
    }

    func rejectSymlinks(_ url: URL) throws {
        var current = url
        while current.path != "/" {
            if let attrs = try? files.attributesOfItem(atPath: current.path), attrs[.type] as? FileAttributeType == .typeSymbolicLink {
                throw LauncherError(message: "A symbolic link was found in an installation path. Use a real private folder.")
            }
            current.deleteLastPathComponent()
        }
    }

    static func validReleasePath(_ name: String) -> Bool {
        let components = name.split(separator: "/", omittingEmptySubsequences: false).map(String.init)
        let blocked: Set<String> = [".env", ".private-backups", ".vendor-private", "node_modules", "bin", "obj", "artifacts", ".git", ".DS_Store"]
        return !name.isEmpty && !name.hasPrefix("/") && !name.contains("\\") && !name.contains(":") &&
            !name.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) &&
            !components.contains(where: { $0.isEmpty || $0 == "." || $0 == ".." || blocked.contains($0) || ($0.hasPrefix(".env") && $0 != ".env.example") })
    }

    /// Verify every file before any installation writes; no links or unlisted payloads.
    func verifyRelease() throws -> [String] {
        let manifest = release.appendingPathComponent("release-sha256.txt")
        try rejectSymlinks(manifest)
        let lines = try String(contentsOf: manifest, encoding: .utf8).split(separator: "\n")
        var names = Set<String>()
        var folded = Set<String>()
        var size: Int = 0
        for line in lines {
            guard line.count > 66 else { throw LauncherError(message: "Invalid release manifest.") }
            let hash = String(line.prefix(64))
            let separator = line.dropFirst(64).prefix(2)
            let name = String(line.dropFirst(66))
            guard separator == "  ", hash.count == 64, hash.allSatisfy({ $0.isHexDigit }),
                  Self.validReleasePath(name), names.insert(name).inserted,
                  folded.insert(name.lowercased()).inserted, names.count < 20000 else {
                throw LauncherError(message: "Unsafe or duplicate release path. Download a fresh release from your vendor.")
            }
            let file = release.appendingPathComponent(name)
            try rejectSymlinks(file)
            let attrs = try files.attributesOfItem(atPath: file.path)
            guard attrs[.type] as? FileAttributeType == .typeRegular else { throw LauncherError(message: "Release entries must be regular files.") }
            size += (attrs[.size] as? NSNumber)?.intValue ?? 0
            guard size < 250_000_000 else { throw LauncherError(message: "Release exceeds the supported size.") }
            let actual = SHA256.hash(data: try Data(contentsOf: file)).map { String(format: "%02x", $0) }.joined()
            guard actual == hash.lowercased() else { throw LauncherError(message: "A release file failed verification. Download a fresh release from your vendor.") }
        }
        guard names.contains("docker-compose.yml"), names.contains("deployment/license-policy.json"), names.contains("Dockerfile") else {
            throw LauncherError(message: "The release is incomplete.")
        }
        guard let enumerator = files.enumerator(at: release, includingPropertiesForKeys: [.isRegularFileKey, .isSymbolicLinkKey]) else { throw LauncherError(message: "Cannot read the release.") }
        for case let file as URL in enumerator {
            let attrs = try file.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey])
            guard attrs.isSymbolicLink != true else { throw LauncherError(message: "Release contains a symbolic link.") }
            let relative = String(file.path.dropFirst(release.path.count + 1))
            if attrs.isRegularFile == true && relative != "release-sha256.txt" && !names.contains(relative) {
                throw LauncherError(message: "Release contains an unlisted file.")
            }
        }
        return names.sorted()
    }

    func install(at root: URL) throws {
        let names = try verifyRelease()
        // Preflight ALL destination paths before replacing any source file.
        for name in names + ["release-sha256.txt", ".env", ".invora-build-required"] { try rejectSymlinks(root.appendingPathComponent(name)) }
        try files.createDirectory(at: root, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        // Mark before the first replacement so a failed/interrupted copy is retried in full.
        try "Installation/build must finish before starting this release.\n".write(to: root.appendingPathComponent(".invora-build-required"), atomically: true, encoding: .utf8)
        for name in names + ["release-sha256.txt"] {
            let target = root.appendingPathComponent(name)
            try files.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true)
            try Data(contentsOf: release.appendingPathComponent(name)).write(to: target, options: .atomic)
        }
        report("Application files installed. Existing configuration and data volumes are retained.")
    }

    func newConfiguration(at root: URL) throws {
        let config = root.appendingPathComponent(".env")
        if files.fileExists(atPath: config.path) { return }
        guard try !hasVolumes() else { throw LauncherError(message: "Existing data needs its original .env. Configuration was not regenerated.") }
        struct Policy: Decodable { let required: Bool; let publicKey: String }
        let policy = try JSONDecoder().decode(Policy.self, from: Data(contentsOf: root.appendingPathComponent("deployment/license-policy.json")))
        guard !policy.required || (Data(base64Encoded: policy.publicKey)?.count ?? 0) >= 256 else {
            throw LauncherError(message: "This commercial release is missing a valid public licence key.")
        }
        guard !policy.publicKey.contains("\n"), !policy.publicKey.contains("\r") else { throw LauncherError(message: "Invalid licence policy.") }
        func secret(_ count: Int) throws -> String {
            var bytes = [UInt8](repeating: 0, count: count)
            guard SecRandomCopyBytes(kSecRandomDefault, count, &bytes) == errSecSuccess else { throw LauncherError(message: "Secure random generation failed.") }
            return bytes.map { String(format: "%02x", $0) }.joined()
        }
        let body = """
        POSTGRES_DB=invora
        POSTGRES_USER=invora
        POSTGRES_PASSWORD=\(try secret(24))
        INVORA_SIGNING_KEY=\(try secret(32))
        INVORA_BOOTSTRAP_KEY=\(try secret(32))
        INVORA_BROWSER_ORIGIN=http://127.0.0.1:8080
        INVORA_ENVIRONMENT=Development
        INVORA_HTTP_PORT=8080
        INVORA_LICENSE_REQUIRED=\(policy.required ? "true" : "false")
        INVORA_LICENSE_PUBLIC_KEY=\(policy.publicKey)

        """
        // umask in main protects even the temporary file before rename.
        try body.write(to: config, atomically: true, encoding: .utf8)
        try files.setAttributes([.posixPermissions: 0o600], ofItemAtPath: config.path)
        report("Created private configuration. Use its setup key once to create the owner login.")
    }

    static func localOrigin(_ body: String) throws -> String {
        var values: [String: String] = [:]
        for line in body.components(separatedBy: .newlines) {
            let clean = line.trimmingCharacters(in: .whitespaces)
            if clean.isEmpty || clean.hasPrefix("#") { continue }
            guard let split = clean.firstIndex(of: "=") else { continue }
            let key = String(clean[..<split]).trimmingCharacters(in: .whitespaces)
            var value = String(clean[clean.index(after: split)...]).trimmingCharacters(in: .whitespaces)
            guard values[key] == nil else { throw LauncherError(message: "The private .env contains a duplicate setting. Remove duplicates before starting.") }
            if value.count >= 2 && ((value.first == "\"" && value.last == "\"") || (value.first == "'" && value.last == "'")) { value = String(value.dropFirst().dropLast()) }
            values[key] = value
        }
        guard let portText = values["INVORA_HTTP_PORT"], let port = Int(portText), (1...65535).contains(port), String(port) == portText,
              let origin = values["INVORA_BROWSER_ORIGIN"], ["http://127.0.0.1:\(port)", "http://localhost:\(port)"].contains(origin),
              values["INVORA_ENVIRONMENT"] == "Development" else {
            throw LauncherError(message: "For this local launcher, set INVORA_HTTP_PORT to one port (1–65535), INVORA_BROWSER_ORIGIN to http://127.0.0.1:<port> (or localhost), and INVORA_ENVIRONMENT=Development. Use the deployment guide for HTTPS hosting.")
        }
        return origin
    }

    func origin(at root: URL) throws -> String {
        try Self.localOrigin(String(contentsOf: root.appendingPathComponent(".env"), encoding: .utf8))
    }

    @discardableResult
    func compose(_ args: [String], at root: URL, timeout: Double = 180, failure: String) throws -> String {
        try run(docker, ["compose", "--project-name", "invora", "--project-directory", root.path,
                         "--env-file", root.appendingPathComponent(".env").path, "-f", root.appendingPathComponent("docker-compose.yml").path] + args,
                at: root, timeout: timeout, failure: failure)
    }

    func remember(_ root: URL) throws {
        try rejectSymlinks(support.appendingPathComponent("installation.txt"))
        try files.createDirectory(at: support, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try (root.path + "\n").write(to: support.appendingPathComponent("installation.txt"), atomically: true, encoding: .utf8)
    }

    func start(update: Bool = false) throws -> String {
        try connect()
        let root = try resolveInstallation()
        let fresh = !files.fileExists(atPath: root.appendingPathComponent("docker-compose.yml").path)
        let pendingBuild = root.appendingPathComponent(".invora-build-required")
        try rejectSymlinks(pendingBuild)
        if fresh || update || files.fileExists(atPath: pendingBuild.path) {
            try install(at: root)
        }
        try newConfiguration(at: root)
        let address = try origin(at: root)
        try remember(root)
        let expected = try compose(["config", "--images"], at: root, failure: "Configuration is incomplete. Review .env privately; preserve existing credentials.").components(separatedBy: .newlines)
        let existing = Set(try run(docker, ["image", "ls", "--format", "{{.Repository}}:{{.Tag}}"], failure: "Could not inspect Docker images.").components(separatedBy: .newlines))
        let needsBuild = files.fileExists(atPath: pendingBuild.path) || expected.contains(where: { !existing.contains($0) })
        if !needsBuild, let ready = try? run("/usr/bin/curl", ["--noproxy", "*", "--fail", "--silent", "--max-time", "2", "\(address)/health/ready"], timeout: 5, failure: "Not ready."), ready == "Healthy" {
            report("Your shop is already running at \(address).")
            return address
        }
        if needsBuild {
            report("Building this release. First installation needs internet and may take several minutes…")
            try compose(["build"], at: root, timeout: 3600, failure: "Image build failed. Check internet and free disk space. Open the application folder and run docker compose build in Terminal for details; then retry Update release.")
            if files.fileExists(atPath: pendingBuild.path) { try files.removeItem(at: pendingBuild) }
        }
        report("Starting the database…")
        try compose(["up", "-d", "db"], at: root, failure: "Database startup failed. Keep the existing configuration and volumes.")
        // Quiet writes before schema changes; never migrate under an active web/API process.
        try compose(["stop", "web", "api"], at: root, failure: "Could not pause the app for migrations.")
        report("Checking database migrations…")
        try compose(["run", "--rm", "migrate"], at: root, timeout: 600, failure: "Migration failed; the shop stays stopped. Keep your backup and run docker compose run --rm migrate from the application folder to inspect the error. Do not reset data.")
        report("Starting the shop…")
        try compose(["up", "-d", "api", "web"], at: root, failure: "Application startup failed. Check Docker Desktop and the application logs.")
        for _ in 0..<60 {
            if let response = try? run("/usr/bin/curl", ["--noproxy", "*", "--fail", "--silent", "--max-time", "2", "\(address)/health/ready"], timeout: 5, failure: "Readiness not yet available."), response == "Healthy" {
                report("Ready. Your shop opens at \(address).")
                return address
            }
            Thread.sleep(forTimeInterval: 2)
        }
        throw LauncherError(message: "The app did not become ready. Use Check status and inspect docker compose logs api web from the application folder. Your data volumes are retained.")
    }

    func check() throws -> String {
        try connect()
        let root = try resolveInstallation()
        guard files.fileExists(atPath: root.appendingPathComponent(".env").path) else { report("New installation: choose Start shop to install and create private configuration."); return "" }
        let address = try origin(at: root)
        try compose(["config", "--quiet"], at: root, failure: "Compose configuration needs attention. Review private .env locally.")
        report("Local configuration is valid. Data volumes \((try hasVolumes()) ? "exist" : "will be created on first start").")
        let healthy = try? run("/usr/bin/curl", ["--noproxy", "*", "--fail", "--silent", "--max-time", "3", "\(address)/health/ready"], timeout: 5, failure: "Not ready.")
        guard healthy == "Healthy" else { throw LauncherError(message: "The shop is stopped or not ready. Choose Start shop. This check has not changed configuration, services or records.") }
        report("Shop is healthy at \(address). This check does not verify backups or account balances.")
        return address
    }

    func stop() throws {
        try connect()
        let root = try resolveInstallation()
        _ = try origin(at: root)
        try compose(["stop"], at: root, failure: "Stopping the shop failed. Check Docker Desktop.")
        report("Shop stopped. Database, uploads, settings and passwords are retained.")
    }
}
