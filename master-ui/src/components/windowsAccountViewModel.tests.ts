import type { ManagedAccountSlot, WindowsAccount } from "../api/windowsAccountsApi";
import {
  eligibleAccountsForRole,
  eligibleRolesForAccount,
  toWindowsAccountInventoryView
} from "./windowsAccountViewModel";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`WINDOWS_ACCOUNT_VIEWMODEL_TEST_FAILED: ${message}`);
}

function account(
  accountName: string,
  administrator: boolean,
  overrides: Partial<WindowsAccount> = {}
): WindowsAccount {
  return {
    accountName,
    displayName: accountName,
    enabled: true,
    administrator,
    builtIn: false,
    managedRole: "NONE",
    ...overrides
  };
}

const emptySlots: ManagedAccountSlot[] = (["PRIMARY", "SECONDARY", "ADMIN"] as const).map((accountId) => ({
  accountId,
  configured: false,
  credentialConfigured: false,
  credentialStatus: "NOT_CONFIGURED",
  windowsAccountName: null
}));

const school = account("SCHOOL-14", false);
const admin = account("ADMIN-14", true);
const disabled = account("DISABLED-14", false, { enabled: false });
const builtIn = account("Administrator", true, { builtIn: true });

assert(eligibleRolesForAccount(school, emptySlots).join(",") === "PRIMARY,SECONDARY",
  "a school account must only be eligible for PRIMARY/SECONDARY");
assert(eligibleRolesForAccount(admin, emptySlots).join(",") === "ADMIN",
  "an administrator must only be eligible for ADMIN");
assert(eligibleRolesForAccount(disabled, emptySlots).length === 0,
  "disabled accounts must be ineligible");
assert(eligibleRolesForAccount(builtIn, emptySlots).length === 0,
  "built-in accounts must be ineligible");
assert(eligibleAccountsForRole("PRIMARY", [school, admin]).map((item) => item.accountName).join() === "SCHOOL-14",
  "PRIMARY must filter administrators");
assert(eligibleAccountsForRole("ADMIN", [school, admin]).map((item) => item.accountName).join() === "ADMIN-14",
  "ADMIN must require an administrator");

const view = toWindowsAccountInventoryView(
  {
    deviceId: "device-14",
    accounts: [
      { ...school, managedRole: "PRIMARY" },
      { ...admin, managedRole: "ADMIN" }
    ]
  },
  {
    accounts: [
      { ...emptySlots[0], configured: true, credentialStatus: "CREDENTIAL_NOT_CONFIGURED", windowsAccountName: school.accountName },
      emptySlots[1],
      { ...emptySlots[2], configured: true, credentialConfigured: true, credentialStatus: "READY", windowsAccountName: admin.accountName }
    ]
  },
  "PRIMARY_ACTIVE"
);

assert(view.managed.length === 3, "the presentation must always expose three stable slots");
assert(view.managed[0].statusLabel === "Falta contraseña", "partial binding must use missing-password copy");
assert(view.managed[0].active, "PRIMARY_ACTIVE must mark PRIMARY active");
assert(view.managed[2].statusLabel === "Credencial lista", "READY must use protected-ready copy");
assert(view.managed[2].credentialLabel === "Guardada de forma protegida",
  "READY must not claim that the password was authenticated");
assert(!view.managed[2].remoteLoginSupported && !view.managed[2].active,
  "ADMIN must not enable remote login or managed session activation");

console.log("WINDOWS_ACCOUNT_VIEWMODEL_TESTS_PASS role-filters status-copy partial-state admin-remote-login-disabled");
