import { useEffect, useMemo, useState } from "react";
import { getEvents, killSwitch } from "./api";

function formatTs(value) {
  return new Date(value).toLocaleTimeString();
}

export default function App() {
  const [events, setEvents] = useState([]);
  const [sourceIp, setSourceIp] = useState("203.0.113.10");
  const [status, setStatus] = useState("System online");

  async function refresh() {
    try {
      const data = await getEvents();
      setEvents(data);
    } catch {
      setStatus("Gateway unreachable");
    }
  }

  useEffect(() => {
    refresh();
    const timer = setInterval(refresh, 3000);
    return () => clearInterval(timer);
  }, []);

  const risk = useMemo(() => {
    if (events.length === 0) return 0;
    return Math.round((events[0].riskScore ?? 0) * 100);
  }, [events]);

  async function triggerKillSwitch() {
    try {
      const payload = {
        userId: "operator",
        sourceIp,
        reason: "Manual block from dashboard"
      };
      await killSwitch(payload);
      setStatus(`Blocked ${sourceIp}`);
      await refresh();
    } catch {
      setStatus("Kill-switch failed");
    }
  }

  return (
    <main className="page">
      <section className="hero">
        <h1>ZTA War Room</h1>
        <p>Continuous trust verification across policy and behavioral risk.</p>
      </section>

      <section className="panels">
        <article className="panel">
          <h2>Risk Meter</h2>
          <div className="meter">
            <div className="meterValue" style={{ width: `${risk}%` }} />
          </div>
          <strong>{risk}%</strong>
          <p>{status}</p>
        </article>

        <article className="panel">
          <h2>Manual Kill-Switch</h2>
          <label htmlFor="sourceIp">Source IP</label>
          <input
            id="sourceIp"
            value={sourceIp}
            onChange={(e) => setSourceIp(e.target.value)}
            placeholder="198.51.100.21"
          />
          <button className="danger" onClick={triggerKillSwitch}>
            BLOCK NOW
          </button>
        </article>
      </section>

      <section className="panel logPanel">
        <h2>Traffic Stream</h2>
        <ul className="log">
          {events.map((event, index) => (
            <li key={`${event.createdAt}-${index}`} className={event.allowed ? "allow" : "deny"}>
              <span>{formatTs(event.createdAt)}</span>
              <span>{event.path}</span>
              <span>{Math.round((event.riskScore ?? 0) * 100)}%</span>
              <span>{event.allowed ? "ALLOWED" : "BLOCKED"}</span>
            </li>
          ))}
          {events.length === 0 && <li className="empty">No traffic yet</li>}
        </ul>
      </section>
    </main>
  );
}
