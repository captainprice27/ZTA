# THEORY: Zero Trust + Behavioral AI Gateway

## 1. Problem Statement

Traditional perimeter security assumes that once a request is authenticated, it is mostly trusted inside the network boundary. This model fails under:
- stolen sessions/tokens,
- credential stuffing,
- low-and-slow exfiltration,
- internal lateral movement.

The project addresses this by applying Zero Trust principles at request time:
1. Policy trust: "Is this identity allowed on this route/method?"
2. Behavioral trust: "Is this behavior statistically consistent with expected usage?"

Access is treated as continuously re-evaluated, not permanently granted.

## 2. Research Motivation

The system combines:
- deterministic authorization (policy rules),
- anomaly detection (network behavior),
- biometric-style behavioral profiling (keystroke timing model),
- operational response (kill-switch and future NSG-level blocking).

This hybrid is motivated by a practical observation: policy alone cannot detect abuse by valid credentials, while anomaly-only systems lack deterministic access boundaries.

## 3. Core Hypothesis

A request-level decision engine that fuses policy + anomaly signals can reduce false trust and improve early attack detection without blocking all unknown behavior.

Formally, for each request `r`:
- policy decision `P(r)` in `{allow, deny}`,
- network risk `N(r)` in `[0,1]`,
- optional behavioral risk `B(r)` in `[0,1]`,
- context risk `C(r)` in `[0,1]` (path/method/off-hours heuristics).

Current scoring design:
- if behavioral input missing: `R = min(1, N + C)`
- if behavioral input present: `R = min(1, wN*N + wB*B + C)`

Decision:
- allow iff `P(r)=allow` and `R < threshold`.

## 4. Data/Model Theory

### 4.1 Network Anomaly Model

Dataset basis: CIC-IDS2018 subset (sampled in training workspace).  
Model: Isolation Forest (unsupervised anomaly model).

Why Isolation Forest:
- robust baseline for outlier detection,
- works without full attack label dependency,
- computationally practical for PoC.

Model output is transformed into risk score via sigmoid-like mapping from model scores.

### 4.2 Behavioral Model

Dataset basis: DSL-StrongPasswordData (keystroke timing).  
Model strategy: per-subject reconstruction model (autoencoder-style via MLPRegressor).

Reasoning:
- behavioral identity is user-specific,
- reconstruction error naturally supports "normal pattern vs unusual pattern" thresholding.

Each subject gets:
- scaler,
- reconstruction model,
- subject-specific threshold.

## 5. What Issues This Solves

1. Compromised valid credentials:
- policy may allow route, but behavior/network anomaly raises risk.

2. Burst abuse from valid account:
- rate + payload + latency signals increase network risk.

3. Manual incident response:
- kill-switch allows immediate block workflow from dashboard/API.

4. Continuous risk awareness:
- event stream and risk score expose operational state.

## 6. Current Practical Constraints (Known)

1. Feature mismatch:
- runtime gateway has fewer coarse features than original IDS training space.
- mapping is best-effort, not full flow feature parity.

2. Behavioral payload availability:
- behavior scoring only meaningful when behavior features are provided.

3. Cloud blocking not fully enforced yet:
- Azure blocker currently logs intent (stub), not full NSG mutation in production path.

4. Dataset sampling tradeoff:
- sampled training improves laptop feasibility but may reduce tail-attack representation.

## 7. Security/Operational Risks

1. False positives:
- anomaly systems can overreact during unusual but legitimate spikes.

2. False negatives:
- attacker behavior can mimic baseline under low-and-slow attack.

3. Threshold sensitivity:
- global threshold may not be optimal per route/user.

Mitigation approach:
- maintain auditable event logs,
- separate policy deny vs anomaly deny reasons,
- calibrate thresholds using replayed traffic.

## 8. Evaluation Lens

For research-quality progression, evaluate:
- policy accuracy (authorized/unauthorized),
- anomaly separation (AUC, precision/recall on synthetic attack traffic),
- operational metrics (decision latency, cache hit rate),
- stability metrics (false block rate over benign sessions).

## 9. What We Are Solving Next

Near-term:
1. Better behavioral input capture from client telemetry.
2. Route-aware thresholding and weighting.
3. Full Azure NSG action path with credentials and rollback strategy.
4. Stronger test harness (normal vs attack scenarios scripted).

Mid-term:
1. Add supervised network model track and ensemble with IF.
2. Per-user adaptive baselines and drift monitoring.
3. Explainability payloads for SOC/operator tooling.

## 10. Research Summary

This project is currently a functional Zero-Trust PoC with request-time policy enforcement, anomaly-aware scoring, and model-backed behavioral hooks.  
It demonstrates an implementable bridge between access control and applied cyber anomaly detection, with clear upgrade paths toward production-grade controls.
