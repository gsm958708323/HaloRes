using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GameFramework.Asset.Patch
{
    public static class FileIntegrity
    {
        public static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(data));
        }

        public static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream));
        }

        public static Task<bool> VerifyAsync(string path, long expectedSize, string expectedHash, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(path) || new FileInfo(path).Length != expectedSize) return false;
                var matches = string.Equals(HashFile(path), expectedHash, StringComparison.OrdinalIgnoreCase);
                ct.ThrowIfCancellationRequested();
                return matches;
            }, ct);
        }

        public static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }
}
