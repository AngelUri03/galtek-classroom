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
    public const string ApplyBrowserNavigationPolicy = "APPLY_BROWSER_NAVIGATION_POLICY";
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
    public const string RemovableStorage = "REMOVABLE_STORAGE";
}

public static class ClassroomStudentAssignmentStrategies
{
    public const string ListOrder = "LIST_ORDER";
    public const string Random = "RANDOM";
    public const string Previous = "PREVIOUS";
    public const string Manual = "MANUAL";
}

public static class ClassroomStudentPreparationStages
{
    public const string Assigned = "ASSIGNED";
    public const string PreparingWindowsSession = "PREPARING_WINDOWS_SESSION";
    public const string PreparingWorkspace = "PREPARING_WORKSPACE";
    public const string PreparingBrowser = "PREPARING_BROWSER";
    public const string ApplyingClassContext = "APPLYING_CLASS_CONTEXT";
    public const string Ready = "READY";
}

public static class ClassroomStudentPreparationStatuses
{
    public const string Pending = "PENDING";
    public const string InProgress = "IN_PROGRESS";
    public const string Ready = "READY";
    public const string PartialReady = "PARTIAL_READY";
    public const string RecoveryRequired = "RECOVERY_REQUIRED";
    public const string Failed = "FAILED";
}

public static class ClassroomWorkspaceResidencyStates
{
    public const string NotMaterialized = "NOT_MATERIALIZED";
    public const string Materializing = "MATERIALIZING";
    public const string Ready = "READY";
    public const string RecoveryRequired = "RECOVERY_REQUIRED";
    public const string Error = "ERROR";
}

public static class ClassroomWorkspaceSyncStates
{
    public const string Synced = "SYNCED";
    public const string DirtyLocal = "DIRTY_LOCAL";
    public const string Syncing = "SYNCING";
    public const string PendingSync = "PENDING_SYNC";
    public const string RecoveryRequired = "RECOVERY_REQUIRED";
    public const string Conflict = "CONFLICT";
    public const string Error = "ERROR";
}

public static class ClassroomProjectionModes
{
    public const string ScreenShare = "SCREEN_SHARE";
    public const string Whiteboard = "WHITEBOARD";
    public const string Pointer = "POINTER";
    public const string LocalMedia = "LOCAL_MEDIA";
    public const string OpenWebContent = "OPEN_WEB_CONTENT";
}

public static class ClassroomOperationPriorities
{
    public const string Critical = "CRITICAL";
    public const string High = "HIGH";
    public const string Normal = "NORMAL";
    public const string Low = "LOW";
}

public static class ClassroomDevicePerformanceProfiles
{
    public const string Legacy = "LEGACY";
    public const string Standard = "STANDARD";
}

public static class ClassroomMasterPerformanceProfiles
{
    public const string MasterBalanced = "MASTER_BALANCED";
}

public static class ClassroomResourceWorkClasses
{
    public const string ControlCritical = "CONTROL_CRITICAL";
    public const string ClassPreparation = "CLASS_PREPARATION";
    public const string Interactive = "INTERACTIVE";
    public const string Transfer = "TRANSFER";
    public const string Visual = "VISUAL";
    public const string Background = "BACKGROUND";
}

public static class ClassroomResourcePressureStates
{
    public const string Normal = "NORMAL";
    public const string Degraded = "DEGRADED";
}

public static class ClassroomSheddableWork
{
    public const string Prefetch = "PREFETCH";
    public const string NonEssentialInventory = "NON_ESSENTIAL_INVENTORY";
    public const string Thumbnails = "THUMBNAILS";
    public const string PreviewQualityOrFps = "PREVIEW_QUALITY_OR_FPS";
    public const string NonUrgentTransfer = "NON_URGENT_TRANSFER";
    public const string BackgroundJob = "BACKGROUND_JOB";
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
    public const string UrlLaunchFailed = "URL_LAUNCH_FAILED";
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
    public const string SessionAgentUnavailable = "SESSION_AGENT_UNAVAILABLE";
    public const string SessionChannelUnauthorized = "SESSION_CHANNEL_UNAUTHORIZED";
    public const string SessionChannelProtocolMismatch = "SESSION_CHANNEL_PROTOCOL_MISMATCH";
    public const string SessionChannelInvalidResponse = "SESSION_CHANNEL_INVALID_RESPONSE";
    public const string SessionCommandResultUnknown = "SESSION_COMMAND_RESULT_UNKNOWN";
    public const string PowerControlUnavailable = "POWER_CONTROL_UNAVAILABLE";
    public const string PowerControlFailed = "POWER_CONTROL_FAILED";
    public const string BrowserPolicyInvalid = "BROWSER_POLICY_INVALID";
    public const string BrowserPolicyNotNativeEnforceable = "BROWSER_POLICY_NOT_NATIVE_ENFORCEABLE";
    public const string BrowserPolicyTooLarge = "BROWSER_POLICY_TOO_LARGE";
    public const string BrowserPolicyUserUnavailable = "BROWSER_POLICY_USER_UNAVAILABLE";
    public const string BrowserAccountScopeUnresolved = "BROWSER_ACCOUNT_SCOPE_UNRESOLVED";
    public const string BrowserPolicyUserHiveUnavailable = "BROWSER_POLICY_USER_HIVE_UNAVAILABLE";
    public const string BrowserPolicyExternalConflict = "BROWSER_POLICY_EXTERNAL_CONFLICT";
    public const string BrowserPolicyApplyFailed = "BROWSER_POLICY_APPLY_FAILED";
    public const string BrowserPolicyRollbackFailed = "BROWSER_POLICY_ROLLBACK_FAILED";
    public const string BrowserPolicyRecoveryRequired = "BROWSER_POLICY_RECOVERY_REQUIRED";
    public const string UrlBlockedByPolicy = "URL_BLOCKED_BY_POLICY";
}
