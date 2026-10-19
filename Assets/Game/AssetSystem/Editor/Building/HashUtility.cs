using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.AssetSystem.Editor.Building
{
    public static class HashUtility
    {
        public static bool TryComputeSha256Hex(
            string filePath,
            out string hashHex,
            out string error)
        {
            hashHex = null;
            error = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "filePath is null or empty.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                error = "File does not exist: " + filePath;
                return false;
            }

            try
            {
                using (var sha = SHA256.Create())
                using (var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.SequentialScan))
                {
                    hashHex = ToHex(sha.ComputeHash(stream));
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);

            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
