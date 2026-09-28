using System;
using System.Linq;

namespace GaltekClassroom.LegacyBundleCleanup
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var predecessors = ReadArgument(args, "--predecessors");
            var currentBundleId = ReadArgument(args, "--current-bundle-id");
            var logPath = ReadArgument(args, "--log");
            if (String.IsNullOrWhiteSpace(predecessors) || !CleanupPolicy.IsValidCurrentBundleId(currentBundleId) || String.IsNullOrWhiteSpace(logPath))
                return (int)CleanupExitCode.InvalidArguments;

            CleanupLog cleanupLog;
            try { cleanupLog = CleanupLog.Open(logPath); }
            catch { return (int)CleanupExitCode.LogUnavailable; }

            using (cleanupLog)
            {
                cleanupLog.Write("CLEANUP_BEGIN exitContract=GALTEK_CLEANUP_V2 legacyExit10=BLOCKED");
                try
                {
                    var ids = predecessors.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
                    var result = new CleanupCoordinator().Run(new WindowsCleanupEnvironment(currentBundleId), ids, cleanupLog.Write);
                    if (result.Outcome == CleanupOutcome.Complete || result.Outcome == CleanupOutcome.NoOp)
                        return (int)CleanupExitCode.Success;
                    return result.Outcome == CleanupOutcome.Blocked ? (int)CleanupExitCode.Blocked : (int)CleanupExitCode.Failed;
                }
                catch (Exception ex)
                {
                    cleanupLog.Write("CLEANUP_FAILED reason=UNHANDLED exception=" + ex.GetType().Name);
                    return (int)CleanupExitCode.Failed;
                }
            }
        }

        private static string ReadArgument(string[] args, string name)
        {
            if (args == null) return null;
            for (var i = 0; i < args.Length; i++)
                if (String.Equals(args[i], name, StringComparison.Ordinal) && i + 1 < args.Length) return args[i + 1];
            return null;
        }
    }
}
