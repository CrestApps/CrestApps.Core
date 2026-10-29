---
sidebar_label: Speech, Language and Typed Extraction
sidebar_position: 31
title: Profile Speech, Language and Typed Extraction
description: Per-profile text-to-speech, speech recognition, turn-taking and language settings, plus typed data-extraction fields, and how a host exposes them.
---

# Profile Speech, Language and Typed Extraction

These settings live on the AI profile and work with every provider. They are stored as profile settings (read with
`profile.TryGetSettings<T>()`, written with `profile.AlterSettings<T>()`), so a profile saved before they existed
behaves exactly as it did.

## Text-to-speech: `AIProfileSpeechSettings`

| Setting | Meaning |
| --- | --- |
| `SpeakingRate` | Multiple of the voice's normal pace (`1` is normal), clamped to 0.25-4. Multiplies the deployment's own pace (`CascadedRealtimeMetadata.SpeechSpeed`). `null` leaves the pace alone. |
| `SpeakingEmotion` | One word (`calm`, `excited`) passed as `TextToSpeechOptionsProperties.Emotion`, for a provider with an emotion control (Cartesia). Other providers ignore it. |
| `PronunciationEntries` | Word or phrase to spoken alias (`SQL` to `sequel`). Applied to the text before synthesis, whole words only, longest phrase first, so it works with every TTS provider. |
| `FallbackTextToSpeechDeploymentName` | Deployment used when the first one throws before producing audio. A failure after audio has started is not retried, because two voices cannot be spliced mid-sentence. The fallback speaks with its own default voice and model. |

Where it applies:

- **Chat hub** (`SynthesizeSpeech` and the speech-to-speech conversation loop): the TTS client is wrapped by
  `ProfileSpeech.DecorateAsync` (fallback, then pronunciation, then rate).
- **Cascaded realtime**: `DefaultRealtimeOrchestrator` reads the profile and hands the cascade a
  `CascadedRealtimeProfileOptions` (the same three settings).

A host that calls TTS itself can use the same helpers: `PronunciationDictionary`, `PronunciationTextToSpeechClient`,
`FallbackTextToSpeechClient` and `ProfileSpeech`.

## Speech recognition: `AIProfileSpeechSettings`

| Setting | Meaning |
| --- | --- |
| `BoostedKeywords` | Words and phrases the recognizer should favor. |
| `RecognitionLanguages` | BCP-47 tags, primary first. The primary is used when the caller did not name a language. |
| `InterruptionSensitivity` | `0` to `1`. How readily caller speech stops the AI. `null` keeps today's behavior (any speech interrupts). |
| `EnableBackchanneling` / `BackchannelPhrases` | The AI says a short "mm-hm" while the caller talks for a while. |

- Keywords and languages reach a provider through `SpeechToTextOptions.AdditionalProperties` under
  `SpeechToTextOptionsProperties.BoostedKeywords` and `RecognitionLanguages`. A client that cannot use them ignores
  them. For the cascaded pipeline the keywords are also added to the transcription prompt as
  `Vocabulary: a, b.`, which prompt-based recognizers understand.
- **Interruption sensitivity** in a cascaded session sets how many words of caller speech cut the AI off
  (`>= 0.67` or unset: one word; `>= 0.34`: three; below: five; `0`: never). On a native realtime session it sets the
  server VAD threshold (`TurnTaking.ToVadThreshold`) and `0` turns interruption off. An explicit `VadThreshold` or
  `AllowInterruption = false` on the request always wins.
- **Backchanneling** is available on the cascaded pipeline only. The acknowledgement is spoken, never stored in the
  conversation history, and never over a reply that is still speaking. `BackchannelPlanner` decides when (every ~18
  words of one caller turn).

## Languages: `AIProfileLanguageSettings`

| Setting | Meaning |
| --- | --- |
| `AllowedLanguages` | BCP-47 tags the profile may use. Empty means no restriction. `es` and `es-MX` match each other. |
| `StartingLanguage` | Language a conversation starts in. |

- `AICompletionContextKeys.ConversationLanguage` in `AICompletionContext.AdditionalProperties` overrides the starting
  language (a host sets it from a conversation variable or from a detected language).
- `AIProfileLanguageCompletionContextBuilderHandler` appends the reply-language instruction to the system message
  ("Reply in Spanish. The only languages you may use are: ...") and writes the resolved language back to the same key.
- `IConversationLanguageService.ResolveStartingLanguage(profile, variable)` resolves the starting language.
- `IConversationLanguageService.ReportDetectedLanguageAsync(...)` is the hook for a detected caller language. If the
  language is allowed and differs from the current one, every registered `IConversationLanguageHandler` runs. A host
  uses its handler to switch the recognition language, set `DetectedLanguageContext.VoiceName`, and store the new
  language for the next completion context. Core does not detect languages itself; the host feeds the hook from its
  recognizer or a language-identification call.

## Typed extraction fields: `DataExtractionEntry`

`DataExtractionEntry` gains `FieldType` (`Text`, `Number`, `Boolean`, `Date`, `Enum`, `List`) and `AllowedValues` (for `Enum`).
The default is `Text`, which stores values as extracted, so existing entries and stored JSON are unchanged.

`DataExtractionValueNormalizer.TryNormalize` runs on every extracted value before it is stored:

| Type | Stored as | Rejected when |
| --- | --- | --- |
| `Number` | invariant decimal (`$1,200.50` becomes `1200.50`) | not a number |
| `Boolean` | `true` / `false` (accepts yes/no/y/n/1/0) | anything else |
| `Date` | `yyyy-MM-dd` | not a date |
| `Enum` | the allowed value's configured spelling | not in `AllowedValues` (an enum with none is kept as text) |
| `List` | one stored value per item, without duplicates | an item is not in `AllowedValues` (a list with none takes any text) |

A `List` field is collected across messages like a field with `AllowMultipleValues`; set either and the field keeps
several values (`DataExtractionValueNormalizer.IsMultiValued`). A list also accepts one string holding several items
separated by commas, semicolons or line breaks, because a model does not always return an array. Give a list
`AllowedValues` to make it a multiple-choice field. Any other multi-valued field takes each value as one item, and
validates it against its type.

A rejected value is dropped and logged at debug level. Typed fields also tell the model the expected type (the
`data-extraction-prompt` template); untyped fields produce the same prompt as before.

## What a host should expose

In the profile editor:

- **Voice tab**: speaking rate, pronunciation dictionary (two columns, plus match-case), fallback TTS deployment.
- **Listening tab**: boosted keywords, recognition languages, interruption sensitivity (slider), backchanneling and its
  phrases.
- **Languages tab**: allowed languages, starting language. Optionally a conversation variable that feeds
  `AICompletionContextKeys.ConversationLanguage`.
- **Data extraction**: a type per field and an allowed-values list when the type is enum.
