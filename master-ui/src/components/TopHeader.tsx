import { CircleCheck, GraduationCap, UserRound } from "lucide-react";

type TopHeaderProps = {
  classroomName: string;
  masterStatus: string;
  teacherName: string;
};

export function TopHeader({ classroomName, masterStatus, teacherName }: TopHeaderProps) {
  return (
    <header className="top-header">
      <div className="top-header__product">
        <span className="top-header__product-name">Galtek Classroom</span>
        <span className="top-header__classroom">
          <GraduationCap size={16} aria-hidden="true" />
          {classroomName}
        </span>
      </div>

      <div className="top-header__meta">
        <span className="status-pill" role="status" aria-label={`Estado general del Master: ${masterStatus}`}>
          <CircleCheck size={16} aria-hidden="true" />
          <span className="status-pill__dot" aria-hidden="true" />
          {masterStatus}
        </span>
        <div className="teacher-chip" aria-label="Area futura de usuario o profesora">
          <UserRound size={18} aria-hidden="true" />
          <span>{teacherName}</span>
        </div>
      </div>
    </header>
  );
}
