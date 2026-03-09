"""FastAPI entry point for the Zettel voice service."""

import asyncio
import base64
import json
import logging
import os
import time
import uuid

from dotenv import load_dotenv

load_dotenv()  # load .env before any other module reads env vars

# Configure stdout logging BEFORE telemetry setup — basicConfig is a no-op if any
# handler already exists on the root logger, so it must run first.
logging.basicConfig(
    level=os.getenv("LOG_LEVEL", "INFO"),
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)

# Telemetry must be initialised before other imports so that get_tracer/get_meter
# use the real provider rather than the default no-op proxy.
from telemetry import setup_telemetry  # noqa: E402

setup_telemetry()

from opentelemetry import trace  # noqa: E402
from opentelemetry.trace import SpanKind, StatusCode  # noqa: E402

import telemetry  # noqa: E402 — import after setup so all handles are populated

from fastapi import FastAPI, WebSocket, WebSocketDisconnect  # noqa: E402
from strands.experimental.bidi import (  # noqa: E402
    BidiAudioInputEvent,
    BidiAudioStreamEvent,
    BidiErrorEvent,
    BidiResponseCompleteEvent,
    BidiResponseStartEvent,
    BidiTranscriptStreamEvent,
    ToolResultEvent,
    ToolUseStreamEvent,
)

from agent import create_agent  # noqa: E402
from tools import AUDIO_SAMPLE_RATE, extract_citations, get_note, search_notes  # noqa: E402, F401

logger = logging.getLogger(__name__)

app = FastAPI(title="Zettel Voice Service")


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket) -> None:
    await websocket.accept()
    session_id = str(uuid.uuid4())
    start = time.monotonic()
    disconnect_reason = "unknown"
    logger.info("voice session started", extra={"session.id": session_id})

    telemetry.voice_sessions_started.add(1)

    agent = create_agent()
    cited_note_ids: set[str] = set()

    async def _send_json(payload: dict) -> None:
        try:
            await websocket.send_text(json.dumps(payload))
        except Exception as exc:
            logger.debug("_send_json failed (session closing): %s", exc)

    with telemetry.tracer.start_as_current_span(
        "voice.session",
        kind=SpanKind.SERVER,
        attributes={"session.id": session_id},
    ) as span:
        try:
            await agent.start()
            await _send_json({"type": "status", "state": "listening"})

            async def _receive_from_browser() -> None:
                """Forward binary PCM frames from the browser into Nova Sonic."""
                import struct
                frames = 0
                try:
                    while True:
                        data = await websocket.receive_bytes()
                        frames += 1
                        if frames % 50 == 0 or frames == 1:
                            samples = struct.unpack(f"{len(data) // 2}h", data)
                            peak = max(abs(s) for s in samples)
                            logger.info(
                                "audio frame %d (%d bytes), peak amplitude: %d",
                                frames,
                                len(data),
                                peak,
                                extra={"session.id": session_id},
                            )
                        if frames % 200 == 0:
                            telemetry.voice_audio_frames.add(200)
                        audio_b64 = base64.b64encode(data).decode("utf-8")
                        await agent.send(
                            BidiAudioInputEvent(
                                audio=audio_b64,
                                format="pcm",
                                sample_rate=AUDIO_SAMPLE_RATE,
                                channels=1,
                            )
                        )
                except WebSocketDisconnect:
                    logger.info(
                        "browser disconnected after %d audio frames",
                        frames,
                        extra={"session.id": session_id},
                    )
                except Exception as exc:
                    logger.warning(
                        "receive_from_browser error after %d frames: %s",
                        frames,
                        exc,
                        extra={"session.id": session_id},
                    )

            async def _receive_from_agent() -> None:
                """Process events from Nova Sonic and forward to the browser."""
                nonlocal cited_note_ids

                logger.info("agent event loop started", extra={"session.id": session_id})
                async for event in agent.receive():
                    logger.debug(
                        "agent event: %s",
                        type(event).__name__,
                        extra={"session.id": session_id},
                    )
                    if isinstance(event, BidiResponseStartEvent):
                        await _send_json({"type": "status", "state": "thinking"})

                    elif isinstance(event, BidiAudioStreamEvent):
                        await _send_json({"type": "status", "state": "speaking"})
                        audio_bytes = base64.b64decode(event.audio)
                        await websocket.send_bytes(audio_bytes)

                    elif isinstance(event, BidiTranscriptStreamEvent):
                        if event.is_final:
                            role = getattr(event, "role", "assistant")
                            text = getattr(event, "current_transcript", None) or getattr(event, "text", "")
                            if text:
                                await _send_json(
                                    {"type": "transcript", "role": role, "text": text}
                                )

                    elif isinstance(event, ToolUseStreamEvent):
                        tool_name = getattr(event, "name", "unknown")
                        telemetry.voice_tool_calls.add(1, {"tool.name": tool_name})
                        await _send_json({"type": "status", "state": "thinking"})

                    elif isinstance(event, ToolResultEvent):
                        result = getattr(event, "result", None)
                        if result is not None:
                            new_citations = [
                                c for c in extract_citations(result)
                                if c["id"] not in cited_note_ids
                            ]
                            for c in new_citations:
                                cited_note_ids.add(c["id"])
                            if new_citations:
                                await _send_json({"type": "citations", "notes": new_citations})

                    elif isinstance(event, BidiResponseCompleteEvent):
                        await _send_json({"type": "status", "state": "listening"})

                    elif isinstance(event, BidiErrorEvent):
                        message = getattr(event, "message", str(event))
                        logger.error(
                            "nova sonic error: %s", message, extra={"session.id": session_id}
                        )
                        span.set_status(StatusCode.ERROR, message)
                        telemetry.voice_errors.add(1, {"error.type": "nova_sonic"})
                        await _send_json({"type": "error", "message": message})
                        break  # session is in undefined state after a BidiErrorEvent; stop cleanly

                logger.warning(
                    "agent.receive() exhausted — no more events will arrive",
                    extra={"session.id": session_id},
                )

            try:
                async with asyncio.TaskGroup() as tg:
                    tg.create_task(_receive_from_browser())
                    tg.create_task(_receive_from_agent())
            except* WebSocketDisconnect:
                disconnect_reason = "client_disconnect"
                logger.info(
                    "voice session: client disconnected", extra={"session.id": session_id}
                )
            except* Exception as eg:
                disconnect_reason = "agent_error"
                for exc in eg.exceptions:
                    logger.exception(
                        "voice session error: %s", exc, extra={"session.id": session_id}
                    )
                telemetry.voice_errors.add(1, {"error.type": "agent_error"})
                span.set_status(StatusCode.ERROR, str(eg.exceptions[0]))
                await _send_json({"type": "error", "message": "session error"})

        except WebSocketDisconnect:
            disconnect_reason = "client_disconnect"
            logger.info("WebSocket client disconnected", extra={"session.id": session_id})
        except Exception as exc:
            disconnect_reason = "unhandled_exception"
            logger.exception(
                "Unhandled error in WebSocket session: %s", exc, extra={"session.id": session_id}
            )
            telemetry.voice_errors.add(1, {"error.type": "unhandled"})
            span.record_exception(exc)
            span.set_status(StatusCode.ERROR, str(exc))
            await _send_json({"type": "error", "message": str(exc)})
        finally:
            duration_ms = round((time.monotonic() - start) * 1000, 1)
            span.set_attribute("session.duration_ms", duration_ms)
            span.set_attribute("session.disconnect_reason", disconnect_reason)
            telemetry.voice_session_duration.record(
                duration_ms, {"session.disconnect_reason": disconnect_reason}
            )
            logger.info(
                "voice session ended",
                extra={
                    "session.id": session_id,
                    "session.duration_ms": duration_ms,
                    "session.disconnect_reason": disconnect_reason,
                },
            )
            try:
                await agent.stop()
            except Exception as exc:
                logger.warning(
                    "agent.stop() failed: %s", exc, extra={"session.id": session_id}
                )
