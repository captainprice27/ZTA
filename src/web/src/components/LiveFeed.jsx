import { useEffect, useRef, useState } from "react";

const gatewayBase = import.meta.env.VITE_GATEWAY_URL ?? "";

export default function LiveFeed({ onEvent }) {
  const [connected, setConnected] = useState(false);
  const retryRef = useRef(null);

  useEffect(() => {
    let es;
    function connect() {
      try {
        es = new EventSource(`${gatewayBase}/api/events/stream`);
        es.onopen = () => setConnected(true);
        es.onmessage = (msg) => {
          try {
            const event = JSON.parse(msg.data);
            onEvent?.(event);
          } catch { /* ignore malformed */ }
        };
        es.onerror = () => {
          setConnected(false);
          es.close();
          retryRef.current = setTimeout(connect, 3000);
        };
      } catch {
        setConnected(false);
        retryRef.current = setTimeout(connect, 3000);
      }
    }

    connect();
    return () => {
      es?.close();
      clearTimeout(retryRef.current);
    };
  }, [onEvent]);

  return (
    <div style={{ display: "flex", alignItems: "center", gap: ".4rem" }}>
      <div className={`status-dot ${connected ? "" : "offline"}`} />
      <span className="status-label">
        {connected ? "SSE Connected" : "Reconnecting..."}
      </span>
    </div>
  );
}
