import type { ReactNode } from "react";
import { Sidebar } from "../../components/Sidebar";
import { TopHeader } from "../../components/TopHeader";
import { mockClassroomDashboard } from "../../mock/mockClassroomDashboard";

type AppShellProps = {
  activeSection: string;
  children: ReactNode;
  onSectionChange: (sectionId: string) => void;
};

export function AppShell({ activeSection, children, onSectionChange }: AppShellProps) {
  return (
    <div className="app-shell">
      <Sidebar activeSection={activeSection} onSectionChange={onSectionChange} />
      <div className="app-shell__workspace">
        <TopHeader
          classroomName={mockClassroomDashboard.classroomName}
          masterStatus={mockClassroomDashboard.masterStatus}
          teacherName={mockClassroomDashboard.teacherName}
        />
        <main className="app-main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
