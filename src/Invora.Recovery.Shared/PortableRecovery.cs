using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Invora.Recovery;

public static class PortableRecovery
{
    private static readonly byte[] Magic = "INVORA-RECOVERY-1\n"u8.ToArray();
    private static readonly HashSet<string> Expected = ["database.backup", "shop.env", "recipient-private.pem", "signing-public.pem", "manifest.json"];
    private const int ChunkSize = 4 * 1024 * 1024;
    private const int Iterations = 600_000;

    public static void Extract(string packagePath, string destination, string password)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Choose a new empty recovery folder. Existing files are never replaced.");
        if (!File.Exists(packagePath) || (File.GetAttributes(packagePath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Choose a regular Invora recovery file.");
        var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".invora-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var zipPath = Path.Combine(staging, "verified.zip");
            Decrypt(packagePath, zipPath, password);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
            if (archive.Entries.Count != Expected.Count || archive.Entries.Any(x => !Expected.Contains(x.FullName)) ||
                archive.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != Expected.Count)
                throw new InvalidDataException("Recovery file has unexpected contents.");
            using var manifest = JsonDocument.Parse(ReadBounded(archive.GetEntry("manifest.json")!, 8192));
            var root = manifest.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("files").EnumerateObject().Count() != Expected.Count - 1)
                throw new InvalidDataException("Recovery manifest is invalid.");
            var files = root.GetProperty("files");
            foreach (var name in Expected.Where(x => x != "manifest.json"))
            {
                var entry = archive.GetEntry(name)!;
                var target = Path.Combine(staging, name);
                using var source = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                long length = 0;
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    output.Write(buffer.AsSpan(0, read));
                    hash.AppendData(buffer.AsSpan(0, read));
                    length += read;
                }
                output.Flush(flushToDisk: true);
                var expected = files.GetProperty(name);
                if (expected.GetProperty("bytes").GetInt64() != length ||
                    !string.Equals(expected.GetProperty("sha256").GetString(), Convert.ToHexStringLower(hash.GetHashAndReset()), StringComparison.Ordinal))
                    throw new InvalidDataException("Recovery checksum failed for " + name + ".");
            }
            }
            File.Delete(zipPath);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static byte[] ReadBounded(ZipArchiveEntry entry, int maximum)
    {
        if (entry.Length > maximum) throw new InvalidDataException("Recovery manifest is too large.");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        if (memory.Length > maximum) throw new InvalidDataException("Recovery manifest is too large.");
        return memory.ToArray();
    }

    private static void Decrypt(string packagePath, string zipPath, string password)
    {
        using var source = File.OpenRead(packagePath);
        if (!ReadExact(source, Magic.Length).AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("This is not an Invora recovery file.");
        var lengthBytes = ReadExact(source, 4);
        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        if (headerLength is 0 or > 4096) throw new InvalidDataException("Invalid recovery header.");
        var headerBytes = ReadExact(source, checked((int)headerLength));
        using var header = JsonDocument.Parse(headerBytes);
        var root = header.RootElement;
        if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("iterations").GetInt32() != Iterations)
            throw new InvalidDataException("Unsupported recovery file version.");
        var salt = Convert.FromBase64String(root.GetProperty("salt").GetString()!);
        var prefix = Convert.FromBase64String(root.GetProperty("nonce_prefix").GetString()!);
        if (salt.Length != 16 || prefix.Length != 4 || password.Length < 16)
            throw new InvalidDataException("Recovery password or file header is invalid.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var aad = Magic.Concat(lengthBytes).Concat(headerBytes).ToArray();
        try
        {
            using var cipher = new AesGcm(key, 16);
            using var output = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            ulong counter = 0, total = 0;
            while (true)
            {
                var packetLength = BinaryPrimitives.ReadUInt32BigEndian(ReadExact(source, 4));
                var nonce = new byte[12];
                prefix.CopyTo(nonce, 0);
                BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), counter);
                if (packetLength == 0)
                {
                    var tag = ReadExact(source, 16);
                    if (source.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing recovery data.");
                    var finalAad = Aad(aad, (byte)'F', counter, total);
                    try { cipher.Decrypt(nonce, ReadOnlySpan<byte>.Empty, tag, Span<byte>.Empty, finalAad); }
                    catch (CryptographicException) { throw new InvalidDataException("Wrong password or damaged recovery file."); }
                    break;
                }
                if (packetLength < 17 || packetLength > ChunkSize + 16) throw new InvalidDataException("Invalid recovery chunk size.");
                var packet = ReadExact(source, checked((int)packetLength));
                var plain = new byte[packet.Length - 16];
                try { cipher.Decrypt(nonce, packet.AsSpan(0, plain.Length), packet.AsSpan(plain.Length), plain, Aad(aad, (byte)'D', counter)); }
                catch (CryptographicException) { throw new InvalidDataException("Wrong password or damaged recovery file."); }
                output.Write(plain);
                total += (ulong)plain.Length;
                counter++;
            }
            output.Flush(flushToDisk: true);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] Aad(byte[] header, byte marker, ulong counter, ulong? total = null)
    {
        var bytes = new byte[header.Length + (total.HasValue ? 17 : 9)];
        header.CopyTo(bytes, 0);
        bytes[header.Length] = marker;
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(header.Length + 1), counter);
        if (total.HasValue) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(header.Length + 9), total.Value);
        return bytes;
    }

    private static byte[] ReadExact(Stream stream, int length)
    {
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
