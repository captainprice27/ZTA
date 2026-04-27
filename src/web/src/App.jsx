import { useCallback, useEffect, useMemo, useState } from "react";
import { getEvents, getAnalytics, killSwitch } from "./api";
import ThemeToggle from "./components/ThemeToggle";
import RiskGauge from "./components/RiskGauge";
import LiveFeed from "./components/LiveFeed";
import AnalyticsPanel from "./components/AnalyticsPanel";

function formatTs(value) {
  if (!value) return "--:--";
  try {
    return new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  } catch { return "--:--"; }
}

export default function App() {
  const [events, setEvents] = useState([]);
  const [analytics, setAnalytics] = useState([]);
  const [sourceIp, setSourceIp] = useState("203.0.113.10");
  const [status, setStatus] = useState("Connecting...");
  const [toasts, setToasts] = useState([]);

  // Initial data load
  async function refresh() {
    try {
      const data = await getEvents();
      setEvents(data);
      setStatus("Online");
    } catch {
      setStatus("Offline");
    }
  }

  async function refreshAnalytics() {
    try {
      const data = await getAnalytics(48);
      setAnalytics(data);
    } catch { /* silent */ }
  }

  useEffect(() => {
    refresh();
    refreshAnalytics();
    const t1 = setInterval(refresh, 5000);
    const t2 = setInterval(refreshAnalytics, 30000);
    return () => { clearInterval(t1); clearInterval(t2); };
  }, []);

  // SSE event handler
  const handleSseEvent = useCallback((event) => {
    setEvents(prev => {
      const exists = prev.some(e => e.createdAtUnixMs === event.createdAtUnixMs);
      if (exists) return prev;
      return [event, ...prev].slice(0, 100);
    });
    setStatus("Online");

    // Toaster notification for anomalies
    if (!event.allowed) {
      const id = Date.now() + Math.random();
      setToasts(prev => [...prev, {
        id,
        type: "block",
        message: `🛡 BLOCKED ${event.sourceIp} → ${event.path} (${Math.round((event.riskScore ?? 0) * 100)}%)`
      }]);
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 5000);
    }
  }, []);

  const risk = useMemo(() => {
    if (events.length === 0) return 0;
    return Math.round((events[0].riskScore ?? 0) * 100);
  }, [events]);

  const stats = useMemo(() => {
    const total = events.length;
    const blocked = events.filter(e => !e.allowed).length;
    const allowed = total - blocked;
    return { total, blocked, allowed };
  }, [events]);

  async function triggerKillSwitch() {
    try {
      await killSwitch({
        userId: "operator",
        sourceIp,
        reason: "Manual block from dashboard"
      });
      setStatus(`Blocked ${sourceIp}`);
      await refresh();
    } catch {
      setStatus("Kill-switch failed");
    }
  }

  return (
    <main className="app">
      {/* Toaster */}
      <div className="toaster-container">
        {toasts.map(t => (
          <div key={t.id} className={`toast toast-${t.type}`}>{t.message}</div>
        ))}
      </div>

      {/* Header */}
      <header className="header">
        <div className="header-left">
          <div className="header-logo">ZT</div>
          <div>
            <div className="header-title">ZTA War Room</div>
            <div className="header-subtitle">Zero-Trust Continuous Verification Engine</div>
          </div>
        </div>
        <div className="header-right">
          <LiveFeed onEvent={handleSseEvent} />
          <div className={`status-dot ${status === "Online" ? "" : "offline"}`} />
          <span className="status-label">{status}</span>
          <ThemeToggle />
        </div>
      </header>

      {/* Dashboard Grid */}
      <div className="dashboard-grid">
        {/* Risk Gauge */}
        <div className="card">
          <div className="card-header">
            <span className="card-title">Threat Level</span>
            <span className="card-badge badge-live">LIVE</span>
          </div>
          <RiskGauge value={risk} />
        </div>

        {/* Stats */}
        <div className="card">
          <div className="card-header">
            <span className="card-title">Session Stats</span>
          </div>
          <div style={{ display: "grid", gap: ".8rem" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontSize: ".82rem", color: "var(--ink-muted)" }}>Total Events</span>
              <span style={{ fontSize: "1.4rem", fontWeight: 700, fontFamily: "'JetBrains Mono', monospace" }}>{stats.total}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontSize: ".82rem", color: "var(--success)" }}>Allowed</span>
              <span style={{ fontSize: "1.2rem", fontWeight: 700, fontFamily: "'JetBrains Mono', monospace", color: "var(--success)" }}>{stats.allowed}</span>
            </div>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontSize: ".82rem", color: "var(--danger)" }}>Blocked</span>
              <span style={{ fontSize: "1.2rem", fontWeight: 700, fontFamily: "'JetBrains Mono', monospace", color: "var(--danger)" }}>{stats.blocked}</span>
            </div>
            <div style={{
              marginTop: ".4rem", height: "6px", borderRadius: "3px",
              background: "var(--line)", overflow: "hidden"
            }}>
              <div style={{
                height: "100%", borderRadius: "3px",
                background: "var(--danger)",
                width: stats.total > 0 ? `${(stats.blocked / stats.total) * 100}%` : "0%",
                transition: "width .5s ease"
              }} />
            </div>
            <div style={{ fontSize: ".72rem", color: "var(--ink-subtle)", textAlign: "center" }}>
              Block Rate: {stats.total > 0 ? Math.round((stats.blocked / stats.total) * 100) : 0}%
            </div>
          </div>
        </div>

        {/* Kill Switch */}
        <div className="card">
          <div className="card-header">
            <span className="card-title">Manual Kill-Switch</span>
          </div>
          <label style={{ fontSize: ".78rem", color: "var(--ink-muted)", marginBottom: ".3rem", display: "block" }}>
            Target Source IP
          </label>
          <input
            className="kill-input"
            id="sourceIp"
            value={sourceIp}
            onChange={(e) => setSourceIp(e.target.value)}
            placeholder="198.51.100.21"
          />
          <button className="kill-btn" onClick={triggerKillSwitch}>
            ⚡ Block Now
          </button>
          <div className="kill-status">{status}</div>
        </div>

        {/* Analytics */}
        <div className="card analytics-panel">
          <div className="card-header">
            <span className="card-title">Hourly Analytics</span>
            <span className="card-badge badge-live">48H</span>
          </div>
          <AnalyticsPanel data={analytics} />
        </div>

        {/* Event Log */}
        <div className="card log-panel">
          <div className="card-header">
            <span className="card-title">Traffic Stream</span>
            <span className="card-badge badge-live">REAL-TIME</span>
          </div>
          <div className="log-scroll">
            {events.length === 0 && <div className="log-empty">No traffic recorded yet</div>}
            {events.map((event, index) => (
              <div key={`${event.createdAtUnixMs}-${index}`} className={`log-row ${event.allowed ? "allow" : "deny"}`}>
                <span style={{ color: "var(--ink-subtle)", fontSize: ".72rem" }}>
                  {formatTs(event.createdAt)}
                </span>
                <span style={{ fontWeight: 500 }}>{event.path}</span>
                <span className="log-source">{event.sourceIp}</span>
                <span className="log-risk">{Math.round((event.riskScore ?? 0) * 100)}%</span>
                <span className="log-status">
                  {event.allowed ? "✓ ALLOW" : "✕ BLOCK"}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </main>
  );
}
