import { useEffect, useRef } from "react";

export default function AnalyticsPanel({ data = [] }) {
  const canvasRef = useRef(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || data.length === 0) return;

    const ctx = canvas.getContext("2d");
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);
    const W = rect.width;
    const H = rect.height;

    ctx.clearRect(0, 0, W, H);

    const padL = 45, padR = 15, padT = 20, padB = 35;
    const chartW = W - padL - padR;
    const chartH = H - padT - padB;
    const n = data.length;
    if (n === 0) return;

    // Extract values
    const risks = data.map(d => d.avgRiskScore ?? 0);
    const blocked = data.map(d => d.blockedCount ?? 0);
    const allowed = data.map(d => d.allowedCount ?? 0);
    const totals = data.map(d => d.totalEvents ?? 0);

    const maxEvents = Math.max(...totals, 1);
    const barW = Math.max(4, (chartW / n) * 0.65);
    const gap = chartW / n;

    const styles = getComputedStyle(document.documentElement);
    const accentColor = styles.getPropertyValue("--accent").trim() || "#6b8f4e";
    const successColor = styles.getPropertyValue("--success").trim() || "#22c55e";
    const dangerColor = styles.getPropertyValue("--danger").trim() || "#dc2626";
    const lineColor = styles.getPropertyValue("--line").trim() || "#c8d6b8";
    const inkMuted = styles.getPropertyValue("--ink-muted").trim() || "#888";

    // Gridlines
    ctx.strokeStyle = lineColor;
    ctx.lineWidth = 0.5;
    for (let i = 0; i <= 4; i++) {
      const y = padT + (chartH / 4) * i;
      ctx.beginPath();
      ctx.moveTo(padL, y);
      ctx.lineTo(padL + chartW, y);
      ctx.stroke();
    }

    // Bars (blocked + allowed stacked)
    for (let i = 0; i < n; i++) {
      const x = padL + gap * i + (gap - barW) / 2;
      const totalH = (totals[i] / maxEvents) * chartH;
      const blockedH = (blocked[i] / maxEvents) * chartH;
      const allowedH = totalH - blockedH;

      // Allowed (bottom)
      ctx.fillStyle = successColor + "60";
      ctx.beginPath();
      ctx.roundRect(x, padT + chartH - totalH, barW, allowedH, [3, 3, 0, 0]);
      ctx.fill();

      // Blocked (top of stack)
      ctx.fillStyle = dangerColor + "80";
      ctx.beginPath();
      ctx.roundRect(x, padT + chartH - blockedH, barW, blockedH, [3, 3, 0, 0]);
      ctx.fill();
    }

    // Risk line
    ctx.strokeStyle = accentColor;
    ctx.lineWidth = 2.5;
    ctx.lineJoin = "round";
    ctx.beginPath();
    for (let i = 0; i < n; i++) {
      const x = padL + gap * i + gap / 2;
      const y = padT + chartH - (risks[i] * chartH);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Risk dots
    for (let i = 0; i < n; i++) {
      const x = padL + gap * i + gap / 2;
      const y = padT + chartH - (risks[i] * chartH);
      ctx.fillStyle = accentColor;
      ctx.beginPath();
      ctx.arc(x, y, 3, 0, Math.PI * 2);
      ctx.fill();
    }

    // X-axis labels
    ctx.fillStyle = inkMuted;
    ctx.font = "10px Inter, sans-serif";
    ctx.textAlign = "center";
    const labelStep = Math.max(1, Math.floor(n / 8));
    for (let i = 0; i < n; i += labelStep) {
      const x = padL + gap * i + gap / 2;
      const label = data[i].hour?.split(" ")[1] || "";
      ctx.fillText(label, x, H - 8);
    }

    // Y-axis labels  
    ctx.textAlign = "right";
    for (let i = 0; i <= 4; i++) {
      const y = padT + (chartH / 4) * i;
      const val = Math.round(maxEvents * (1 - i / 4));
      ctx.fillText(val.toString(), padL - 8, y + 4);
    }
  }, [data]);

  if (data.length === 0) {
    return <div className="chart-empty">Collecting analytics data...</div>;
  }

  return (
    <>
      <div className="chart-container">
        <canvas ref={canvasRef} />
      </div>
      <div className="chart-legend">
        <div className="chart-legend-item">
          <div className="chart-legend-dot" style={{ background: "var(--accent)" }} />
          <span>Avg Risk Score</span>
        </div>
        <div className="chart-legend-item">
          <div className="chart-legend-dot" style={{ background: "var(--success)", opacity: .6 }} />
          <span>Allowed</span>
        </div>
        <div className="chart-legend-item">
          <div className="chart-legend-dot" style={{ background: "var(--danger)", opacity: .7 }} />
          <span>Blocked</span>
        </div>
      </div>
    </>
  );
}
