import { createContext, useCallback, useContext, useMemo, useRef, type ReactNode } from "react";
import { Toast, type ToastMessage } from "primereact/toast";
import { KeyedRegistry } from "./keyedRegistry";

export type AppToastSeverity = "info" | "success" | "warn" | "error";
export type AppToastMessage = {
  key: string;
  severity: AppToastSeverity;
  summary: string;
  detail: string;
  sticky?: boolean;
  life?: number;
};

type AppToastApi = {
  show: (message: AppToastMessage) => void;
  clear: (key: string) => void;
};

const AppToastContext = createContext<AppToastApi | null>(null);

export function AppToastProvider({ children }: { children: ReactNode }) {
  const toastRef = useRef<Toast>(null);
  const active = useRef(new KeyedRegistry<ToastMessage>());

  const clear = useCallback((key: string) => {
    const previous = active.current.get(key);
    if (previous) toastRef.current?.remove(previous);
    active.current.delete(key);
  }, []);

  const show = useCallback((message: AppToastMessage) => {
    const next: ToastMessage = {
      severity: message.severity,
      summary: message.summary,
      detail: message.detail,
      sticky: message.sticky,
      life: message.sticky ? undefined : (message.life ?? 4200),
      closable: true
    };
    const previous = active.current.replace(message.key, next);
    if (previous) toastRef.current?.remove(previous);
    toastRef.current?.show(next);
    if (!message.sticky) {
      window.setTimeout(() => {
        active.current.deleteIfCurrent(message.key, next);
      }, next.life ?? 4200);
    }
  }, []);

  const value = useMemo(() => ({ show, clear }), [show, clear]);
  return (
    <AppToastContext.Provider value={value}>
      {children}
      <Toast ref={toastRef} position="top-right" className="galtek-toast" />
    </AppToastContext.Provider>
  );
}

export function useAppToast() {
  const context = useContext(AppToastContext);
  if (!context) throw new Error("useAppToast must be used inside AppToastProvider.");
  return context;
}
