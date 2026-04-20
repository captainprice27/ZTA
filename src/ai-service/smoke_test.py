import json
import sys
from urllib import request


SAMPLE_PAYLOAD = {
    "userId": "prayas",
    "sourceIp": "203.0.113.10",
    "path": "/api/data",
    "method": "GET",
    "requestsPerMinute": 8,
    "payloadBytes": 4096,
    "hourOfDay": 14,
    "requestLatencyMs": 120,
    "behaviorFeatures": {
        "H.period": 0.12,
        "DD.period.t": 0.33,
        "UD.period.t": 0.21,
        "H.t": 0.08,
        "DD.t.i": 0.13,
        "UD.t.i": 0.05,
        "H.i": 0.09,
        "DD.i.e": 0.17,
        "UD.i.e": 0.08,
        "H.e": 0.10,
        "DD.e.five": 1.10,
        "UD.e.five": 1.00,
        "H.five": 0.09,
        "DD.five.Shift.r": 0.90,
        "UD.five.Shift.r": 0.80,
        "H.Shift.r": 0.12,
        "DD.Shift.r.o": 0.70,
        "UD.Shift.r.o": 0.60,
        "H.o": 0.10,
        "DD.o.a": 0.20,
        "UD.o.a": 0.10,
        "H.a": 0.12,
        "DD.a.n": 0.20,
        "UD.a.n": 0.08,
        "H.n": 0.11,
        "DD.n.l": 0.26,
        "UD.n.l": 0.15,
        "H.l": 0.10,
        "DD.l.Return": 0.28,
        "UD.l.Return": 0.18,
        "H.Return": 0.09,
    },
}


def main() -> int:
    url = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8000/score"
    body = json.dumps(SAMPLE_PAYLOAD).encode("utf-8")
    req = request.Request(url, data=body, headers={"Content-Type": "application/json"}, method="POST")
    with request.urlopen(req, timeout=10) as resp:
        payload = json.loads(resp.read().decode("utf-8"))
    print(json.dumps(payload, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
