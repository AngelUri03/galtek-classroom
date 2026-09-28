using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GaltekClassroom.Agent.Service.Identity;
using GaltekClassroom.Agent.Service.ManagedAccounts;
using GaltekClassroom.Agent.Service.NetworkTransport;
using GaltekClassroom.Agent.Shared;
using GaltekClassroom.Protocol.Network.V1;

namespace GaltekClassroom.Agent.Service.WindowsAccounts;

public sealed record WindowsLocalAccountRecord(
    string AccountName,
    string DisplayName,
    bool Enabled,
    bool Administrator,
    bool BuiltIn,
    string WindowsSid);

public interface IWindowsAccountInventorySource
{
    IReadOnlyList<WindowsLocalAccountRecord> Read(CancellationToken cancellationToken);
}

public sealed class WindowsAccountInventorySource : IWindowsAccountInventorySource
{
    private const uint NerrSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint UfAccountDisable = 0x0002;
    private const int MaxPreferredLength = -1;
    private const string BuiltInAdministratorsSid = "S-1-5-32-544";

    public IReadOnlyList<WindowsLocalAccountRecord> Read(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows account inventory requires Windows.");
        }

        string administratorsGroup = ResolveAccountName(BuiltInAdministratorsSid);
        HashSet<string> administratorSids = ReadLocalGroupMemberSids(administratorsGroup, cancellationToken);
        var accounts = new List<WindowsLocalAccountRecord>();
        UIntPtr resumeHandle = UIntPtr.Zero;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            IntPtr buffer = IntPtr.Zero;
            uint status = NetUserEnum(
                null,
                20,
                0,
                out buffer,
                MaxPreferredLength,
                out int entriesRead,
                out _,
                ref resumeHandle);
            try
            {
                if (status != NerrSuccess && status != ErrorMoreData)
                {
                    throw new Win32Exception((int)status, "NetUserEnum failed.");
                }

                int size = Marshal.SizeOf<UserInfo20>();
                for (int index = 0; index < entriesRead; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    UserInfo20 user = Marshal.PtrToStructure<UserInfo20>(IntPtr.Add(buffer, index * size));
                    if (string.IsNullOrWhiteSpace(user.Name))
                    {
                        continue;
                    }

                    string sid = ResolveLocalUserSid(user.Name);
                    accounts.Add(new WindowsLocalAccountRecord(
                        user.Name,
                        string.IsNullOrWhiteSpace(user.FullName) ? user.Name : user.FullName,
                        (user.Flags & UfAccountDisable) == 0,
                        administratorSids.Contains(sid),
                        IsBuiltInLocalAccountSid(sid),
                        sid));
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    _ = NetApiBufferFree(buffer);
                }
            }

            if (status != ErrorMoreData)
            {
                break;
            }
        }
        while (true);

        return accounts
            .OrderBy(account => account.AccountName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HashSet<string> ReadLocalGroupMemberSids(
        string groupName,
        CancellationToken cancellationToken)
    {
        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        UIntPtr resumeHandle = UIntPtr.Zero;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            IntPtr buffer = IntPtr.Zero;
            uint status = NetLocalGroupGetMembers(
                null,
                groupName,
                0,
                out buffer,
                MaxPreferredLength,
                out int entriesRead,
                out _,
                ref resumeHandle);
            try
            {
                if (status != NerrSuccess && status != ErrorMoreData)
                {
                    throw new Win32Exception((int)status, "NetLocalGroupGetMembers failed.");
                }

                int size = Marshal.SizeOf<LocalGroupMembersInfo0>();
                for (int index = 0; index < entriesRead; index++)
                {
                    LocalGroupMembersInfo0 member = Marshal.PtrToStructure<LocalGroupMembersInfo0>(
                        IntPtr.Add(buffer, index * size));
                    if (member.Sid != IntPtr.Zero)
                    {
                        members.Add(SidToString(member.Sid));
                    }
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    _ = NetApiBufferFree(buffer);
                }
            }

            if (status != ErrorMoreData)
            {
                break;
            }
        }
        while (true);

        return members;
    }

    private static string ResolveLocalUserSid(string accountName)
    {
        string qualifiedName = $"{Environment.MachineName}\\{accountName}";
        uint sidLength = 0;
        uint domainLength = 0;
        _ = LookupAccountName(null, qualifiedName, null, ref sidLength, null, ref domainLength, out _);
        int error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer || sidLength == 0)
        {
            throw new Win32Exception(error, "LookupAccountName could not size a local user SID.");
        }

        var sid = new byte[sidLength];
        var domain = new StringBuilder((int)domainLength);
        if (!LookupAccountName(null, qualifiedName, sid, ref sidLength, domain, ref domainLength, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LookupAccountName could not resolve a local user SID.");
        }

        GCHandle handle = GCHandle.Alloc(sid, GCHandleType.Pinned);
        try
        {
            return SidToString(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    private static string ResolveAccountName(string stringSid)
    {
        if (!ConvertStringSidToSid(stringSid, out IntPtr sid))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Built-in group SID could not be parsed.");
        }

        try
        {
            uint nameLength = 0;
            uint domainLength = 0;
            _ = LookupAccountSid(null, sid, null, ref nameLength, null, ref domainLength, out _);
            int error = Marshal.GetLastWin32Error();
            if (error != ErrorInsufficientBuffer || nameLength == 0)
            {
                throw new Win32Exception(error, "Built-in group SID could not be resolved.");
            }

            var name = new StringBuilder((int)nameLength);
            var domain = new StringBuilder((int)domainLength);
            if (!LookupAccountSid(null, sid, name, ref nameLength, domain, ref domainLength, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Built-in group SID could not be resolved.");
            }

            return name.ToString();
        }
        finally
        {
            _ = LocalFree(sid);
        }
    }

    private static string SidToString(IntPtr sid)
    {
        if (!ConvertSidToStringSid(sid, out IntPtr value))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows SID could not be converted.");
        }

        try
        {
            return Marshal.PtrToStringUni(value)
                ?? throw new InvalidOperationException("Windows returned an empty SID.");
        }
        finally
        {
            _ = LocalFree(value);
        }
    }

    private static bool IsBuiltInLocalAccountSid(string sid)
    {
        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int separator = sid.LastIndexOf('-');
        return separator >= 0
            && uint.TryParse(sid.AsSpan(separator + 1), out uint rid)
            && rid < 1000;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct UserInfo20
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string FullName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
        public uint Flags;
        public uint UserId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LocalGroupMembersInfo0
    {
        public IntPtr Sid;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint NetUserEnum(
        string? serverName,
        int level,
        int filter,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref UIntPtr resumeHandle);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint NetLocalGroupGetMembers(
        string? serverName,
        string localGroupName,
        int level,
        out IntPtr buffer,
        int preferredMaximumLength,
        out int entriesRead,
        out int totalEntries,
        ref UIntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern uint NetApiBufferFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LookupAccountNameW")]
    private static extern bool LookupAccountName(
        string? systemName,
        string accountName,
        byte[]? sid,
        ref uint sidSize,
        StringBuilder? referencedDomainName,
        ref uint referencedDomainNameSize,
        out int use);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LookupAccountSidW")]
    private static extern bool LookupAccountSid(
        string? systemName,
        IntPtr sid,
        StringBuilder? name,
        ref uint nameSize,
        StringBuilder? referencedDomainName,
        ref uint referencedDomainNameSize,
        out int use);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertStringSidToSidW")]
    private static extern bool ConvertStringSidToSid(string stringSid, out IntPtr sid);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertSidToStringSidW")]
    private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr stringSid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr value);
}

public sealed class GetWindowsAccountInventoryOperationHandler : IRemoteOperationHandler
{
    private readonly InstallationIdentityStore _installationIdentityStore;
    private readonly IManagedWindowsAccountBindingStore _bindingStore;
    private readonly IWindowsAccountInventorySource _source;

    public GetWindowsAccountInventoryOperationHandler(
        InstallationIdentityStore installationIdentityStore,
        IManagedWindowsAccountBindingStore bindingStore,
        IWindowsAccountInventorySource source)
    {
        _installationIdentityStore = installationIdentityStore;
        _bindingStore = bindingStore;
        _source = source;
    }

    public NetworkOperationType OperationType => NetworkOperationType.GetWindowsAccountInventory;

    public async Task<RemoteOperationHandlerResult> HandleAsync(
        OperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OperationParametersCase != OperationRequest.OperationParametersOneofCase.None)
        {
            return Failed(NetworkOperationErrorCode.ProtocolViolation,
                "GET_WINDOWS_ACCOUNT_INVENTORY does not accept parameters.");
        }

        InstallationIdentityStoreReadResult identity =
            await _installationIdentityStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (identity.Status != InstallationIdentityStoreReadStatus.Loaded || identity.Identity is null)
        {
            return Failed(NetworkOperationErrorCode.ProtocolViolation, "Installation identity is invalid.");
        }

        ManagedWindowsAccountBindingStoreReadResult bindings =
            await _bindingStore.ListAsync(identity.Identity.InstallationId, cancellationToken).ConfigureAwait(false);
        if (!bindings.Loaded)
        {
            return Failed(NetworkOperationErrorCode.ManagedAccountBindingsInvalid,
                "Managed Windows account bindings are invalid.");
        }

        IReadOnlyDictionary<string, string> managedBySid = bindings.Bindings.ToDictionary(
            binding => binding.WindowsSid,
            binding => binding.AccountId,
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<WindowsLocalAccountRecord> accounts;
        try
        {
            accounts = _source.Read(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Failed(NetworkOperationErrorCode.OperationRejected,
                "Windows account inventory could not be read.");
        }

        var result = new WindowsAccountInventoryResult();
        result.Accounts.AddRange(accounts.Select(account => new WindowsAccountInventoryEntry
        {
            AccountName = account.AccountName,
            DisplayName = account.DisplayName,
            Enabled = account.Enabled,
            Administrator = account.Administrator,
            BuiltIn = account.BuiltIn,
            ManagedRole = ManagedRole(account.WindowsSid, managedBySid)
        }));

        return RemoteOperationHandlerResult.Success("Windows account inventory was read.", result);
    }

    private static ManagedWindowsAccountId ManagedRole(
        string sid,
        IReadOnlyDictionary<string, string> managedBySid)
    {
        if (!managedBySid.TryGetValue(sid, out string? accountId))
        {
            return ManagedWindowsAccountId.Unspecified;
        }

        return accountId == ClassroomManagedWindowsAccountTypes.Primary
            ? ManagedWindowsAccountId.Primary
            : accountId == ClassroomManagedWindowsAccountTypes.Secondary
                ? ManagedWindowsAccountId.Secondary
                : accountId == ClassroomManagedWindowsAccountTypes.Admin
                    ? ManagedWindowsAccountId.Admin
                    : ManagedWindowsAccountId.Unspecified;
    }

    private static RemoteOperationHandlerResult Failed(NetworkOperationErrorCode code, string message)
    {
        return new RemoteOperationHandlerResult(OperationExecutionStatus.Failed, code, message);
    }
}
