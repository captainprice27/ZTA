import json
import os
from threading import RLock
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
from fastapi import FastAPI
from pydantic import AliasChoices, BaseModel, ConfigDict, Field, field_validator


APP_ROOT = Path(__file__).resolve().parents[1]
MODELS_ROOT = APP_ROOT / "models"
NETWORK_MODEL_PATH = MODELS_ROOT / "network" / "network_iforest.joblib"
NETWORK_META_PATH = MODELS_ROOT / "network" / "network_iforest_meta.json"
BEHAVIOR_DIR = MODELS_ROOT / "behavior"
USER_SUBJECT_MAP_PATH = BEHAVIOR_DIR / "user_subject_map.json"

NETWORK_WEIGHT = float(os.getenv("NETWORK_WEIGHT", "0.7"))
BEHAVIOR_WEIGHT = float(os.getenv("BEHAVIOR_WEIGHT", "0.3"))
_anomaly_threshold = float(os.getenv("ANOMALY_THRESHOLD", "0.6"))
ALLOW_DEGRADED_SCORING = os.getenv("ALLOW_DEGRADED_SCORING", "true").lower() == "true"

if NETWORK_WEIGHT < 0 or BEHAVIOR_WEIGHT < 0:
    raise ValueError("NETWORK_WEIGHT and BEHAVIOR_WEIGHT must be non-negative")
if _anomaly_threshold < 0 or _anomaly_threshold > 1:
    raise ValueError("ANOMALY_THRESHOLD must be between 0 and 1")


def get_anomaly_threshold() -> float:
    return _anomaly_threshold


def set_anomaly_threshold(value: float) -> None:
    global _anomaly_threshold
    if value < 0 or value > 1:
        raise ValueError("ANOMALY_THRESHOLD must be between 0 and 1")
    _anomaly_threshold = value


class ScoreRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    user_id: str = Field(validation_alias=AliasChoices("user_id", "userId", "UserId"))
    source_ip: str = Field(validation_alias=AliasChoices("source_ip", "sourceIp", "SourceIp"))
    path: str
    method: str
    requests_per_minute: int = Field(
        default=0,
        ge=0,
        validation_alias=AliasChoices("requests_per_minute", "requestsPerMinute", "RequestsPerMinute"),
    )
    payload_bytes: int = Field(
        default=0,
        ge=0,
        validation_alias=AliasChoices("payload_bytes", "payloadBytes", "PayloadBytes"),
    )
    hour_of_day: int = Field(
        default=0,
        ge=0,
        le=23,
        validation_alias=AliasChoices("hour_of_day", "hourOfDay", "HourOfDay"),
    )
    request_latency_ms: float = Field(
        default=0,
        ge=0,
        validation_alias=AliasChoices("request_latency_ms", "requestLatencyMs", "RequestLatencyMs"),
    )
    behavior_subject: str | None = Field(
        default=None,
        validation_alias=AliasChoices("behavior_subject", "behaviorSubject", "BehaviorSubject"),
    )
    behavior_features: dict[str, float] | None = Field(
        default=None,
        validation_alias=AliasChoices("behavior_features", "behaviorFeatures", "BehaviorFeatures"),
    )

    @field_validator("path")
    @classmethod
    def validate_path(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized.startswith("/"):
            raise ValueError("path must start with '/'")
        return normalized

    @field_validator("method")
    @classmethod
    def normalize_method(cls, value: str) -> str:
        normalized = value.strip().upper()
        if normalized not in {"GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"}:
            raise ValueError("unsupported HTTP method")
        return normalized


class ScoreResponse(BaseModel):
    anomaly_score: float
    is_anomaly: bool
    reasons: list[str]
    degraded_mode: bool = False
    model_status: dict[str, Any] = Field(default_factory=dict)
    feature_contributions: dict[str, float] = Field(default_factory=dict)


class FeedbackRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)
    anomaly_threshold: float | None = Field(
        default=None,
        ge=0.0,
        le=1.0,
        validation_alias=AliasChoices("anomaly_threshold", "anomalyThreshold"),
    )


class ModelState:
    def __init__(self) -> None:
        self._lock = RLock()
        self.network_model: Any | None = None
        self.network_meta: dict[str, Any] = {}
        self.behavior_models: dict[str, dict[str, Any]] = {}
        self.user_subject_map: dict[str, str] = {}
        self.load_errors: list[str] = []

    def load(self) -> None:
        load_errors: list[str] = []
        network_model: Any | None = None
        network_meta: dict[str, Any] = {}
        behavior_models: dict[str, dict[str, Any]] = {}
        user_subject_map: dict[str, str] = {}

        if NETWORK_MODEL_PATH.exists() and NETWORK_META_PATH.exists():
            try:
                network_model = joblib.load(NETWORK_MODEL_PATH)
                with open(NETWORK_META_PATH, "r", encoding="utf-8") as f:
                    network_meta = json.load(f)
            except Exception as ex:
                load_errors.append(f"network-model-load-failed:{type(ex).__name__}")
        else:
            load_errors.append("network-model-files-missing")

        if USER_SUBJECT_MAP_PATH.exists():
            with open(USER_SUBJECT_MAP_PATH, "r", encoding="utf-8") as f:
                user_subject_map = json.load(f)
        else:
            load_errors.append("behavior-user-map-missing")

        if BEHAVIOR_DIR.exists():
            for p in BEHAVIOR_DIR.glob("behavior_*.joblib"):
                try:
                    bundle = joblib.load(p)
                    subject = str(bundle.get("subject", "")).strip()
                    if subject:
                        behavior_models[subject] = bundle
                except Exception as ex:
                    load_errors.append(f"behavior-model-load-failed:{p.name}:{type(ex).__name__}")
                    continue
        else:
            load_errors.append("behavior-directory-missing")

        with self._lock:
            self.load_errors = load_errors
            self.network_model = network_model
            self.network_meta = network_meta
            self.behavior_models = behavior_models
            self.user_subject_map = user_subject_map

    def snapshot(self) -> dict[str, Any]:
        with self._lock:
            return {
                "network_model": self.network_model,
                "network_meta": dict(self.network_meta),
                "behavior_models": dict(self.behavior_models),
                "user_subject_map": dict(self.user_subject_map),
                "load_errors": list(self.load_errors),
            }

    @property
    def degraded(self) -> bool:
        snapshot = self.snapshot()
        if not ALLOW_DEGRADED_SCORING:
            return bool(snapshot["load_errors"])
        return snapshot["network_model"] is None

    def status(self) -> dict[str, Any]:
        snapshot = self.snapshot()
        return {
            "network_model_loaded": snapshot["network_model"] is not None,
            "behavior_models_loaded": len(snapshot["behavior_models"]),
            "user_subject_map_loaded": bool(snapshot["user_subject_map"]),
            "degraded_mode": self.degraded,
            "load_errors": snapshot["load_errors"],
            "weights": {
                "network_weight": NETWORK_WEIGHT,
                "behavior_weight": BEHAVIOR_WEIGHT,
                "anomaly_threshold": get_anomaly_threshold(),
            },
        }


state = ModelState()
state.load()
app = FastAPI(title="ZTA AI Service", version="0.3.0")


def _safe_float(v: Any) -> float:
    try:
        return float(v)
    except Exception:
        return 0.0


def _ip_entropy(source_ip: str) -> float:
    octets = [segment for segment in source_ip.split(".") if segment.isdigit()]
    if len(octets) != 4:
        return 0.0
    nums = [int(part) for part in octets]
    return float(np.std(nums))


def _path_depth(path: str) -> int:
    return max(0, len([part for part in path.split("/") if part]))


def _path_risk(path: str, method: str, hour_of_day: int) -> tuple[float, list[str]]:
    score = 0.0
    reasons: list[str] = []
    low_path = path.lower()
    method_upper = method.upper()

    if low_path.startswith("/api/admin"):
        score += 0.15
        reasons.append("admin-path-access")
    if method_upper in {"PUT", "DELETE", "PATCH"}:
        score += 0.1
        reasons.append("state-changing-method")
    if hour_of_day < 6:
        score += 0.05
        reasons.append("off-hours-request")
    if "auth" in low_path and method_upper == "POST":
        score += 0.05
        reasons.append("auth-surface")

    return min(score, 0.4), reasons


def _feature_candidates(req: ScoreRequest) -> dict[str, float]:
    requests = max(req.requests_per_minute, 1)
    payload = max(req.payload_bytes, 0)
    latency_ms = max(req.request_latency_ms, 0.0)
    path_depth = _path_depth(req.path)
    path_length = len(req.path)
    ip_entropy = _ip_entropy(req.source_ip)
    bytes_per_request = payload / requests
    burst_factor = requests * max(bytes_per_request, 1.0)

    return {
        "flow byts/s": float(payload),
        "flow pkts/s": float(requests),
        "totlen fwd pkts": float(payload),
        "tot fwd pkts": float(requests),
        "flow duration": float(latency_ms * 1000.0),
        "pkt len mean": float(bytes_per_request),
        "pkt len max": float(min(payload, 65535)),
        "pkt len min": float(0 if payload == 0 else min(bytes_per_request, 1500)),
        "init fwd win byts": float(min(payload, 65535)),
        "subflow fwd byts": float(payload),
        "subflow fwd pkts": float(requests),
        "flow iat mean": float(latency_ms),
        "fwd iat mean": float(latency_ms),
        "active mean": float(latency_ms),
        "idle mean": float(max(0.0, (60000.0 / requests) - latency_ms)),
        "down/up ratio": float(payload / max(requests, 1)),
        "avg pkt size": float(bytes_per_request),
        "fwd header len": float(path_length * 4),
        "bwd header len": float(path_depth * 8),
        "protocol": float({"GET": 6, "POST": 17, "PUT": 17, "DELETE": 17, "PATCH": 17}.get(req.method.upper(), 0)),
        "min seg size fwd": float(max(20, min(path_length * 4, 1500))),
        "packet length mean": float(bytes_per_request),
        "act_data_pkt_fwd": float(path_depth),
        "flow packets/s": float(requests),
        "path depth surrogate": float(path_depth),
        "request burst surrogate": float(burst_factor),
        "source ip entropy": float(ip_entropy),
    }


def _network_score(req: ScoreRequest) -> tuple[float, list[str], dict[str, float]]:
    reasons: list[str] = []
    contributions: dict[str, float] = {}
    snapshot = state.snapshot()
    status = state.status()
    network_model = snapshot["network_model"]
    network_meta = snapshot["network_meta"]

    if network_model is None or not network_meta:
        reasons.extend(["network-model-missing"])
        reasons.extend(status["load_errors"])
        if not ALLOW_DEGRADED_SCORING:
            return 1.0, reasons, contributions
        return 0.5, reasons, contributions

    feature_cols = network_meta.get("feature_columns", [])
    medians = network_meta.get("feature_medians", {})
    if not feature_cols:
        return 0.5, ["network-feature-metadata-missing"], contributions

    vector = [_safe_float(medians.get(c, 0.0)) for c in feature_cols]
    idx = {name.lower(): i for i, name in enumerate(feature_cols)}
    candidates = _feature_candidates(req)

    mapped = 0
    for column_name, value in candidates.items():
        feature_index = idx.get(column_name)
        if feature_index is not None:
            vector[feature_index] = float(value)
            mapped += 1

    x = pd.DataFrame([vector], columns=feature_cols, dtype=np.float64)
    score_samples = float(network_model.score_samples(x)[0])
    network_score = 1.0 / (1.0 + np.exp(score_samples))

    # --- XAI: per-feature anomaly contributions ---
    readable_names = {
        "flow pkts/s": "requests_per_minute",
        "totlen fwd pkts": "payload_bytes",
        "flow duration": "request_latency",
        "pkt len mean": "avg_packet_size",
        "flow iat mean": "inter_arrival_time",
        "idle mean": "idle_time",
        "source ip entropy": "source_ip_entropy",
        "path depth surrogate": "path_depth",
        "request burst surrogate": "burst_factor",
    }
    for col_name in feature_cols:
        col_lower = col_name.lower()
        median_val = _safe_float(medians.get(col_name, 0.0))
        actual_val = vector[idx[col_lower]] if col_lower in idx else median_val
        if median_val != 0:
            deviation = abs(actual_val - median_val) / (abs(median_val) + 1e-9)
        else:
            deviation = abs(actual_val)
        if deviation > 0.05:
            label = readable_names.get(col_lower, col_lower)
            contributions[label] = round(min(deviation, 5.0), 4)

    # Sort and keep top-8 most impactful features
    contributions = dict(sorted(contributions.items(), key=lambda kv: kv[1], reverse=True)[:8])

    if req.requests_per_minute > 120:
        reasons.append("frequency-spike")
    if req.payload_bytes > 300_000:
        reasons.append("payload-burst")
    if req.request_latency_ms > 2_000:
        reasons.append("latency-outlier")
    reasons.append(f"network-features-mapped:{mapped}/{len(feature_cols)}")
    if not reasons:
        reasons.append("network-model-evaluated")

    return float(min(max(network_score, 0.0), 1.0)), reasons, contributions


def _resolve_subject(req: ScoreRequest) -> str | None:
    if req.behavior_subject:
        return req.behavior_subject.strip()

    user_subject_map = state.snapshot()["user_subject_map"]
    if req.user_id in user_subject_map:
        return str(user_subject_map[req.user_id]).strip()

    user = req.user_id.strip().lower()
    if user.startswith("s") and len(user) == 4 and user[1:].isdigit():
        return user
    return None


def _behavior_score(req: ScoreRequest) -> tuple[float | None, list[str]]:
    subject = _resolve_subject(req)
    if not subject:
        return None, ["behavior-subject-unresolved"]

    bundle = state.snapshot()["behavior_models"].get(subject)
    if not bundle:
        return None, [f"behavior-model-not-found:{subject}"]

    features = req.behavior_features
    if not features:
        return None, ["behavior-features-missing"]

    feature_cols = bundle["feature_columns"]
    row = np.array([_safe_float(features.get(c, 0.0)) for c in feature_cols], dtype=np.float64).reshape(1, -1)
    scaler = bundle["scaler"]
    autoencoder = bundle["autoencoder"]
    threshold = float(bundle["threshold"])

    x = scaler.transform(row)
    pred = autoencoder.predict(x)
    recon_err = float(np.mean((x - pred) ** 2))
    score = min(1.0, recon_err / max(threshold, 1e-9))

    reasons = [f"behavior-subject:{subject}"]
    if score >= 1.0:
        reasons.append("behavior-threshold-exceeded")
    else:
        reasons.append("behavior-normal-range")
    return score, reasons


@app.get("/health")
def health() -> dict[str, Any]:
    return {
        "status": "ok",
        **state.status(),
    }


@app.get("/config")
def config() -> dict[str, Any]:
    return {
        "network_weight": NETWORK_WEIGHT,
        "behavior_weight": BEHAVIOR_WEIGHT,
        "anomaly_threshold": get_anomaly_threshold(),
        "allow_degraded_scoring": ALLOW_DEGRADED_SCORING,
    }


@app.post("/score", response_model=ScoreResponse)
def score(req: ScoreRequest) -> ScoreResponse:
    network_score, network_reasons, feature_contribs = _network_score(req)
    path_score, path_reasons = _path_risk(req.path, req.method, req.hour_of_day)
    behavior_score, behavior_reasons = _behavior_score(req)

    if behavior_score is None:
        final_score = min(1.0, network_score + path_score)
    else:
        final_score = min(1.0, NETWORK_WEIGHT * network_score + BEHAVIOR_WEIGHT * behavior_score + path_score)

    threshold = get_anomaly_threshold()
    is_anomaly = final_score >= threshold
    reasons = _dedupe_reasons(network_reasons + path_reasons + behavior_reasons)
    if state.degraded:
        reasons.append("service-degraded-mode")

    return ScoreResponse(
        anomaly_score=round(float(final_score), 6),
        is_anomaly=is_anomaly,
        reasons=reasons,
        degraded_mode=state.degraded,
        model_status=state.status(),
        feature_contributions=feature_contribs,
    )


@app.post("/debug/behavior-score", response_model=ScoreResponse)
def debug_behavior_score(req: ScoreRequest) -> ScoreResponse:
    behavior_score, behavior_reasons = _behavior_score(req)
    final_score = 0.0 if behavior_score is None else behavior_score
    return ScoreResponse(
        anomaly_score=round(float(final_score), 6),
        is_anomaly=final_score >= 1.0,
        reasons=_dedupe_reasons(behavior_reasons),
        degraded_mode=state.degraded,
        model_status=state.status(),
    )


@app.post("/reload-models")
def reload_models() -> dict[str, Any]:
    state.load()
    return {
        "status": "reloaded",
        **state.status(),
    }


@app.post("/feedback")
def feedback(req: FeedbackRequest) -> dict[str, Any]:
    changes: dict[str, Any] = {}
    if req.anomaly_threshold is not None:
        old = get_anomaly_threshold()
        set_anomaly_threshold(req.anomaly_threshold)
        changes["anomaly_threshold"] = {"old": old, "new": req.anomaly_threshold}
    return {
        "status": "applied" if changes else "no-changes",
        "changes": changes,
        "current_config": {
            "network_weight": NETWORK_WEIGHT,
            "behavior_weight": BEHAVIOR_WEIGHT,
            "anomaly_threshold": get_anomaly_threshold(),
            "allow_degraded_scoring": ALLOW_DEGRADED_SCORING,
        },
    }


def _dedupe_reasons(reasons: list[str]) -> list[str]:
    output: list[str] = []
    seen: set[str] = set()
    for reason in reasons:
        key = reason.strip().lower()
        if not key or key in seen:
            continue
        seen.add(key)
        output.append(reason)
    return output
