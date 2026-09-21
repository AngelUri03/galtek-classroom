import { useEffect, useRef, useState } from "react";
import {
  Activity,
  Check,
  ChevronDown,
  CircleCheck,
  GraduationCap,
  TriangleAlert,
  UserRound
} from "lucide-react";
import type { ClassroomOption } from "../types/classroom";

type TopHeaderProps = {
  classroomName: string;
  classrooms: ClassroomOption[];
  selectedClassroomId?: string;
  masterStatus: string;
  masterStatusTone: "neutral" | "success" | "warning";
  currentAccountName?: string;
  onClassroomChange: (classroomId: string) => void;
};

const statusIcons = {
  neutral: Activity,
  success: CircleCheck,
  warning: TriangleAlert
};

export function TopHeader({
  classroomName,
  classrooms,
  selectedClassroomId,
  masterStatus,
  masterStatusTone,
  currentAccountName,
  onClassroomChange
}: TopHeaderProps) {
  const StatusIcon = statusIcons[masterStatusTone];
  const [isClassroomMenuOpen, setIsClassroomMenuOpen] = useState(false);
  const selectorRef = useRef<HTMLDivElement | null>(null);
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const selectedIndex = Math.max(
    0,
    classrooms.findIndex((classroom) => classroom.classroomId === selectedClassroomId)
  );

  useEffect(() => {
    if (!isClassroomMenuOpen) {
      return;
    }

    const handlePointerDown = (event: PointerEvent) => {
      if (!selectorRef.current?.contains(event.target as Node)) {
        setIsClassroomMenuOpen(false);
      }
    };

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsClassroomMenuOpen(false);
      }
    };

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isClassroomMenuOpen]);

  useEffect(() => {
    if (isClassroomMenuOpen) {
      optionRefs.current[selectedIndex]?.focus();
    }
  }, [isClassroomMenuOpen, selectedIndex]);

  const openMenu = () => setIsClassroomMenuOpen((isOpen) => !isOpen);
  const selectClassroom = (classroomId: string) => {
    onClassroomChange(classroomId);
    setIsClassroomMenuOpen(false);
  };

  const moveFocus = (direction: 1 | -1) => {
    const activeIndex = optionRefs.current.findIndex((item) => item === document.activeElement);
    const baseIndex = activeIndex >= 0 ? activeIndex : selectedIndex;
    const nextIndex = (baseIndex + direction + classrooms.length) % classrooms.length;
    optionRefs.current[nextIndex]?.focus();
  };

  return (
    <header className="top-header">
      <div className="top-header__product">
        <span className="top-header__product-name">Galtek Classroom</span>
        {classrooms.length > 0 ? (
          <div className="classroom-selector" ref={selectorRef}>
            <button
              className="classroom-selector__trigger"
              type="button"
              aria-haspopup="listbox"
              aria-expanded={isClassroomMenuOpen}
              onClick={openMenu}
              onKeyDown={(event) => {
                if (event.key === "ArrowDown" || event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  setIsClassroomMenuOpen(true);
                }
              }}
            >
              <GraduationCap size={16} aria-hidden="true" />
              <span>{classroomName}</span>
              <ChevronDown size={15} aria-hidden="true" />
            </button>
            {isClassroomMenuOpen ? (
              <div className="classroom-selector__menu" role="listbox" aria-label="Aulas activas">
                {classrooms.map((classroom, index) => {
                  const isSelected = classroom.classroomId === selectedClassroomId;

                  return (
                    <button
                      key={classroom.classroomId}
                      ref={(element) => {
                        optionRefs.current[index] = element;
                      }}
                      className="classroom-selector__option"
                      type="button"
                      role="option"
                      aria-selected={isSelected}
                      tabIndex={isSelected ? 0 : -1}
                      onClick={() => selectClassroom(classroom.classroomId)}
                      onKeyDown={(event) => {
                        if (event.key === "ArrowDown") {
                          event.preventDefault();
                          moveFocus(1);
                        }
                        if (event.key === "ArrowUp") {
                          event.preventDefault();
                          moveFocus(-1);
                        }
                        if (event.key === "Enter" || event.key === " ") {
                          event.preventDefault();
                          selectClassroom(classroom.classroomId);
                        }
                      }}
                    >
                      <span>{classroom.displayName}</span>
                      {isSelected ? <Check size={16} aria-hidden="true" /> : null}
                    </button>
                  );
                })}
              </div>
            ) : null}
          </div>
        ) : (
          <span className="top-header__classroom">
            <GraduationCap size={16} aria-hidden="true" />
            {classroomName}
          </span>
        )}
      </div>

      <div className="top-header__meta">
        <span
          className={`status-pill status-pill--${masterStatusTone}`}
          role="status"
          aria-label={`Estado del Master: ${masterStatus}`}
        >
          <StatusIcon size={16} aria-hidden="true" />
          <span className="status-pill__dot" aria-hidden="true" />
          {masterStatus}
        </span>
        {currentAccountName ? (
          <div className="teacher-chip" aria-label="Cuenta actual autorizada">
            <UserRound size={18} aria-hidden="true" />
            <span>{currentAccountName}</span>
          </div>
        ) : null}
      </div>
    </header>
  );
}
