using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GaltekClassroom.Bootstrapper
{
    // One cached, read-only native .NET pass. Detect starts no script host,
    // CIM/WMI, hashing, directory enumeration, polling, or mutation.
    public static class LegacyDetector
    {
        private const string ServiceName = "GaltekClassroomAgent";
        private const string LegacyServiceName = "GaltekClassroomAgentService";
        private const string TaskName = "GaltekClassroomSessionAgent";
        private const string Clsid = "{D1A77223-ACAE-4C53-8C52-4FE8B8357E82}";
        private const string ProviderName = "Galtek Classroom Credential Provider";

        public static LegacyDetection Detect()
        {
            var root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Galtek", "Classroom", "Agent"));
            var imagePath = ReadServiceImagePath(ServiceName);
            if (imagePath == null) imagePath = ReadServiceImagePath(LegacyServiceName);
            var service = imagePath != null;

            string taskPath = null;
            var session = TryReadTask(out taskPath);
            var providerKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + Clsid;
            var inprocKey = @"SOFTWARE\Classes\CLSID\" + Clsid + @"\InprocServer32";
            var providerValue = ReadRegistryDefault(providerKey);
            var cpPath = ReadRegistryDefault(inprocKey);
            var cp = providerValue != null || !String.IsNullOrWhiteSpace(cpPath);

            var result = new LegacyDetection { Service = service, Session = session, CredentialProvider = cp };
            if (providerValue != null && !String.Equals(providerValue, ProviderName, StringComparison.Ordinal))
                return Block(result, "CP", "PROVIDER_NAME_MISMATCH");
            if (service && !IsExpectedServiceImagePath(imagePath, Path.Combine(root, "GaltekClassroom.Agent.Service.exe")))
                return Block(result, "SERVICE", "PATH_OUTSIDE_GALTEK_ROOT");
            if (session && !IsExactComponentPath(taskPath, Path.Combine(root, "Session"), "GaltekClassroom.Agent.Session.exe"))
                return Block(result, "SESSION", "PATH_OUTSIDE_GALTEK_ROOT");
            if (cp && !IsExactComponentPath(cpPath, Path.Combine(root, "CredentialProvider"), "GaltekClassroom.CredentialProvider.dll"))
                return Block(result, "CP", "PATH_OUTSIDE_GALTEK_ROOT");

            result.Reason = service || session || cp ? "VALID_LEGACY_INSTALLATION" : "FRESH_INSTALL";
            result.State = !service && !session && !cp ? "FRESH" :
                service && session && cp ? "LEGACY_SUPPORTED" : "REPAIRABLE_PARTIAL";
            return result;
        }

        public static bool IsExpectedServiceImagePath(string raw, string expectedExecutable)
        {
            if (!TryNormalizeLocal(expectedExecutable, out var expected)) return false;
            if (TrySplitCommandLine(raw, out var executable, out var arguments) &&
                String.IsNullOrWhiteSpace(arguments) && TryNormalizeLocal(executable, out var parsed) &&
                String.Equals(parsed, expected, StringComparison.OrdinalIgnoreCase)) return true;
            return raw != null && raw.IndexOf('"') < 0 && TryNormalizeLocal(raw.Trim(), out var whole) &&
                String.Equals(whole, expected, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsExactComponentPath(string candidate, string expectedRoot, string expectedFileName)
        {
            if (!TryNormalizeLocal(candidate, out var path) || !TryNormalizeLocal(expectedRoot, out var root)) return false;
            var boundary = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TrySplitCommandLine(string raw, out string executable, out string arguments)
        {
            executable = null; arguments = null;
            if (String.IsNullOrWhiteSpace(raw)) return false;
            var text = raw.Trim();
            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                if (end < 1) return false;
                executable = text.Substring(1, end - 1);
                arguments = text.Substring(end + 1).Trim();
                return true;
            }
            var space = text.IndexOfAny(new[] { ' ', '\t' });
            executable = space < 0 ? text : text.Substring(0, space);
            arguments = space < 0 ? String.Empty : text.Substring(space).Trim();
            return true;
        }

        private static bool TryNormalizeLocal(string value, out string normalized)
        {
            normalized = null;
            try
            {
                if (String.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0 || value.IndexOf('"') >= 0) return false;
                var full = Path.GetFullPath(value.Trim());
                var root = Path.GetPathRoot(full);
                if (String.IsNullOrEmpty(root) || root.Length != 3 || root[1] != ':' || root[2] != '\\') return false;
                if (full.Substring(root.Length).IndexOf(':') >= 0) return false;
                normalized = full;
                return true;
            }
            catch { return false; }
        }

        private static string ReadServiceImagePath(string name) => ReadRegistryValue(@"SYSTEM\CurrentControlSet\Services\" + name, "ImagePath");
        private static string ReadRegistryDefault(string path) => ReadRegistryValue(path, String.Empty);
        private static string ReadRegistryValue(string path, string name)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(path, false))
                return key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        private static bool TryReadTask(out string executable)
        {
            executable = null;
            object scheduler = null, folder = null, task = null, definition = null, actions = null, action = null;
            try
            {
                var type = Type.GetTypeFromProgID("Schedule.Service", false);
                if (type == null) return false;
                scheduler = Activator.CreateInstance(type);
                dynamic service = scheduler;
                service.Connect();
                folder = service.GetFolder("\\");
                task = ((dynamic)folder).GetTask(TaskName);
                definition = ((dynamic)task).Definition;
                actions = ((dynamic)definition).Actions;
                if (((dynamic)actions).Count < 1) return true;
                action = ((dynamic)actions).Item(1);
                executable = (string)((dynamic)action).Path;
                return true;
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002) || ex.HResult == unchecked((int)0x8004130D)) { return false; }
            finally
            {
                foreach (var value in new[] { action, actions, definition, task, folder, scheduler })
                    if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
            }
        }

        private static LegacyDetection Block(LegacyDetection result, string component, string reason)
        {
            result.State = "BLOCKED_CONFLICT"; result.Component = component; result.Reason = reason; return result;
        }
    }
}
