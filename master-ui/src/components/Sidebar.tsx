import { ChevronLeft, ChevronRight } from "lucide-react";
import { useState } from "react";
import { masterNavigationItems } from "../app/layout/navigation";
import galtekLogo from "../assets/galtek-logo.png";

type SidebarProps = {
  activeSection: string;
  onSectionChange: (sectionId: string) => void;
};

export function Sidebar({ activeSection, onSectionChange }: SidebarProps) {
  const [collapsed, setCollapsed] = useState(false);

  return (
    <aside className={collapsed ? "sidebar sidebar--collapsed" : "sidebar"} aria-label="Navegacion principal">
      <div className="sidebar__brand">
        <span className="sidebar__logo-frame" aria-hidden="true">
          <img src={galtekLogo} alt="" />
        </span>
        {!collapsed && (
          <div className="sidebar__brand-text">
            <span>GALTEK</span>
            <strong>Classroom</strong>
          </div>
        )}
      </div>

      <nav className="sidebar__nav" aria-label="Secciones del Master">
        {masterNavigationItems.map((item) => {
          const Icon = item.icon;
          const active = item.id === activeSection;
          const className = [
            "sidebar__item",
            active ? "sidebar__item--active" : "",
            item.disabled ? "sidebar__item--disabled" : "",
            item.id === "settings" ? "sidebar__item--separated" : ""
          ]
            .filter(Boolean)
            .join(" ");

          return (
            <button
              key={item.id}
              type="button"
              className={className}
              aria-current={active ? "page" : undefined}
              aria-disabled={item.disabled ? true : undefined}
              title={collapsed ? item.label : undefined}
              onClick={() => {
                if (!item.disabled) {
                  onSectionChange(item.id);
                }
              }}
            >
              <Icon aria-hidden="true" size={19} strokeWidth={2} />
              {!collapsed && <span>{item.label}</span>}
            </button>
          );
        })}
      </nav>

      <button
        type="button"
        className="sidebar__collapse"
        aria-label={collapsed ? "Expandir barra lateral" : "Colapsar barra lateral"}
        onClick={() => setCollapsed((value) => !value)}
      >
        {collapsed ? <ChevronRight size={18} aria-hidden="true" /> : <ChevronLeft size={18} aria-hidden="true" />}
      </button>
    </aside>
  );
}
