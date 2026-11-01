---
sidebar_label: ElevenLabs and Cartesia
sidebar_position: 5
title: ElevenLabs and Cartesia Voices
description: Text-to-speech, speech recognition, voice search and voice cloning with ElevenLabs and Cartesia, and the provider-neutral voice library and speech markup they share.
---

# ElevenLabs and Cartesia Voices

> Speak and listen with ElevenLabs or Cartesia in a cascaded realtime deployment, the chat hub's voice mode or any host
> code that asks for an `ITextToSpeechClient` or `ISpeechToTextClient`, and search or clone voices without leaving the
> framework.

Both providers are speech-only: they have no chat, embedding or image models. Either can be the text-to-speech leg of a
cascade or the voice of a text chat. Cartesia can also be a cascade's speech-to-text leg, through its realtime
transcription (see [Hearing in a cascade](#hearing-in-a-cascade)). ElevenLabs' speech recognition transcribes whole
recordings, which serves dictation but not a cascade, whose speech-to-text leg hears the user live.

## Quick Start

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIElevenLabs()   // CrestApps.Core.AI.ElevenLabs
    .AddCoreAICartesia();    // CrestApps.Core.AI.Cartesia
```

Each registers an `IAIClientProvider`, an `IVoiceLibraryProvider` and a deployment provider with a contained connection:
create a deployment with the provider `ElevenLabs` or `Cartesia`, the API key (and, optionally, an endpoint), and the
model.

| | ElevenLabs | Cartesia |
| --- | --- | --- |
| Client name | `ElevenLabs` | `Cartesia` |
| Text-to-speech model | `eleven_flash_v2_5` (default), `eleven_multilingual_v2`, ... | `sonic-2` (default), `sonic-3`, ... |
| Speech-to-text model | `scribe_v1` (default), `scribe_v2` | `ink-whisper` (default); `ink-2` or `ink-preview` to hear in a cascade |
| Endpoint | `https://api.elevenlabs.io`; set the connection's `Endpoint` for a residency region | `https://api.cartesia.ai` |
| Authentication | `xi-api-key` header | `X-API-Key` header and a pinned `Cartesia-Version` |
| Voice cloning | Instant voice cloning from one or more recordings | From one clip |

A deployment's model name picks the model; the voice is chosen where voices are chosen everywhere else (the profile's or
persona's voice, `TextToSpeechOptions.VoiceId`). These providers have no default voice, so a request without one fails
with a message that says so.

## Text-to-speech

- Streams the audio as it is generated, so a realtime reply starts speaking before it has finished.
- A realtime session asks for raw PCM by naming the format `Pcm<rate>` (for example `Pcm24000`); anything else gets MP3.
  ElevenLabs produces PCM at 8, 16, 22.05, 24, 32, 44.1 and 48 kHz; Cartesia at 8, 16, 22.05, 24, 44.1 and 48 kHz. Any
  other rate fails instead of playing at the wrong speed.
- A 16-bit sample that straddles two network reads is held back for the next chunk, so streamed PCM never shifts by a
  byte.
- `TextToSpeechOptions.Speed` is limited to what the provider accepts (ElevenLabs 0.7 to 1.2, Cartesia 0.6 to 1.5).
- `TextToSpeechOptions.Language` is sent as an ISO 639-1 code (`es-MX` becomes `es`). ElevenLabs' multilingual v2
  models detect the language and are not sent one; Cartesia defaults to `en`.
- `TextToSpeechOptionsProperties.Emotion` selects Cartesia's emotion control (`calm`, `excited`, ...). ElevenLabs ignores
  it.
- The profile's pronunciation dictionary, speaking rate and fallback voice (see
  [Profile Speech](../core/ai-profile-speech-and-language.md)) apply as with every provider.

## Speech markup

Text sent to a voice can carry two neutral tags, so a prompt does not depend on the provider that speaks it:

| Markup | Meaning |
| --- | --- |
| `<break time="500ms"/>` or `<break time="1.5s"/>` | A pause, at most 3 seconds. |
| `<spell>AB12</spell>` | Read one character at a time. |

`SpeechMarkup.Render(text, dialect)` turns them into ElevenLabs break tags and hyphenated letters, Cartesia break and
spell tags, or plain punctuation and spaced letters (`SpeechMarkupDialect.Plain`). The ElevenLabs and Cartesia clients
render them themselves. The OpenAI, Azure OpenAI and Azure Speech text-to-speech clients are wrapped in
`SpeechMarkupTextToSpeechClient`, so the tags are never read aloud. A host that wraps another provider can do the same:

```csharp
ITextToSpeechClient client = new SpeechMarkupTextToSpeechClient(inner, SpeechMarkupDialect.Plain);
```

## Speech recognition

- Both APIs transcribe a whole recording, so streaming yields the finished transcript as one update.
- Audio that has a container (WAV, MP3, Ogg, FLAC, WebM) is uploaded as it is. Anything else is taken for raw 16-bit mono
  PCM at `SpeechToTextOptions.SpeechSampleRate` (16 kHz when unset) and wrapped in a WAV header
  (`SpeechAudioFormat.ToUploadable`).
- The language is sent as an ISO 639-1 code.
- Boosted keywords (`SpeechToTextOptionsProperties.BoostedKeywords`) are sent as key terms to the Scribe v2 models.
  Scribe v1 and Cartesia have no vocabulary hints and ignore them.

## Hearing in a cascade

A Cartesia deployment can be the speech-to-text leg of a cascaded realtime deployment. Its realtime client opens
transcription sessions on Cartesia's turns endpoint (`wss://api.cartesia.ai/stt/turns/websocket`), which transcribes the
user's audio as it streams and decides on the server where each of the user's turns ends:

- each `turn.update` carries the turn so far; its new words become a partial transcript, which a cascade takes as the user
  talking over the assistant;
- `turn.end` carries the turn's definitive text, the completed transcript that starts the reply. It is metered with the
  length of the audio sent since the previous turn ended;
- the user starting to speak is reported with the turn's first words, so a sound with no words neither starts a turn nor
  interrupts the assistant. Punctuation is not words: Cartesia can open a turn as the previous one ends that holds nothing
  but its full stop;
- a silence the session's turn detection names caps how long Cartesia waits after speech before it ends a turn (640 to
  11,200 ms); without one it waits at most 1,200 ms;
- a turn Cartesia opens as the previous one ends can stay open through the silence after it, even with words in it,
  until the user speaks again. A turn with words Cartesia says nothing more about for a second past that cap is ended by
  the session. Cartesia's own end of it adds nothing, and words Cartesia hears in it afterwards are a turn of their own;
- the session logs, at the debug level, each event Cartesia sends with its turn. What the user said is logged only at the
  trace level.

Name a Cartesia deployment whose model is `ink-2` or `ink-preview` as the cascade's speech-to-text deployment. A deployment
on `ink-whisper` (the batch default) or with no model hears with `ink-2`. Cartesia detects the language itself; its realtime
models transcribe English, French, Hindi, Japanese and Spanish. A refused connection says why: the API key, the model or
audio format, the account's credit, or too many connections.

## Voice library: search and cloning

`IVoiceLibrary` lists beyond the short voice list `ISpeechVoiceResolver` returns:

```csharp
var library = await voiceLibraryResolver.GetLibraryAsync(deployment, cancellationToken);

var page = await library.SearchAsync(new VoiceSearchRequest
{
    Query = "calm",
    Language = "es",
    Gender = SpeechVoiceGender.Female,
}, cancellationToken);

// page.Voices, and page.NextCursor for the next page.

if (library.SupportsCloning)
{
    var voice = await library.CloneAsync(new VoiceCloneRequest
    {
        Name = "Support agent",
        Language = "en",
        Samples = [new VoiceCloneSample { FileName = "agent.mp3", ContentType = "audio/mpeg", Content = bytes }],
    }, cancellationToken);

    // voice.Id is now a voice the deployment can speak with.
}
```

- `IVoiceLibraryResolver` (registered by `AddCoreAIServices`) finds the provider's `IVoiceLibraryProvider` from the
  deployment and opens it with the deployment's connection. It returns `null` for a provider with no library.
- Search filters that the API does not take (ElevenLabs language and gender, Cartesia language) are applied to the page
  returned, so a filtered page can hold fewer voices than the page size; follow `NextCursor` to read on.
- Cloning stores the recording with the provider. Get the speaker's consent first, and offer `DeleteAsync` to remove a
  voice you created. Only voices created through `CloneAsync` should be deleted.
- A provider without cloning (`SupportsCloning` is `false`) throws `NotSupportedException` from `CloneAsync` and
  `DeleteAsync`. A host registers a library for another provider with an `IVoiceLibraryProvider`.

## Errors

A failed call raises `HttpRequestException` whose `StatusCode` is set and whose message names the provider, the status,
a hint for 401 (check the API key) and 429 (rate limit or concurrency limit), and the start of the answer's body. The
API key is never part of the message.

## Verified against a live account?

The request and response shapes follow the providers' public API documentation and are covered by tests against
recorded-shape fakes. They have not been run against a live ElevenLabs or Cartesia account. Before relying on them in
production, run one real call of each kind with your key: speak a sentence as MP3 and as `Pcm24000`, transcribe a short
recording, search voices, and (if you use it) clone and delete a voice. Cartesia's request shapes are pinned by the
`Cartesia-Version` header the provider sends.
