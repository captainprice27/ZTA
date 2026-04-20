# AI Service Calibration

## Purpose
This service combines three signals:

1. network anomaly score from the IDS-derived Isolation Forest
2. path/method/time risk heuristics
3. optional behavioral biometric reconstruction error

The final score is:

```text
final = network_score + path_score
```

If behavior features are present:

```text
final = (NETWORK_WEIGHT * network_score) + (BEHAVIOR_WEIGHT * behavior_score) + path_score
```

## Runtime knobs
The service reads these environment variables:

- `NETWORK_WEIGHT` default `0.7`
- `BEHAVIOR_WEIGHT` default `0.3`
- `ANOMALY_THRESHOLD` default `0.6`
- `ALLOW_DEGRADED_SCORING` default `true`

## Practical calibration workflow

1. Start the AI service and collect `/score` outputs for a normal traffic set.
2. Collect outputs for clearly malicious or suspicious traffic.
3. Compare false positives vs false negatives.
4. Adjust:
   - `ANOMALY_THRESHOLD` upward if too many normal flows are blocked.
   - `ANOMALY_THRESHOLD` downward if attack-like flows pass.
   - `BEHAVIOR_WEIGHT` upward only when behavior features are consistently present and reliable.
   - `NETWORK_WEIGHT` upward when behavior signals are absent or incomplete.
5. Re-run the same payload set and record the new decision boundary.

## Recommended first-pass values

- network-only mode: `NETWORK_WEIGHT=0.8`, `BEHAVIOR_WEIGHT=0.2`, `ANOMALY_THRESHOLD=0.65`
- mixed mode with reliable keystroke features: `NETWORK_WEIGHT=0.65`, `BEHAVIOR_WEIGHT=0.35`, `ANOMALY_THRESHOLD=0.6`

## Degraded mode
If the network model or metadata cannot load:

- health reports `degraded_mode: true`
- `/score` includes `service-degraded-mode` in `reasons`
- if `ALLOW_DEGRADED_SCORING=false`, the service fails closed with a maximum network score

## Smoke test

```powershell
cd e:\SAVED_CODES_AND_PROJECT\code-playing\ZTA\src\ai-service
python .\smoke_test.py
```
