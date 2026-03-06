import json
import os
from pathlib import Path
from typing import Any

import joblib
import numpy as np
import pandas as pd
from fastapi import FastAPI
from pydantic import AliasChoices, BaseModel, ConfigDict, Field


APP_ROOT = Path(__file__).resolve().parents[1]
MODELS_ROOT = APP_ROOT / "models"
NETWORK_MODEL_PATH = MODELS_ROOT / "network" / "network_iforest.joblib"
NETWORK_META_PATH = MODELS_ROOT / "network" / "network_iforest_meta.json"
BEHAVIOR_DIR = MODELS_ROOT / "behavior"
USER_SUBJECT_MAP_PATH = BEHAVIOR_DIR / "user_subject_map.json"

NETWORK_WEIGHT = float(os.getenv("NETWORK_WEIGHT", "0.7"))
BEHAVIOR_WEIGHT = float(os.getenv("BEHAVIOR_WEIGHT", "0.3"))
ANOMALY_THRESHOLD = float(os.getenv("ANOMALY_THRESHOLD", "0.6"))


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


class ScoreResponse(BaseModel):
    anomaly_score: float
    is_anomaly: bool
    reasons: list[str]


class ModelState:
    def __init__(self) -> None:
        self.network_model: Any | None = None
        self.network_meta: dict[str, Any] = {}
        self.behavior_models: dict[str, dict[str, Any]] = {}
        self.user_subject_map: dict[str, str] = {}

    def load(self) -> None:
        if NETWORK_MODEL_PATH.exists() and NETWORK_META_PATH.exists():
            try:
                self.network_model = joblib.load(NETWORK_MODEL_PATH)
                with open(NETWORK_META_PATH, "r", encoding="utf-8") as f:
                    self.network_meta = json.load(f)
            except Exception as ex:
                print(f"[WARN] Failed to load network model: {ex}")
                self.network_model = None
                self.network_meta = {}

        if USER_SUBJECT_MAP_PATH.exists():
            with open(USER_SUBJECT_MAP_PATH, "r", encoding="utf-8") as f:
                self.user_subject_map = json.load(f)

        if BEHAVIOR_DIR.exists():
            for p in BEHAVIOR_DIR.glob("behavior_*.joblib"):
                try:
                    bundle = joblib.load(p)
                    subject = str(bundle.get("subject", "")).strip()
                    if subject:
                        self.behavior_models[subject] = bundle
                except Exception as ex:
                    print(f"[WARN] Failed to load behavior model {p.name}: {ex}")
                    continue


state = ModelState()
state.load()
app = FastAPI(title="ZTA AI Service", version="0.2.0")


def _safe_float(v: Any) -> float:
    try:
        return float(v)
    except Exception:
        return 0.0


def _path_risk(path: str, method: str, hour_of_day: int) -> tuple[float, list[str]]:
    score = 0.0
    reasons: list[str] = []
    low_path = path.lower()
    m = method.upper()

    if low_path.startswith("/api/admin"):
        score += 0.15
        reasons.append("admin-path-access")
    if m in {"PUT", "DELETE", "PATCH"}:
        score += 0.1
        reasons.append("state-changing-method")
    if hour_of_day < 6:
        score += 0.05
        reasons.append("off-hours-request")

    return min(score, 0.4), reasons


def _network_score(req: ScoreRequest) -> tuple[float, list[str]]:
    reasons: list[str] = []

    if state.network_model is None or not state.network_meta:
        return 0.5, ["network-model-missing"]

    feature_cols = state.network_meta.get("feature_columns", [])
    medians = state.network_meta.get("feature_medians", {})
    if not feature_cols:
        return 0.5, ["network-feature-metadata-missing"]

    vector = [_safe_float(medians.get(c, 0.0)) for c in feature_cols]
    idx = {name.lower(): i for i, name in enumerate(feature_cols)}

    # Best-effort mapping from runtime request signals into IDS-trained feature space.
    for col_name, val in (
        ("flow byts/s", req.payload_bytes),
        ("flow pkts/s", req.requests_per_minute),
        ("totlen fwd pkts", req.payload_bytes),
        ("tot fwd pkts", req.requests_per_minute),
        ("flow duration", req.request_latency_ms * 1000.0),
        ("pkt len mean", req.payload_bytes / max(req.requests_per_minute, 1)),
    ):
        i = idx.get(col_name)
        if i is not None:
            vector[i] = float(val)

    x = pd.DataFrame([vector], columns=feature_cols, dtype=np.float64)
    score_samples = float(state.network_model.score_samples(x)[0])
    network_score = 1.0 / (1.0 + np.exp(score_samples))

    if req.requests_per_minute > 120:
        reasons.append("frequency-spike")
    if req.payload_bytes > 300_000:
        reasons.append("payload-burst")
    if req.request_latency_ms > 2_000:
        reasons.append("latency-outlier")
    if not reasons:
        reasons.append("network-model-evaluated")

    return float(min(max(network_score, 0.0), 1.0)), reasons


def _resolve_subject(req: ScoreRequest) -> str | None:
    if req.behavior_subject:
        return req.behavior_subject.strip()

    if req.user_id in state.user_subject_map:
        return str(state.user_subject_map[req.user_id]).strip()

    user = req.user_id.strip().lower()
    if user.startswith("s") and len(user) == 4 and user[1:].isdigit():
        return user
    return None


def _behavior_score(req: ScoreRequest) -> tuple[float | None, list[str]]:
    subject = _resolve_subject(req)
    if not subject:
        return None, ["behavior-subject-unresolved"]

    bundle = state.behavior_models.get(subject)
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
def health() -> dict:
    return {
        "status": "ok",
        "network_model_loaded": state.network_model is not None,
        "behavior_models_loaded": len(state.behavior_models),
    }


@app.post("/score", response_model=ScoreResponse)
def score(req: ScoreRequest) -> ScoreResponse:
    network_score, network_reasons = _network_score(req)
    path_score, path_reasons = _path_risk(req.path, req.method, req.hour_of_day)
    behavior_score, behavior_reasons = _behavior_score(req)

    if behavior_score is None:
        final_score = min(1.0, network_score + path_score)
    else:
        final_score = min(1.0, NETWORK_WEIGHT * network_score + BEHAVIOR_WEIGHT * behavior_score + path_score)

    is_anomaly = final_score >= ANOMALY_THRESHOLD
    reasons = network_reasons + path_reasons + behavior_reasons

    return ScoreResponse(
        anomaly_score=round(float(final_score), 6),
        is_anomaly=is_anomaly,
        reasons=reasons,
    )
