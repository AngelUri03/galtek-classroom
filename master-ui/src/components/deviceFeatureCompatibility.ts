export type DeviceFeatureState = "AVAILABLE" | "REQUIRES_CLIENT_UPDATE" | "UNAVAILABLE" | "UNKNOWN";

export type DeviceFeature =
  | "keyboardMouse" | "power" | "openLinks"
  | "sessionState" | "profileLogon" | "profileSwitch" | "sessionLogoff" | "adminSession"
  | "accountInventory" | "profileBinding" | "credentialProvisioning" | "credentialRemoval"
  | "credentialRevealMasterOnly" | "navigationRules" | "downloadControl";

const requiredCapability: Partial<Record<DeviceFeature, string>> = {
  keyboardMouse: "INPUT_CONTROL_V1",
  power: "POWER_CONTROL_V1",
  openLinks: "OPEN_URL_V1",
  sessionState: "WINDOWS_SESSION_STATE_V1",
  profileLogon: "WINDOWS_SESSION_LOGON_V1",
  profileSwitch: "WINDOWS_SESSION_SWITCH_V1",
  sessionLogoff: "WINDOWS_SESSION_LOGOFF_V1",
  adminSession: "ADMIN_MANAGED_SESSION_V1",
  accountInventory: "WINDOWS_ACCOUNT_INVENTORY_V1",
  profileBinding: "MANAGED_ACCOUNT_BINDING_V2",
  credentialProvisioning: "MANAGED_CREDENTIAL_PROVISIONING_V1",
  credentialRemoval: "MANAGED_CREDENTIAL_REMOVAL_V1",
  navigationRules: "BROWSER_NAVIGATION_POLICY_V1",
  downloadControl: "BROWSER_DOWNLOAD_POLICY_V1"
};

export function deviceFeatureCompatibility(
  capabilities: readonly string[] | null | undefined,
  feature: DeviceFeature
): DeviceFeatureState {
  if (feature === "credentialRevealMasterOnly") return "AVAILABLE";
  if (!capabilities) return "UNKNOWN";
  const required = requiredCapability[feature];
  if (!required) return "UNAVAILABLE";
  return capabilities.includes(required) ? "AVAILABLE" : "REQUIRES_CLIENT_UPDATE";
}
