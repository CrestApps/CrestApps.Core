---
sidebar_label: AI Animations
sidebar_position: 16
title: AI Animations
description: Let a conversation turn a request for a moving scene into an animated MP4 that the model draws in code and the chat plays.
---

# AI Animations

> Ask for "a German Shorthaired Pointer on point in a golden field at sunrise, then a pheasant flushes" and get a
> video that plays in the chat — written as drawing code by your own chat model, run in a sandbox, and encoded on
> your own server, with no video service account.

## Quick Start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddDocumentProcessing(documentProcessing => documentProcessing
            .AddAnimations()
        )
    )
);

app.AddAnimationAssetEndpoint();
```

Install FFmpeg on the server (`apt install ffmpeg`, `winget install Gyan.FFmpeg`, or a layer in your container
image), or point `CrestApps:AI:Animations:FFmpegPath` at it. Without a usable FFmpeg the agent is not offered.

## How It Works

`AddAnimations()` registers the **Animation Agent** (`animation-agent`), a system agent built like the PDF and Word
agents: always available to the primary model, hidden from the agent pickers, and running two hidden tools,
`render_animation` and `get_animation_source`.

1. The primary model hands the request to the agent.
2. The agent writes a JavaScript program for that one video: `setup(info)` lays the scene out once, and
   `frame(ctx, t, state)` draws one frame at time `t` on a canvas-like `ctx` — paths, curves, gradients, blend
   modes, blur, shadows and text, plus `draw` helpers for smooth organic outlines, tapered limbs, shading, easing
   and noise, and scene helpers for skies, glows, light rays, hills, grass fields, contact shadows and camera moves.
3. The script runs in the **Jint** JavaScript interpreter, inside the app, with no file, network, process or .NET
   access. Every call — setup and each frame — has its own time, statement, memory and recursion limits.
4. A short test run checks the script first. A JavaScript error comes back with its line; a script too slow to
   finish, or one that draws a whole subject into a few pixels, comes back with what to fix.
5. A **director's review** looks at key frames before rendering: they are drawn and judged against the request by
   the vision deployment, and a script that falls short goes back to the agent with concrete notes. A first draft
   and one revision are reviewed; the next attempt is rendered.
6. The frames are replayed onto a SkiaSharp canvas and piped to FFmpeg, which writes an H.264 MP4 (`yuv420p`,
   `+faststart`). An optional film look — letterbox, vignette, grain — is laid over each frame.
7. The video, a poster still, the script and its settings are stored with the conversation, and a `[fig:N]` marker
   registered with `AICompletionReference.IsVideo` is replaced in the chat by a video player with a download
   button.

Follow-ups such as "make the dog bigger" call `get_animation_source` and change the stored script rather than
starting again.

## What to Expect

- **Any subject can be attempted**: animals, people, vehicles, places, nature and action, because the model draws
  it rather than choosing from a catalogue.
- **Quality varies from run to run** and depends on the chat model. Results are stylized 2D animation drawn in
  code, not photoreal or 3D footage, and without sound. A stronger model writes noticeably better drawing code;
  `AgentDeploymentName` chooses the one the agent uses.
- **Physical values are simulation settings.** A wind speed in the request sets how things move in the drawing;
  it is not a validated physical model.
- A video takes from under a minute to a few minutes: the model writes the script, the review may ask for one or
  two revisions, and a 15-second video at 720p renders in tens of seconds on the CPU.

## Configuration

```json
{
  "CrestApps": {
    "AI": {
      "Animations": {
        "Enabled": true,
        "FFmpegPath": "ffmpeg",
        "BasePath": "App_Data/Animations",
        "ReviewScriptsBeforeRender": true,
        "MaxDurationSeconds": 30,
        "RenderTimeout": "00:02:00"
      }
    }
  }
}
```

| Setting | Default | Notes |
| --- | --- | --- |
| `Enabled` | `true` | Turning it off removes the agent and its description from every request. |
| `FFmpegPath` | `ffmpeg` | A full path, or a name on the `PATH`. Checked on first use; FFmpeg that cannot be started is tried again a minute later. |
| `VideoEncoder` | first available | `libx264`, `libopenh264` or `h264_mf`. |
| `ConstantRateFactor` / `BitRateKilobits` | `20` / `6000` | Quality for libx264, and the bit rate for encoders without a rate factor. |
| `BasePath` | `App_Data/Animations` | Relative to the content root. |
| `TempDirectory` | system temp | Where a render works before the video is stored. |
| `AgentDeploymentName` | the calling chat's deployment | The chat deployment the agent writes its drawing code with, when it should differ from the one the chat uses — for example a stronger model. |
| `ReviewScriptsBeforeRender` | `true` | The director's review of key frames. Needs a vision deployment. |
| `DefaultWidth` / `DefaultHeight` / `DefaultFrameRate` | `1280` / `720` / `30` | Used when the request gives none. |
| `MaxWidth` / `MaxHeight` / `MaxFrameRate` | `1920` / `1080` / `60` | |
| `DefaultDurationSeconds` / `MaxDurationSeconds` | `10` / `30` | |
| `MaxScriptCharacters` | `100000` | |
| `ScriptFrameTimeout` | 2 seconds | Time allowed for setup or one frame. |
| `MaxScriptStatementsPerFrame` / `MaxScriptMemoryBytes` | `5000000` / 256 MB | Per call into the script. |
| `MaxScriptCommandsPerFrame` | `2000000` | Drawing values one frame may record. |
| `RenderTimeout` | 2 minutes | A render that runs longer is stopped. |
| `MaxConcurrentRenders` | `2` | Each render keeps a CPU core busy; further requests wait. |
| `MaxOutputBytes` | 50 MB | |

## The Sandbox

The model's script is treated as untrusted. It runs in Jint with .NET interop off, and nothing from the host is
handed to it: the script records its drawing calls as numbers and strings, and the host replays them onto the
canvas. `Math.random` is seeded and `Date.now` returns the frame time, so the same script always draws the same
video. Runaway loops, memory bombs and deep recursion stop the frame with a message the agent can act on.

## FFmpeg and Licensing

FFmpeg is not bundled; whoever deploys the application installs it and takes on its license terms. FFmpeg is
LGPL-2.1 by default and GPL when built with `libx264`. Running it as a separate process does not place your code
under either license. H.264 may carry patent royalties for commercial distribution. Jint is BSD-2-Clause and
SkiaSharp is MIT.

## Storage, Access and Cleanup

Videos are kept outside the document store, under `{BasePath}/{referenceType}/{referenceId}/{animationId}/` as
`video.mp4`, `poster.png`, `code.js` and `settings.json`. Implement `IAnimationStore` to keep them elsewhere.

`GET ai/animations/{referenceType}/{referenceId}/{animationId}/{video|poster}` serves them with range support. It is
authorized with the same owner rule as a document download. Videos are deleted when the conversation is deleted
and when a chat interaction's history is cleared, even while a video is playing.

## Cancellation and Failure

Stopping a response cancels the render on providers that pass cancellation to their tools: the script, FFmpeg and
anything it started are stopped and the working folder is deleted. A render that fails, runs past
`RenderTimeout`, or produces a file over `MaxOutputBytes` is reported to the user in plain words. The chat shows
only its typing indicator while a video is made.
