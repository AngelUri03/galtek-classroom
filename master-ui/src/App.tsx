import { useState } from "react";
import { AppShell } from "./app/layout/AppShell";
import { ClassroomDashboard } from "./pages/ClassroomDashboard";

export function App() {
  const [activeSection, setActiveSection] = useState("classroom");

  return (
    <AppShell activeSection={activeSection} onSectionChange={setActiveSection}>
      <ClassroomDashboard />
    </AppShell>
  );
}
