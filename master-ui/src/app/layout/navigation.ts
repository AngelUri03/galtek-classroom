import {
  Activity,
  AppWindow,
  Files,
  GraduationCap,
  Monitor,
  Navigation,
  Settings,
  type LucideIcon
} from "lucide-react";

export type MasterNavItem = {
  id: string;
  label: string;
  icon: LucideIcon;
  disabled?: boolean;
};

export const masterNavigationItems: MasterNavItem[] = [
  { id: "classroom", label: "Aula", icon: Monitor },
  { id: "students", label: "Alumnos", icon: GraduationCap, disabled: true },
  { id: "applications", label: "Aplicaciones", icon: AppWindow, disabled: true },
  { id: "files", label: "Archivos", icon: Files, disabled: true },
  { id: "navigation", label: "Navegacion", icon: Navigation, disabled: true },
  { id: "activity", label: "Actividad", icon: Activity, disabled: true },
  { id: "settings", label: "Configuracion", icon: Settings, disabled: true }
];
