import { useMemo } from "react";

export default function RiskGauge({ value = 0 }) {
  const pct = Math.min(100, Math.max(0, value));
  const radius = 70;
  const cx = 90, cy = 90;
  const startAngle = Math.PI;
  const endAngle = 0;
  const totalArc = Math.PI;
  const circumference = totalArc * radius;

  const dashOffset = circumference * (1 - pct / 100);

  const color = useMemo(() => {
    if (pct < 30) return "var(--success)";
    if (pct < 60) return "#eab308";
    if (pct < 80) return "#f97316";
    return "var(--danger)";
  }, [pct]);

  const x1 = cx + radius * Math.cos(startAngle);
  const y1 = cy + radius * Math.sin(startAngle);
  const x2 = cx + radius * Math.cos(endAngle);
  const y2 = cy + radius * Math.sin(endAngle);

  const arcPath = `M ${x1} ${y1} A ${radius} ${radius} 0 0 1 ${x2} ${y2}`;

  return (
    <div className="gauge-container">
      <svg className="gauge-svg" viewBox="0 0 180 100">
        <path d={arcPath} className="gauge-bg" />
        <path
          d={arcPath}
          className="gauge-fill"
          style={{
            stroke: color,
            strokeDasharray: circumference,
            strokeDashoffset: dashOffset,
          }}
        />
      </svg>
      <div className="gauge-value" style={{ color }}>{pct}%</div>
      <div className="gauge-label">Current Threat Level</div>
    </div>
  );
}
