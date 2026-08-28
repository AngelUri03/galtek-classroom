namespace GaltekClassroom.Agent.Shared;

public static class ClassroomOperationTypes
{
    public const string LockInput = "LOCK_INPUT";
    public const string UnlockInput = "UNLOCK_INPUT";
    public const string Shutdown = "SHUTDOWN";
    public const string Restart = "RESTART";
    public const string OpenApplication = "OPEN_APPLICATION";
    public const string OpenUrl = "OPEN_URL";
    public const string StartProjection = "START_PROJECTION";
    public const string StopProjection = "STOP_PROJECTION";
    public const string DistributeFile = "DISTRIBUTE_FILE";
    public const string CreateFolder = "CREATE_FOLDER";
    public const string SetWallpaper = "SET_WALLPAPER";
    public const string RestoreWallpaper = "RESTORE_WALLPAPER";
    public const string GetWindowsSessionState = "GET_WINDOWS_SESSION_STATE";
    public const string LogonManagedAccount = "LOGON_MANAGED_ACCOUNT";
    public const string LogoffWindowsSession = "LOGOFF_WINDOWS_SESSION";
    public const string SwitchManagedAccount = "SWITCH_MANAGED_ACCOUNT";
    public const string AssignStudent = "ASSIGN_STUDENT";
    public const string MoveStudent = "MOVE_STUDENT";
    public const string SwapStudents = "SWAP_STUDENTS";
    public const string SyncStudentWorkspace = "SYNC_STUDENT_WORKSPACE";
    public const string RestoreStudentWorkspace = "RESTORE_STUDENT_WORKSPACE";
}

public static class ClassroomOperationStatuses
{
    public const string Success = "SUCCESS";
    public const string PartialSuccess = "PARTIAL_SUCCESS";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";
    public const string RolledBack = "ROLLED_BACK";
}

public static class ClassroomTargetStatuses
{
    public const string Pending = "PENDING";
    public const string NoChange = "NO_CHANGE";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";
    public const string Skipped = "SKIPPED";
    public const string Cancelled = "CANCELLED";
    public const string RolledBack = "ROLLED_BACK";
}

public static class ClassroomPreflightStatuses
{
    public const string Ready = "READY";
    public const string Warning = "WARNING";
    public const string Blocked = "BLOCKED";
}

public static class ClassroomWorkspaceDestinations
{
    public const string WorkspaceRoot = "WORKSPACE_ROOT";
    public const string Documents = "DOCUMENTS";
    public const string Homework = "HOMEWORK";
    public const string Work = "WORK";
    public const string Downloads = "DOWNLOADS";
    public const string Desktop = "DESKTOP";
    public const string ClassroomShared = "CLASSROOM_SHARED";
}

public static class ClassroomConflictPolicies
{
    public const string Skip = "SKIP";
    public const string Replace = "REPLACE";
    public const string Rename = "RENAME";
}

public static class ClassroomManagedWindowsAccountTypes
{
    public const string Primary = "PRIMARY";
    public const string Secondary = "SECONDARY";
}

public static class ClassroomWindowsSessionStates
{
    public const string NoSession = "NO_SESSION";
    public const string PrimaryActive = "PRIMARY_ACTIVE";
    public const string SecondaryActive = "SECONDARY_ACTIVE";
    public const string OtherSessionActive = "OTHER_SESSION_ACTIVE";
    public const string Unknown = "UNKNOWN";
}

public static class ClassroomManagedAccountSwitchActions
{
    public const string NoChange = "NO_CHANGE";
    public const string Logon = "LOGON";
    public const string Switch = "SWITCH";
    public const string Pending = "PENDING";
    public const string Blocked = "BLOCKED";
}

public static class ClassroomOperationErrorCodes
{
    public const string DeviceOffline = "DEVICE_OFFLINE";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string AgentUnavailable = "AGENT_UNAVAILABLE";
    public const string SessionNotAvailable = "SESSION_NOT_AVAILABLE";
    public const string DeviceBusy = "DEVICE_BUSY";
    public const string LicenseNotActive = "LICENSE_NOT_ACTIVE";
    public const string FeatureNotAllowed = "FEATURE_NOT_ALLOWED";
    public const string StudentNotFound = "STUDENT_NOT_FOUND";
    public const string StudentNotAssigned = "STUDENT_NOT_ASSIGNED";
    public const string StudentAlreadyAssigned = "STUDENT_ALREADY_ASSIGNED";
    public const string TargetOccupied = "TARGET_OCCUPIED";
    public const string WorkspaceNotFound = "WORKSPACE_NOT_FOUND";
    public const string WorkspaceNotReady = "WORKSPACE_NOT_READY";
    public const string WorkspaceBusy = "WORKSPACE_BUSY";
    public const string InsufficientDiskSpace = "INSUFFICIENT_DISK_SPACE";
    public const string FileLocked = "FILE_LOCKED";
    public const string FileWriteFailed = "FILE_WRITE_FAILED";
    public const string TransferFailed = "TRANSFER_FAILED";
    public const string TransferIntegrityFailed = "TRANSFER_INTEGRITY_FAILED";
    public const string RollbackFailed = "ROLLBACK_FAILED";
    public const string BrowserNotAvailable = "BROWSER_NOT_AVAILABLE";
    public const string BrowserProfileNotFound = "BROWSER_PROFILE_NOT_FOUND";
    public const string BrowserProfileNotPortable = "BROWSER_PROFILE_NOT_PORTABLE";
    public const string BrowserReauthRequired = "BROWSER_REAUTH_REQUIRED";
    public const string InvalidUrl = "INVALID_URL";
    public const string ApplicationNotInstalled = "APPLICATION_NOT_INSTALLED";
    public const string ApplicationNotAllowed = "APPLICATION_NOT_ALLOWED";
    public const string ApplicationStartFailed = "APPLICATION_START_FAILED";
    public const string InvalidFile = "INVALID_FILE";
    public const string InvalidFolderName = "INVALID_FOLDER_NAME";
    public const string InvalidDestination = "INVALID_DESTINATION";
    public const string ImageInvalid = "IMAGE_INVALID";
    public const string AccountNotConfigured = "ACCOUNT_NOT_CONFIGURED";
    public const string ManagedCredentialNotConfigured = "MANAGED_CREDENTIAL_NOT_CONFIGURED";
    public const string WindowsSessionUnknown = "WINDOWS_SESSION_UNKNOWN";
    public const string WindowsLogonFailed = "WINDOWS_LOGON_FAILED";
    public const string WindowsLogoffFailed = "WINDOWS_LOGOFF_FAILED";
    public const string SessionSwitchFailed = "SESSION_SWITCH_FAILED";
    public const string CredentialProviderUnavailable = "CREDENTIAL_PROVIDER_UNAVAILABLE";
    public const string MasterNotLicensed = "MASTER_NOT_LICENSED";
    public const string MasterWindowsAccountNotAuthorized = "MASTER_WINDOWS_ACCOUNT_NOT_AUTHORIZED";
    public const string MasterNotPaired = "MASTER_NOT_PAIRED";
    public const string OperationNotImplemented = "OPERATION_NOT_IMPLEMENTED";
}
