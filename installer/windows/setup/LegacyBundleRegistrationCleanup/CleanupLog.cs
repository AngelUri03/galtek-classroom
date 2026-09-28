using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GaltekClassroom.LegacyBundleCleanup
{
    internal sealed class CleanupLog : IDisposable
    {
        private const int MaximumRetainedLogs = 10;
        private readonly StreamWriter writer;

        private CleanupLog(StreamWriter writer)
        {
            this.writer = writer;
        }

        public static CleanupLog Open(string requestedPath)
        {
            var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Galtek", "Classroom", "Installer", "logs"));
            var fullPath = Path.GetFullPath(requestedPath ?? String.Empty);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (!String.Equals(directory, root, StringComparison.OrdinalIgnoreCase) ||
                String.IsNullOrWhiteSpace(fileName) || fileName.Length > 128 ||
                !fileName.StartsWith("legacy-bundle-cleanup-", StringComparison.Ordinal) ||
                !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                fileName.Any(c => !(Char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')))
                throw new InvalidOperationException("CLEANUP_LOG_PATH_INVALID");

            Directory.CreateDirectory(root);
            Prune(root, fullPath);
            return new CleanupLog(new StreamWriter(new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true });
        }

        public void Write(string message)
        {
            this.writer.WriteLine(DateTime.UtcNow.ToString("o") + " " + Sanitize(message));
        }

        public void Dispose()
        {
            this.writer.Dispose();
        }

        private static void Prune(string root, string currentPath)
        {
            try
            {
                var old = new DirectoryInfo(root).GetFiles("legacy-bundle-cleanup-*.log", SearchOption.TopDirectoryOnly)
                    .Where(x => !String.Equals(x.FullName, currentPath, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Skip(MaximumRetainedLogs - 1)
                    .ToList();
                foreach (var file in old) file.Delete();
            }
            catch
            {
                // Retention is best-effort; diagnostics for this operation must remain available.
            }
        }

        private static string Sanitize(string value) => (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
