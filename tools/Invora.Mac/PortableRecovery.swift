import Foundation
import CryptoKit
import CommonCrypto
import Darwin

// Standalone extractor for the password-protected .invora format. It never
// connects to Docker or overwrites a live shop. The launcher can give it a
// password through stdin; terminal users get a hidden prompt.

enum RecoveryFailure: Error, LocalizedError {
    case invalid(String)
    var errorDescription: String? {
        if case .invalid(let message) = self { return message }
        return nil
    }
}

let magic = Data("INVORA-RECOVERY-1\n".utf8)
let chunkSize = 4 * 1024 * 1024
let expectedNames = Set(["database.backup", "shop.env", "recipient-private.pem", "signing-public.pem", "manifest.json"])

func readExact(_ handle: FileHandle, _ count: Int) throws -> Data {
    guard let data = try handle.read(upToCount: count), data.count == count else {
        throw RecoveryFailure.invalid("Recovery file is incomplete.")
    }
    return data
}

func u32(_ bytes: Data) -> Int {
    bytes.reduce(0) { ($0 << 8) | Int($1) }
}

func counterData(_ value: UInt64) -> Data {
    Data((0..<8).reversed().map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) })
}

func derive(_ password: String, salt: Data) throws -> Data {
    guard password.count >= 16, salt.count == 16 else { throw RecoveryFailure.invalid("Recovery password or file header is invalid.") }
    var key = [UInt8](repeating: 0, count: 32)
    let status = password.withCString { chars in
        salt.withUnsafeBytes { saltBuffer in
            CCKeyDerivationPBKDF(CCPBKDFAlgorithm(kCCPBKDF2), chars, strlen(chars),
                                  saltBuffer.bindMemory(to: UInt8.self).baseAddress, salt.count,
                                  CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256), 600_000, &key, key.count)
        }
    }
    guard status == kCCSuccess else { throw RecoveryFailure.invalid("Recovery password could not be processed.") }
    return Data(key)
}

func run(_ executable: String, _ args: [String], output: FileHandle? = nil) throws -> Data {
    let process = Process(); process.executableURL = URL(fileURLWithPath: executable); process.arguments = args
    let pipe = Pipe(); process.standardOutput = output ?? pipe.fileHandleForWriting; process.standardError = FileHandle.nullDevice
    try process.run()
    if output == nil { try? pipe.fileHandleForWriting.close() }
    let data = output == nil ? pipe.fileHandleForReading.readDataToEndOfFile() : Data()
    process.waitUntilExit()
    guard process.terminationStatus == 0 else { throw RecoveryFailure.invalid("Recovery archive could not be verified or read.") }
    return data
}

func extract(_ package: URL, to destination: URL, password: String) throws {
    guard !FileManager.default.fileExists(atPath: destination.path) else {
        throw RecoveryFailure.invalid("Choose a new empty recovery folder; existing files are never replaced.")
    }
    let temporaryRoot = FileManager.default.temporaryDirectory.appendingPathComponent("invora-recovery-" + UUID().uuidString)
    try FileManager.default.createDirectory(at: temporaryRoot, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
    defer { try? FileManager.default.removeItem(at: temporaryRoot) }
    let zip = temporaryRoot.appendingPathComponent("verified.zip")
    let source = try FileHandle(forReadingFrom: package)
    defer { try? source.close() }
    guard try readExact(source, magic.count) == magic else { throw RecoveryFailure.invalid("This is not an Invora recovery file.") }
    let lengthBytes = try readExact(source, 4)
    let headerLength = u32(lengthBytes)
    guard headerLength > 0 && headerLength <= 4096 else { throw RecoveryFailure.invalid("Invalid recovery header.") }
    let headerBytes = try readExact(source, headerLength)
    guard let header = try JSONSerialization.jsonObject(with: headerBytes) as? [String: Any],
          header["version"] as? Int == 1, header["iterations"] as? Int == 600_000,
          let saltText = header["salt"] as? String, let salt = Data(base64Encoded: saltText),
          let prefixText = header["nonce_prefix"] as? String, let prefix = Data(base64Encoded: prefixText),
          prefix.count == 4 else { throw RecoveryFailure.invalid("Unsupported recovery file header.") }
    let key = SymmetricKey(data: try derive(password, salt: salt))
    let aad = magic + lengthBytes + headerBytes
    FileManager.default.createFile(atPath: zip.path, contents: nil, attributes: [.posixPermissions: 0o600])
    let plain = try FileHandle(forWritingTo: zip)
    defer { try? plain.close() }
    var index: UInt64 = 0
    var total: UInt64 = 0
    while true {
        let length = u32(try readExact(source, 4))
        let nonce = try AES.GCM.Nonce(data: prefix + counterData(index))
        if length == 0 {
            let tag = try readExact(source, 16)
            guard try source.read(upToCount: 1)?.isEmpty != false else { throw RecoveryFailure.invalid("Unexpected trailing recovery data.") }
            let box = try AES.GCM.SealedBox(nonce: nonce, ciphertext: Data(), tag: tag)
            do { _ = try AES.GCM.open(box, using: key, authenticating: aad + Data("F".utf8) + counterData(index) + counterData(total)) }
            catch { throw RecoveryFailure.invalid("Wrong password or damaged recovery file.") }
            break
        }
        guard length >= 17 && length <= chunkSize + 16 else { throw RecoveryFailure.invalid("Invalid recovery chunk size.") }
        let block = try readExact(source, length)
        let box = try AES.GCM.SealedBox(nonce: nonce, ciphertext: block.dropLast(16), tag: block.suffix(16))
        do {
            let bytes = try AES.GCM.open(box, using: key, authenticating: aad + Data("D".utf8) + counterData(index))
            try plain.write(contentsOf: bytes)
            total += UInt64(bytes.count)
        } catch { throw RecoveryFailure.invalid("Wrong password or damaged recovery file.") }
        index += 1
    }
    try plain.synchronize()
    let listing = try run("/usr/bin/unzip", ["-Z1", zip.path])
    let names = Set(String(decoding: listing, as: UTF8.self).split(separator: "\n").map(String.init))
    guard names == expectedNames else { throw RecoveryFailure.invalid("Recovery file has unexpected contents.") }
    let manifestData = try run("/usr/bin/unzip", ["-p", zip.path, "manifest.json"])
    guard let manifest = try JSONSerialization.jsonObject(with: manifestData) as? [String: Any],
          manifest["version"] as? Int == 1,
          let entries = manifest["files"] as? [String: [String: Any]],
          Set(entries.keys) == expectedNames.subtracting(["manifest.json"]) else {
        throw RecoveryFailure.invalid("Recovery file manifest is invalid.")
    }
    let extracted = temporaryRoot.appendingPathComponent("contents")
    try FileManager.default.createDirectory(at: extracted, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
    for name in expectedNames where name != "manifest.json" {
        let target = extracted.appendingPathComponent(name)
        FileManager.default.createFile(atPath: target.path, contents: nil, attributes: [.posixPermissions: 0o600])
        let handle = try FileHandle(forWritingTo: target)
        _ = try run("/usr/bin/unzip", ["-p", zip.path, name], output: handle)
        try handle.close()
        let verified = try FileHandle(forReadingFrom: target)
        defer { try? verified.close() }
        var digest = SHA256()
        var byteCount = 0
        while let bytes = try verified.read(upToCount: 1024 * 1024), !bytes.isEmpty {
            digest.update(data: bytes)
            byteCount += bytes.count
        }
        let hash = digest.finalize().map { String(format: "%02x", $0) }.joined()
        guard entries[name]?["sha256"] as? String == hash,
              entries[name]?["bytes"] as? Int == byteCount else {
            throw RecoveryFailure.invalid("Recovery checksum failed for \(name).")
        }
    }
    try FileManager.default.moveItem(at: extracted, to: destination)
}

do {
    let args = CommandLine.arguments
    guard args.count == 3 || (args.count == 4 && args[3] == "--password-stdin") else {
        throw RecoveryFailure.invalid("Usage: InvoraPortableRecovery PACKAGE.invora NEW_EMPTY_FOLDER [--password-stdin]")
    }
    let password: String
    if args.count == 4 {
        password = String(decoding: FileHandle.standardInput.readDataToEndOfFile(), as: UTF8.self).trimmingCharacters(in: .newlines)
    } else {
        guard let pointer = getpass("Recovery password: ") else { throw RecoveryFailure.invalid("Recovery password is required.") }
        password = String(cString: pointer)
    }
    try extract(URL(fileURLWithPath: args[1]), to: URL(fileURLWithPath: args[2]), password: password)
    print("Verified recovery files extracted. Keep this folder private; restore into an isolated database before cutover.")
} catch {
    fputs("Invora recovery stopped safely: \(error.localizedDescription)\n", stderr)
    exit(1)
}
