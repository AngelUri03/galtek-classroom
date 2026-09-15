import type { ClassroomDashboardData } from "../types/classroom";

export const mockClassroomDashboard: ClassroomDashboardData = {
  classroomName: "Aula Primaria - placeholder",
  teacherName: "Profesora - futuro usuario",
  masterStatus: "Master listo",
  summary: [
    { id: "devices", label: "Equipos", value: 26, detail: "Total temporal", tone: "neutral" },
    { id: "online", label: "En linea", value: 21, detail: "Placeholder local", tone: "success" },
    { id: "inUse", label: "En uso", value: 18, detail: "Sin API real", tone: "info" },
    { id: "attention", label: "Con atencion", value: 3, detail: "Datos dummy", tone: "warning" }
  ],
  devices: [
    {
      id: "pc-01",
      label: "PC01",
      studentName: "Alicia",
      status: "inUse",
      secondaryStatus: "PRIMARY activo",
      note: "Actividad abierta",
      selected: true
    },
    {
      id: "pc-02",
      label: "PC02",
      studentName: "Mateo",
      status: "online",
      secondaryStatus: "Disponible",
      note: "Lista para iniciar"
    },
    {
      id: "pc-03",
      label: "PC03",
      studentName: "Sofia",
      status: "attention",
      secondaryStatus: "Requiere revision",
      note: "Conexion por validar"
    },
    {
      id: "pc-04",
      label: "PC04",
      studentName: "Diego",
      status: "inUse",
      secondaryStatus: "En clase",
      note: "Trabajo local activo"
    },
    {
      id: "pc-05",
      label: "PC05",
      studentName: "Valeria",
      status: "online",
      secondaryStatus: "Sin alumno fijo",
      note: "Equipo disponible"
    },
    {
      id: "pc-06",
      label: "PC06",
      studentName: "Temporal",
      status: "degraded",
      secondaryStatus: "Rendimiento limitado",
      note: "Placeholder futuro"
    }
  ]
};
