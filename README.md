# Anubhav — VR Public Speaking Coach

🏆 **Sarvam AI Track winner, WE Hack 5.0** (IEEE WIE VIT, 36-hour hackathon) — Team Kaala Teeka.

Puts a speaker in front of a live virtual audience on a Meta Quest headset and gives them
honest, real-time coaching in **22 Indian languages plus English**, powered by Sarvam AI's
speech-to-text, LLM, and text-to-speech pipeline.

This repo is the Unity/Quest client. The FastAPI hub it talks to lives in the collaborator
repo [`vaibhav700c/Anubhav`](https://github.com/vaibhav700c/Anubhav).

## How a session works

1. The speaker starts talking; **`AudioManager`** streams raw PCM16 audio to the backend
   hub over a WebSocket as it's captured — no need to finish the sentence first.
2. The hub runs Sarvam's STT → `sarvam-105b` LLM coaching pass → Bulbul TTS, auto-detecting
   the spoken language across all 22 supported codes (or pinned to one via `sessionLanguage`).
3. **`HubClient`** keeps two connections open at once rather than one, because the two data
   streams arrive at very different rates:
   - a **telemetry channel** delivering partial transcript + live score/emotion roughly
     every 3.5 seconds, used to drive the in-VR transcript panel and an "aura" color in
     near real time;
   - the **coaching channel**, which only resolves once the full LLM+TTS round trip
     completes (20–30 s, since the model reasons before answering) and carries the actual
     spoken coaching line.

   Score/emotion get applied twice (fast from telemetry, then re-applied when coaching
   arrives) rather than freezing the UI for the entire round trip.
4. **`AudienceManager`** reacts a 5×6 seated crowd to that score/emotion — deliberately
   *not* all at once. A real crowd doesn't move in unison, so one random member reacts
   every 6–7 seconds while everyone else stays idle; this alone was the difference between
   the crowd reading as alive versus obviously scripted.
5. **`SessionController`** drives the session clock and, on finish, calls
   `POST /session/complete` — the backend's own accumulated transcript is treated as
   authoritative over anything reconstructed from partial telemetry frames on the client.
6. **`UIManager`** shows the live transcript, score/emotion HUD, and the final coaching
   report.

## Architecture

```
Unity (Quest)                              FastAPI Hub (vaibhav700c/Anubhav)
┌─────────────┐   PCM16 audio (binary)     ┌───────────────────────────┐
│ AudioManager│ ─────────────────────────▶ │ WS /session/{id}?vr       │
├─────────────┤                            │  Sarvam STT → sarvam-105b │
│ HubClient   │ ◀── coach_feedback + TTS ── │  → Bulbul TTS             │
│  (2 sockets)│ ◀── transcript/score/emo ── │ WS /session/{id}?app      │
├─────────────┤   POST /session/complete   │  (Flutter telemetry twin) │
│AudienceMgr  │ ◀────── final report ─────  │ POST /session/complete    │
│SessionCtrl  │                            └───────────────────────────┘
│UIManager    │
└─────────────┘
```

## Tech stack

Unity, C#, Meta XR / Oculus Interaction SDK, `System.Net.WebSockets` for the hub link,
Newtonsoft.Json, TextMesh Pro. Backend (separate repo): FastAPI, Sarvam AI (STT/LLM/TTS).

## Building

Open in Unity for Android/Quest, point `HubClient.hubBaseUrl` at a running instance of the
[backend hub](https://github.com/vaibhav700c/Anubhav) (`ws://<host>:8000` for local dev),
and build `Assets/Scenes/anubhav.unity` to a Quest device. The rule-based reconnect and
dual-channel telemetry work against the hub's mock-mode fallback data even without a live
Sarvam API key configured.
