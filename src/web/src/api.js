const gatewayBase = import.meta.env.VITE_GATEWAY_URL ?? "";

async function request(path, init) {
  const response = await fetch(`${gatewayBase}${path}`, {
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {})
    },
    ...init
  });

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `Request failed: ${response.status}`);
  }

  return response.json();
}

export function getEvents() {
  return request("/api/events");
}

export function killSwitch(payload) {
  return request("/api/kill-switch", {
    method: "POST",
    body: JSON.stringify(payload)
  });
}
