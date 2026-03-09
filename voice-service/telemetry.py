"""OpenTelemetry setup for the Zettel voice service.

Mirrors the backend pattern (ZettelTelemetry / Program.cs):
- Instrumentation is always configured
- OTLP export only activates when OTEL_ENDPOINT is set
- Call setup_telemetry() once at startup, before any other imports that use spans

Usage:
    from telemetry import setup_telemetry, tracer, voice_sessions_started, ...
    setup_telemetry()   # call before creating any spans
"""

import logging
import os

from opentelemetry import metrics, trace

SERVICE = "zettel-voice"

_logger = logging.getLogger(__name__)

# ── Module-level handles — populated by setup_telemetry() ────────────────────
# Declared here so other modules can import them at the top of the file;
# safe to use only after setup_telemetry() has been called.

tracer: trace.Tracer
voice_sessions_started: metrics.Counter
voice_session_duration: metrics.Histogram
voice_audio_frames: metrics.Counter
voice_tool_calls: metrics.Counter
voice_errors: metrics.Counter


def setup_telemetry() -> None:
    """Initialise OpenTelemetry SDK.  Must be called before any spans are created."""
    global tracer, voice_sessions_started, voice_session_duration
    global voice_audio_frames, voice_tool_calls, voice_errors

    from opentelemetry._logs import set_logger_provider
    from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
    from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
    from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
    from opentelemetry.instrumentation.httpx import HTTPXClientInstrumentor
    from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
    from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
    from opentelemetry.sdk.metrics import MeterProvider
    from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
    from opentelemetry.sdk.resources import SERVICE_NAME, Resource
    from opentelemetry.sdk.trace import TracerProvider
    from opentelemetry.sdk.trace.export import BatchSpanProcessor

    endpoint = os.getenv("OTEL_ENDPOINT")
    resource = Resource.create({SERVICE_NAME: SERVICE})

    # ── Traces ────────────────────────────────────────────────────────────────
    tracer_provider = TracerProvider(resource=resource)
    if endpoint:
        tracer_provider.add_span_processor(
            BatchSpanProcessor(OTLPSpanExporter(endpoint=endpoint))
        )
    trace.set_tracer_provider(tracer_provider)

    # ── Metrics ───────────────────────────────────────────────────────────────
    readers = []
    if endpoint:
        readers.append(PeriodicExportingMetricReader(OTLPMetricExporter(endpoint=endpoint)))
    meter_provider = MeterProvider(resource=resource, metric_readers=readers)
    metrics.set_meter_provider(meter_provider)

    # ── Logs — bridge stdlib logging into OTLP ────────────────────────────────
    log_provider = LoggerProvider(resource=resource)
    if endpoint:
        log_provider.add_log_record_processor(
            BatchLogRecordProcessor(OTLPLogExporter(endpoint=endpoint))
        )
    set_logger_provider(log_provider)
    # Attach to the root logger so every logger.info/warning/error is captured
    logging.getLogger().addHandler(
        LoggingHandler(level=logging.NOTSET, logger_provider=log_provider)
    )

    # ── HTTP auto-instrumentation (covers all httpx calls in tools.py) ────────
    HTTPXClientInstrumentor().instrument()

    # ── Tracer and meter ──────────────────────────────────────────────────────
    tracer = trace.get_tracer(SERVICE)
    _meter = metrics.get_meter(SERVICE)

    # ── Metrics definitions ───────────────────────────────────────────────────
    voice_sessions_started = _meter.create_counter(
        "zettel.voice.sessions.started",
        description="Number of voice sessions started",
    )
    voice_session_duration = _meter.create_histogram(
        "zettel.voice.sessions.duration",
        unit="ms",
        description="Duration of voice sessions in milliseconds",
    )
    voice_audio_frames = _meter.create_counter(
        "zettel.voice.audio_frames",
        description="Number of audio frames forwarded to Nova Sonic",
    )
    voice_tool_calls = _meter.create_counter(
        "zettel.voice.tool_calls",
        description="Number of tool calls made during voice sessions",
    )
    voice_errors = _meter.create_counter(
        "zettel.voice.errors",
        description="Number of voice session errors by type",
    )

    if endpoint:
        _logger.info("OpenTelemetry OTLP export enabled → %s", endpoint)
    else:
        _logger.info("OTEL_ENDPOINT not set — telemetry configured but not exported")
