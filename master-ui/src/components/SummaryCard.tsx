import { Activity, Monitor, MousePointer2, TriangleAlert, Wifi } from "lucide-react";
import type { DashboardMetric } from "../types/classroom";

type SummaryCardProps = {
  metric: DashboardMetric;
};

const metricIcon = {
  devices: Monitor,
  online: Wifi,
  inUse: MousePointer2,
  attention: TriangleAlert
};

export function SummaryCard({ metric }: SummaryCardProps) {
  const Icon = metricIcon[metric.id as keyof typeof metricIcon] ?? Activity;

  return (
    <article className={`summary-card summary-card--${metric.tone}`}>
      <div className="summary-card__topline">
        <div className="summary-card__label">{metric.label}</div>
        <span className="summary-card__icon" aria-hidden="true">
          <Icon size={18} />
        </span>
      </div>
      <div>
        <div className="summary-card__value">{metric.value}</div>
        <div className="summary-card__detail">{metric.detail}</div>
      </div>
    </article>
  );
}
