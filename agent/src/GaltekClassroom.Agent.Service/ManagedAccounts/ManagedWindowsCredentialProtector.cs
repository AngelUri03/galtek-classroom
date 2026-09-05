using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;

namespace GaltekClassroom.Agent.Service.ManagedAccounts;

public enum ManagedWindowsCredentialProtectionStatus
{
    Protected,
    Unprotected,
    ProtectionFailed
}

public sealed record ManagedWindowsCredentialProtectionResult(
    ManagedWindowsCredentialProtectionStatus Status,
    byte[] Data,
    string? ErrorMessage)
{
    public bool Succeeded => Status is ManagedWindowsCredentialProtectionStatus.Protected
        or ManagedWindowsCredentialProtectionStatus.Unprotected;

    public static ManagedWindowsCredentialProtectionResult Protected(byte[] data)
    {
        return new ManagedWindowsCredentialProtectionResult(
            ManagedWindowsCredentialProtectionStatus.Protected,
            data,
            null);
    }

    public static ManagedWindowsCredentialProtectionResult Unprotected(byte[] data)
    {
        return new ManagedWindowsCredentialProtectionResult(
            ManagedWindowsCredentialProtectionStatus.Unprotected,
            data,
            null);
    }

    public static ManagedWindowsCredentialProtectionResult Failed(string errorMessage)
    {
        return new ManagedWindowsCredentialProtectionResult(
            ManagedWindowsCredentialProtectionStatus.ProtectionFailed,
            Array.Empty<byte>(),
            errorMessage);
    }
}

public interface IManagedWindowsCredentialProtector
{
    ManagedWindowsCredentialProtectionResult Protect(
        byte[] plaintext,
        byte[] optionalEntropy);

    ManagedWindowsCredentialProtectionResult Unprotect(
        byte[] protectedData,
        byte[] optionalEntropy);
}

public interface IWindowsDpapiManagedWindowsCredentialNativeApi
{
    bool IsWindows { get; }

    bool IsRunningAsLocalSystem();

    bool CryptProtectData(
        byte[] plaintext,
        byte[] optionalEntropy,
        int flags,
        out byte[] protectedData,
        out string errorMessage);

    bool CryptUnprotectData(
        byte[] protectedData,
        byte[] optionalEntropy,
        int flags,
        out byte[] plaintext,
        out string errorMessage);
}

public sealed class UnsupportedManagedWindowsCredentialProtector : IManagedWindowsCredentialProtector
{
    public ManagedWindowsCredentialProtectionResult Protect(
        byte[] plaintext,
        byte[] optionalEntropy)
    {
        return ManagedWindowsCredentialProtectionResult.Failed(
            "Managed Windows credentials are supported only by the Agent Service on Windows.");
    }

    public ManagedWindowsCredentialProtectionResult Unprotect(
        byte[] protectedData,
        byte[] optionalEntropy)
    {
        return ManagedWindowsCredentialProtectionResult.Failed(
            "Managed Windows credentials are supported only by the Agent Service on Windows.");
    }
}

public sealed class WindowsDpapiManagedWindowsCredentialProtector : IManagedWindowsCredentialProtector
{
    public const int CryptProtectUiForbidden = 0x1;

    private readonly IWindowsDpapiManagedWindowsCredentialNativeApi _native;

    public WindowsDpapiManagedWindowsCredentialProtector(
        IWindowsDpapiManagedWindowsCredentialNativeApi native)
    {
        _native = native;
    }

    public ManagedWindowsCredentialProtectionResult Protect(
        byte[] plaintext,
        byte[] optionalEntropy)
    {
        if (plaintext.Length == 0)
        {
            return ManagedWindowsCredentialProtectionResult.Failed(
                "Managed Windows credential payload is empty.");
        }

        if (!_native.IsWindows || !_native.IsRunningAsLocalSystem())
        {
            return ManagedWindowsCredentialProtectionResult.Failed(
                "Managed Windows credential protection requires LocalSystem on Windows.");
        }

        var plaintextCopy = plaintext.ToArray();
        var entropyCopy = optionalEntropy.ToArray();
        try
        {
            return _native.CryptProtectData(
                plaintextCopy,
                entropyCopy,
                CryptProtectUiForbidden,
                out var protectedData,
                out var errorMessage)
                ? ManagedWindowsCredentialProtectionResult.Protected(protectedData)
                : ManagedWindowsCredentialProtectionResult.Failed(errorMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextCopy);
            CryptographicOperations.ZeroMemory(entropyCopy);
        }
    }

    public ManagedWindowsCredentialProtectionResult Unprotect(
        byte[] protectedData,
        byte[] optionalEntropy)
    {
        if (protectedData.Length == 0)
        {
            return ManagedWindowsCredentialProtectionResult.Failed(
                "Managed Windows credential protected data is empty.");
        }

        if (!_native.IsWindows || !_native.IsRunningAsLocalSystem())
        {
            return ManagedWindowsCredentialProtectionResult.Failed(
                "Managed Windows credential unprotect requires LocalSystem on Windows.");
        }

        var protectedCopy = protectedData.ToArray();
        var entropyCopy = optionalEntropy.ToArray();
        try
        {
            return _native.CryptUnprotectData(
                protectedCopy,
                entropyCopy,
                CryptProtectUiForbidden,
                out var plaintext,
                out var errorMessage)
                ? ManagedWindowsCredentialProtectionResult.Unprotected(plaintext)
                : ManagedWindowsCredentialProtectionResult.Failed(errorMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedCopy);
            CryptographicOperations.ZeroMemory(entropyCopy);
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiManagedWindowsCredentialNativeApi : IWindowsDpapiManagedWindowsCredentialNativeApi
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public bool IsRunningAsLocalSystem()
    {
        return WindowsIdentity.GetCurrent().IsSystem;
    }

    public bool CryptProtectData(
        byte[] plaintext,
        byte[] optionalEntropy,
        int flags,
        out byte[] protectedData,
        out string errorMessage)
    {
        return CryptData(
            plaintext,
            optionalEntropy,
            flags,
            protect: true,
            out protectedData,
            out errorMessage);
    }

    public bool CryptUnprotectData(
        byte[] protectedData,
        byte[] optionalEntropy,
        int flags,
        out byte[] plaintext,
        out string errorMessage)
    {
        return CryptData(
            protectedData,
            optionalEntropy,
            flags,
            protect: false,
            out plaintext,
            out errorMessage);
    }

    private static bool CryptData(
        byte[] input,
        byte[] optionalEntropy,
        int flags,
        bool protect,
        out byte[] output,
        out string errorMessage)
    {
        output = Array.Empty<byte>();
        errorMessage = string.Empty;

        IntPtr inputBuffer = IntPtr.Zero;
        IntPtr entropyBuffer = IntPtr.Zero;
        var inputBlob = default(DataBlob);
        var entropyBlob = default(DataBlob);
        var outputBlob = default(DataBlob);

        try
        {
            inputBuffer = AllocAndCopy(input);
            inputBlob = new DataBlob(input.Length, inputBuffer);

            if (optionalEntropy.Length > 0)
            {
                entropyBuffer = AllocAndCopy(optionalEntropy);
                entropyBlob = new DataBlob(optionalEntropy.Length, entropyBuffer);
            }

            var entropyArgument = optionalEntropy.Length > 0 ? entropyBlob : default;
            bool succeeded = protect
                ? NativeCryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyArgument,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    flags,
                    out outputBlob)
                : NativeCryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    ref entropyArgument,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    flags,
                    out outputBlob);

            if (!succeeded || outputBlob.DataPointer == IntPtr.Zero || outputBlob.DataLength <= 0)
            {
                var error = Marshal.GetLastWin32Error();
                errorMessage = $"Managed Windows credential DPAPI operation failed: {new Win32Exception(error).Message}";
                return false;
            }

            output = new byte[outputBlob.DataLength];
            Marshal.Copy(outputBlob.DataPointer, output, 0, output.Length);
            return true;
        }
        finally
        {
            ZeroAndFree(inputBuffer, input.Length);
            ZeroAndFree(entropyBuffer, optionalEntropy.Length);

            if (outputBlob.DataPointer != IntPtr.Zero)
            {
                LocalFree(outputBlob.DataPointer);
            }
        }
    }

    private static IntPtr AllocAndCopy(byte[] source)
    {
        var buffer = Marshal.AllocHGlobal(source.Length);
        Marshal.Copy(source, 0, buffer, source.Length);
        return buffer;
    }

    private static void ZeroAndFree(IntPtr buffer, int length)
    {
        if (buffer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            Marshal.Copy(new byte[length], 0, buffer, length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CryptProtectData")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeCryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CryptUnprotectData")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeCryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", EntryPoint = "LocalFree", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob
    {
        public DataBlob(int dataLength, IntPtr dataPointer)
        {
            DataLength = dataLength;
            DataPointer = dataPointer;
        }

        public readonly int DataLength;
        public readonly IntPtr DataPointer;
    }
}
