import type { ReactNode } from "react";
import { Sidebar } from "../../components/Sidebar";
import { TopHeader } from "../../components/TopHeader";
import type { ClassroomOption } from "../../types/classroom";

export type HeaderState = {
  classroomName: string;
  masterStatus: string;
  masterStatusTone: "neutral" | "success" | "warning";
  currentAccountName?: string;
};

type AppShellProps = {
  activeSection: string;
  header: HeaderState;
  classrooms: ClassroomOption[];
  selectedClassroomId?: string;
  children: ReactNode;
  onClassroomChange: (classroomId: string) => void;
  onSectionChange: (sectionId: string) => void;
};

export function AppShell({
  activeSection,
  header,
  classrooms,
  selectedClassroomId,
  children,
  onClassroomChange,
  onSectionChange
}: AppShellProps) {
  return (
    <div className="app-shell">
      <Sidebar activeSection={activeSection} onSectionChange={onSectionChange} />
      <div className="app-shell__workspace">
        <TopHeader
          classroomName={header.classroomName}
          classrooms={classrooms}
          selectedClassroomId={selectedClassroomId}
          masterStatus={header.masterStatus}
          masterStatusTone={header.masterStatusTone}
          currentAccountName={header.currentAccountName}
          onClassroomChange={onClassroomChange}
        />
        <main className="app-main" tabIndex={-1}>
          {children}
        </main>
      </div>
    </div>
  );
}
