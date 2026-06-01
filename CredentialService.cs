using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace UbisoftAutoLogin;

internal sealed class CredentialService
{
    public const string UsernameTarget = "UbisoftAutoLogin:Username";
    public const string PasswordTarget = "UbisoftAutoLogin:Password";

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    private readonly AppLogger _logger;

    public CredentialService(AppLogger logger)
    {
        _logger = logger;
    }

    public bool HasCredentials() => HasCredential(UsernameTarget) && HasCredential(PasswordTarget);

    public SavedCredentials? ReadCredentials()
    {
        var username = ReadCredential(UsernameTarget);
        var password = ReadCredential(PasswordTarget);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            password = null;
            return null;
        }

        return new SavedCredentials(username, password);
    }

    public string? ReadUsername() => ReadCredential(UsernameTarget);

    public void SaveCredentials(string username, string password)
    {
        SaveCredential(UsernameTarget, username);
        SaveCredential(PasswordTarget, password);
        _logger.Info("Credentials saved in Windows Credential Manager.");
    }

    private bool HasCredential(string targetName) => ReadCredential(targetName) is not null;

    private string? ReadCredential(string targetName)
    {
        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168)
            {
                _logger.Warn($"Credential read failed for target '{targetName}' with Win32 error {error}.");
            }

            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes);
            }
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private void SaveCredential(string targetName, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = IntPtr.Zero;

        try
        {
            blob = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, blob, bytes.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = targetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            if (blob != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(blob);
            }

            Array.Clear(bytes);
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}

internal sealed record SavedCredentials(string Username, string Password);
