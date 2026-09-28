using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GaltekClassroom.LegacyBundleCleanup
{
    public sealed class WindowsCleanupEnvironment : ICleanupEnvironment
    {
        private const string UninstallRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string DependenciesRoot = @"SOFTWARE\Classes\Installer\Dependencies";
        private const string ServiceName = "GaltekClassroomAgent";
        private const string TaskName = "GaltekClassroomSessionAgent";
        private const uint QueryLimitedInformation = 0x1000;
        private readonly RegistryView view = RegistryView.Registry64;
        public string CurrentBundleId { get; }

        public WindowsCleanupEnvironment(string currentBundleId)
        {
            if (!CleanupPolicy.IsValidCurrentBundleId(currentBundleId)) throw new ArgumentException("CURRENT_BUNDLE_ID_INVALID", nameof(currentBundleId));
            this.CurrentBundleId = new Guid(currentBundleId).ToString("B").ToUpperInvariant();
        }

        public CurrentBundleRecord GetCurrentBundle()
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
            using (var root = baseKey.OpenSubKey(UninstallRoot, false))
            using (var key = baseKey.OpenSubKey(UninstallRoot + "\\" + this.CurrentBundleId, false))
            {
                if (root == null || key == null) return null;
                var providerKey = key.GetValue("BundleProviderKey") as string;
                return new CurrentBundleRecord
                {
                    BundleId = this.CurrentBundleId,
                    ProductName = key.GetValue("DisplayName") as string,
                    Manufacturer = key.GetValue("Publisher") as string,
                    Version = key.GetValue("BundleVersion") as string,
                    UpgradeCodes = ToStrings(key.GetValue("BundleUpgradeCode")),
                    ProviderKey = providerKey,
                    ProviderKeyRegistrationCount = CountProviderRegistrations(root, providerKey),
                    CachePath = key.GetValue("BundleCachePath") as string,
                    CachePathSafe = IsExactCachePath(this.CurrentBundleId, CleanupPolicy.CurrentBundleExecutableName, key.GetValue("BundleCachePath") as string, false),
                    ResumeMode = ReadNumber(key, "Resume"),
                    Installed = ReadNumber(key, "Installed")
                };
            }
        }

        public ProviderRecord GetProvider(string providerKey)
        {
            if (String.IsNullOrWhiteSpace(providerKey) || providerKey.IndexOf('\\') >= 0 || providerKey.IndexOf('/') >= 0)
                throw new InvalidOperationException("UNSAFE_PROVIDER_KEY");
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
            using (var key = baseKey.OpenSubKey(DependenciesRoot + "\\" + providerKey, false))
            {
                if (key == null) return new ProviderRecord { Key = providerKey, Exists = false, Dependents = new string[0] };
                using (var dependents = key.OpenSubKey("Dependents", false))
                    return new ProviderRecord
                    {
                        Key = providerKey,
                        Exists = true,
                        OwnerId = key.GetValue(String.Empty) as string,
                        Version = key.GetValue("Version") as string,
                        DisplayName = key.GetValue("DisplayName") as string,
                        Dependents = dependents?.GetSubKeyNames() ?? new string[0]
                    };
            }
        }

        public bool CurrentMsiInstalled => IsMsiInstalled(CleanupPolicy.CurrentMsiProductCode);

        public bool ServiceHealthy
        {
            get
            {
                try
                {
                    using (var service = new ServiceController(ServiceName))
                    {
                        if (service.Status != ServiceControllerStatus.Running || service.StartType != ServiceStartMode.Automatic) return false;
                    }
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
                    using (var key = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ServiceName, false))
                    {
                        return key?.GetValue("DelayedAutoStart") is int delayed && delayed == 1 &&
                            String.Equals(key.GetValue("ObjectName") as string, "LocalSystem", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { return false; }
            }
        }

        public bool SessionHealthy
        {
            get
            {
                object scheduler = null, folder = null, task = null, definition = null, actions = null, action = null;
                try
                {
                    var type = Type.GetTypeFromProgID("Schedule.Service", false);
                    if (type == null) return false;
                    scheduler = Activator.CreateInstance(type);
                    dynamic service = scheduler; service.Connect();
                    folder = service.GetFolder("\\");
                    task = ((dynamic)folder).GetTask(TaskName);
                    if (!((dynamic)task).Enabled) return false;
                    var state = (int)((dynamic)task).State;
                    if (state != 3 && state != 4) return false;
                    definition = ((dynamic)task).Definition;
                    actions = ((dynamic)definition).Actions;
                    if (((dynamic)actions).Count < 1) return false;
                    action = ((dynamic)actions).Item(1);
                    var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Galtek", "Classroom", "Agent", "Session", "GaltekClassroom.Agent.Session.exe");
                    return String.Equals(Path.GetFullPath((string)((dynamic)action).Path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
                finally
                {
                    foreach (var value in new[] { action, actions, definition, task, folder, scheduler })
                        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
                }
            }
        }

        public bool SnapshotPending => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Galtek", "Classroom", "Installer", "setup-rollback.json"));

        public LegacyBundleRecord GetBundle(LegacyBundleDefinition definition)
        {
            if (definition == null || !CleanupPolicy.TryGetBundle(definition.BundleId, out _)) throw new InvalidOperationException("BUNDLE_NOT_ALLOWLISTED");
            var expectedPath = ExpectedCachePath(definition);
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
            using (var key = baseKey.OpenSubKey(UninstallRoot + "\\" + definition.BundleId, false))
            {
                var registeredPath = key?.GetValue("BundleCachePath") as string;
                return new LegacyBundleRecord
                {
                    BundleId = definition.BundleId,
                    ArpExists = key != null,
                    ProductName = key?.GetValue("DisplayName") as string,
                    Manufacturer = key?.GetValue("Publisher") as string,
                    Version = key?.GetValue("BundleVersion") as string,
                    UpgradeCodes = ToStrings(key?.GetValue("BundleUpgradeCode")),
                    ProviderKey = key?.GetValue("BundleProviderKey") as string,
                    RegisteredCachePath = registeredPath,
                    ExpectedCachePath = expectedPath,
                    CachePathSafe = IsExactCachePath(definition.BundleId, definition.CacheExecutableName, registeredPath, true),
                    CacheExists = File.Exists(expectedPath) || Directory.Exists(ExpectedCacheDirectory(definition))
                };
            }
        }

        public bool IsMsiInstalled(string productCode) => MsiQueryProductState(productCode) == 5;

        public bool IsHistoricalBundleProcessActive(LegacyBundleDefinition definition)
        {
            var expected = ExpectedCachePath(definition);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expected)))
            {
                try
                {
                    if (!TryGetProcessPath(process.Id, out var path)) return true;
                    if (String.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { return true; }
                finally { process.Dispose(); }
            }
            return false;
        }

        public void RemoveDependencyEdge(string providerKey, string dependentId)
        {
            if (!CleanupPolicy.IsKnownEdge(providerKey, dependentId) || CleanupPolicy.EqualsIgnoreCase(dependentId, this.CurrentBundleId))
                throw new InvalidOperationException("EDGE_NOT_ALLOWLISTED");
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
            using (var provider = baseKey.OpenSubKey(DependenciesRoot + "\\" + providerKey, true))
            {
                if (provider == null) return;
                using (var dependents = provider.OpenSubKey("Dependents", true))
                {
                    if (dependents == null) return;
                    dependents.DeleteSubKeyTree(dependentId, false);
                    if (dependents.SubKeyCount != 0) return;
                }
                provider.DeleteSubKey("Dependents", false);
            }
        }

        public void RemoveProvider(LegacyProviderDefinition definition)
        {
            if (definition == null || !CleanupPolicy.TryGetProvider(definition.Key, out _)) throw new InvalidOperationException("PROVIDER_NOT_ALLOWLISTED");
            if (String.Equals(definition.Key, CleanupPolicy.CurrentBundleProviderKey, StringComparison.Ordinal) ||
                String.Equals(definition.Key, CleanupPolicy.CurrentMsiProductCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CURRENT_PROVIDER_PROTECTED");
            var live = GetProvider(definition.Key);
            if (!live.Exists) return;
            if (!CleanupPolicy.EqualsIgnoreCase(live.OwnerId, definition.OwnerId) || CleanupPolicy.NormalizeVersion(live.Version) != definition.Version ||
                !String.Equals(live.DisplayName, CleanupPolicy.ProductName, StringComparison.Ordinal) || (live.Dependents?.Length ?? 0) != 0)
                throw new InvalidOperationException("PROVIDER_IDENTITY_CHANGED");
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
                DeleteExactSubKeyTree(baseKey, DependenciesRoot, definition.Key);
        }

        public void RemoveCache(LegacyBundleDefinition definition)
        {
            if (definition == null || !CleanupPolicy.TryGetBundle(definition.BundleId, out _)) throw new InvalidOperationException("BUNDLE_NOT_ALLOWLISTED");
            var live = GetBundle(definition);
            if (!live.CachePathSafe) throw new InvalidOperationException("CACHE_PATH_CHANGED");
            var directory = ExpectedCacheDirectory(definition);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        public void RemoveArp(LegacyBundleDefinition definition)
        {
            if (definition == null || !CleanupPolicy.TryGetBundle(definition.BundleId, out _)) throw new InvalidOperationException("BUNDLE_NOT_ALLOWLISTED");
            var live = GetBundle(definition);
            if (!live.ArpExists) return;
            var valid = String.Equals(live.ProductName, CleanupPolicy.ProductName, StringComparison.Ordinal) &&
                String.Equals(live.Manufacturer, CleanupPolicy.Manufacturer, StringComparison.Ordinal) &&
                CleanupPolicy.NormalizeVersion(live.Version) == definition.Version &&
                String.Equals(live.ProviderKey, definition.ProviderKey, StringComparison.OrdinalIgnoreCase) && live.CachePathSafe;
            if (!valid) throw new InvalidOperationException("ARP_IDENTITY_CHANGED");
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, this.view))
                DeleteExactSubKeyTree(baseKey, UninstallRoot, definition.BundleId);
        }

        private static string ExpectedCacheDirectory(LegacyBundleDefinition definition) => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache", definition.BundleId));
        private static string ExpectedCachePath(LegacyBundleDefinition definition) => Path.Combine(ExpectedCacheDirectory(definition), definition.CacheExecutableName);

        private static bool IsExactCachePath(string bundleId, string executableName, string registeredPath, bool allowMissingValue)
        {
            try
            {
                var expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Package Cache", bundleId, executableName));
                if (String.IsNullOrWhiteSpace(registeredPath)) return allowMissingValue;
                return String.Equals(Path.GetFullPath(registeredPath), expected, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static int CountProviderRegistrations(RegistryKey uninstallRoot, string providerKey)
        {
            if (uninstallRoot == null || String.IsNullOrWhiteSpace(providerKey)) return 0;
            var count = 0;
            foreach (var name in uninstallRoot.GetSubKeyNames())
                using (var key = uninstallRoot.OpenSubKey(name, false))
                    if (String.Equals(key?.GetValue("BundleProviderKey") as string, providerKey, StringComparison.Ordinal)) count++;
            return count;
        }

        private static int? ReadNumber(RegistryKey key, string name) => key?.GetValue(name) is int number ? (int?)number : null;

        private static bool TryGetProcessPath(int processId, out string path)
        {
            path = null;
            using (var handle = OpenProcess(QueryLimitedInformation, false, processId))
            {
                if (handle == null || handle.IsInvalid) return false;
                var capacity = 32768;
                var builder = new StringBuilder(capacity);
                if (!QueryFullProcessImageName(handle, 0, builder, ref capacity)) return false;
                path = builder.ToString();
                return !String.IsNullOrWhiteSpace(path);
            }
        }

        private static void DeleteExactSubKeyTree(RegistryKey baseKey, string parentPath, string childName)
        {
            if (String.IsNullOrWhiteSpace(childName) || childName.IndexOf('\\') >= 0 || childName.IndexOf('/') >= 0) throw new InvalidOperationException("UNSAFE_REGISTRY_CHILD");
            using (var parent = baseKey.OpenSubKey(parentPath, true)) parent?.DeleteSubKeyTree(childName, false);
        }

        private static string[] ToStrings(object value) => value as string[] ?? (value is string text ? new[] { text } : new string[0]);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiQueryProductState(string productCode);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder executableName, ref int size);
    }
}
