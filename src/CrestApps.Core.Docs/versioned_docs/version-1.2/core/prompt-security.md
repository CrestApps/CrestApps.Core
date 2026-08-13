---
sidebar_label: Prompt Security
sidebar_position: 6
title: Prompt Security
description: Defense-in-depth prompt injection protections for AI Profile-based chat experiences in CrestApps.Core.
---

# Prompt Security

> A local, regex-based security layer that hardens AI Profile chat experiences against prompt injection, prompt leakage, tool discovery, and context-extraction attacks.

`CrestApps.Core` includes a prompt security layer for **AI Profile-based chat**. It is designed as one layer in a broader defense-in-depth architecture:

- **input validation** blocks or flags suspicious prompts before they reach the model
- **system prompt hardening** adds security instructions and input boundaries
- **output filtering** detects likely prompt leakage in model responses
- **tool authorization** still enforces access when a model attempts to invoke tools
- **audit logging** records suspicious activity for investigation

## Scope

The prompt security layer is applied to **AI Profile chat experiences** where the framework owns the reusable behavior contract.

It is **not** applied to `ChatInteraction` playground-style experiences by default. That is intentional: Chat Interactions are meant for direct operator control over system prompts, models, and orchestration inputs, while AI Profiles are the reusable runtime contract that benefits most from enforced security defaults.

## How the detector works

The detector stays intentionally local and deterministic:

1. **Pre-flight limits** reject oversized prompts before deeper analysis runs.
2. **Input normalization** reduces common obfuscation tricks:
   - Unicode normalization (`FormKC`)
   - removal of zero-width and invisible characters
   - repeated-whitespace collapsing
   - homoglyph folding for a practical set of common Cyrillic and Greek lookalikes
3. **Regex rules** evaluate the normalized prompt and, when useful, the folded prompt.
4. **Weighted scoring** aggregates matched rules instead of stopping at the first match.
5. **Risk mapping** converts the aggregate score into `Low`, `Medium`, `High`, or `Critical`.
6. **Policy enforcement** blocks prompts at or above the effective blocking threshold for the site or profile.

This keeps evaluation:

- **fast** enough for inline request validation
- **explainable** for operators and audits
- **predictable** for regulated environments that prefer transparent controls

## Architecture

The detector is built around extensible rule components so new regex rules can be added without changing the main evaluation flow.

| Type | Purpose |
| --- | --- |
| `IPromptSecurityRule` | One pluggable rule that evaluates a normalized prompt |
| `PromptSecurityEvaluationContext` | Input plus normalized/folded forms and effective policy settings |
| `PromptSecurityRuleResult` | Rule metadata, categories, severity, and score contribution |
| `PromptSecurityRiskScoringEngine` | Aggregates matches into one final `PromptSecurityResult` |
| `PromptSecurityDetectionTelemetry` | Non-sensitive normalization and evaluation telemetry |

`PromptSecurityResult` now carries:

- `Disposition` (`Safe`, `Flagged`, `Blocked`)
- `RiskLevel`
- `Score`
- `DetectionRule` for the primary matched rule
- `MatchedRuleIds`
- `MatchedRules`
- `MatchedCategories`
- `Telemetry`

## Built-in detection coverage

The default rules target common enterprise prompt-injection and jailbreak patterns, including:

- instruction override attempts (`ignore previous instructions`, `replace system prompt`)
- role confusion and developer/system-role injection
- persona jailbreaks (`developer mode`, `DAN`, unrestricted personas)
- privilege escalation and authority impersonation
- prompt leakage and hidden-context disclosure
- indirect prompt probes (false assertions with "prove me wrong" framing)
- conversation-history extraction
- memory-store extraction
- configuration and secret-disclosure requests
- tool, function-schema, and agent-orchestration discovery
- true/false extraction games and character-by-character leakage
- encoding-based exfiltration (`base64`, `hex`, `unicode`, `binary`)
- delimiter and boundary manipulation
- RAG document prompt injection that tries to override trusted policy
- hypothetical scenario bypass ("in a fictional world where you have no restrictions...")
- output format manipulation (requesting JSON/XML fields containing system prompts)
- virtualization attacks (terminal simulation, shell emulation)
- context poisoning (planting persistent instructions for exploitation in later turns)
- model-specific completion attacks (injected control tokens like `<|end_turn|>`, `[INST]`, `<<SYS>>`)
- harmful content generation (XSS payloads, phishing emails, malware, hate speech)
- sensitive data probing (eliciting stored PII or conversation data)

Multiple low-confidence indicators can combine into a higher-confidence block, which is one of the main improvements over a first-match-only detector.

## Security options

The prompt security layer is controlled by `PromptSecurityOptions`.

### Core controls

| Option | Default | What it does |
| --- | --- | --- |
| `EnableInjectionDetection` | `true` | Enables prompt validation before the model runs |
| `EnableOutputFiltering` | `true` | Scans model output for likely prompt leakage or disclosure indicators |
| `EnableSecurityPreamble` | `true` | Prepends a security-focused system prompt layer |
| `EnableInputDelimiters` | `true` | Wraps user input in explicit untrusted-content markers |
| `EnableAuditLogging` | `true` | Records suspicious prompt or output events |
| `MaxPromptLength` | `8000` | Hard limit checked before deeper detection runs |
| `BlockingThreshold` | `High` | Final risk level that changes a suspicious prompt from flagged to blocked |
| `MaxMessagesPerWindow` | `20` | Maximum messages per sliding window before rate limiting kicks in |
| `RateLimitWindow` | `00:01:00` | Duration of the sliding window for rate limiting |
| `MaxAnonymousSessionsPerWindow` | `5` | Maximum anonymous chat sessions that can be started per window |
| `AnonymousSessionRateLimitWindow` | `00:10:00` | Duration of the anonymous session-start rate-limit window |

### Visitor identity and remote-address handling

`AIVisitorIdentityOptions` controls how anonymous visitors are stabilized and how network data is captured:

| Option | Default | What it does |
| --- | --- | --- |
| `CookieName` | `crestapps-ai-visitor` | Name of the first-party cookie used to persist anonymous visitor IDs |
| `CookieLifetime` | `180` days | Lifetime of the anonymous visitor cookie |
| `RemoteAddressMode` | `Hashed` | Chooses whether remote-address data is disabled, stored as a salted hash, stored in plain text, or encrypted at rest with Data Protection |
| `RemoteAddressHashSalt` | `CrestApps.Core.AI.VisitorIdentity` | Salt used when `RemoteAddressMode = Hashed` or `Encrypted` |

### Weighted-scoring thresholds

| Option | Default | Meaning |
| --- | --- | --- |
| `LowRiskScoreThreshold` | `10` | Minimum aggregate score required before a prompt is considered suspicious |
| `MediumRiskScoreThreshold` | `20` | Minimum score for `Medium` risk |
| `HighRiskScoreThreshold` | `35` | Minimum score for `High` risk |
| `CriticalRiskScoreThreshold` | `50` | Minimum score for `Critical` risk |

### Custom patterns

`CustomBlockedPatterns` lets you add organization-specific regex rules. These are useful for:

- internal canary tokens
- known tenant-specific secret names
- product-specific internal prompt markers
- policy phrases that should never appear in user input

## Site defaults and per-profile overrides

The framework supports two layers of configuration:

1. **site-level defaults** through `PromptSecurityOptions`
2. **per-profile anti-spam throttle overrides** through `PromptSecurityProfileSettings`

Both MVC and Blazor sample hosts expose the site defaults in admin settings. AI Profiles and AI Profile-source templates can override the anti-spam throttle limits so each use case can raise or lower its quotas.

Per-profile overrides are intentionally scoped to anti-spam throttling only:

- `MaxMessagesPerWindow` — maximum messages allowed within the message window
- `RateLimitWindow` — the message sliding-window duration
- `MaxAnonymousSessionsPerWindow` — maximum anonymous session starts within the session window
- `AnonymousSessionRateLimitWindow` — the anonymous session-start window duration

High-level input and output security guards — injection detection, output filtering, the security preamble, input delimiters, the blocking threshold, and the maximum prompt length — remain **global concerns** configured only through `PromptSecurityOptions`. They are deliberately not overridable per profile so protective posture stays consistent across every profile.

That model lets you keep strong global guards while allowing carefully chosen profiles to be more permissive or more strict on throttling.

## Rate limiting

The security layer includes built-in rate limiting to prevent abuse and brute-force prompt extraction attempts. Rate limiting runs **before** the expensive regex evaluation, acting as an early short-circuit for abusive senders.

By default, spam reduction is layered:

1. Anonymous browsers receive a stable first-party visitor cookie.
2. Prompt traffic is throttled per visitor, with optional network-address participation and session/connection fallbacks.
3. Anonymous session starts are throttled separately so attackers cannot reset prompt quotas just by creating new sessions.
4. Sample hosts also apply an ASP.NET Core endpoint policy to explicit session-start endpoints before the chat pipeline runs.

### Configuration

| Option | Default | What it does |
| --- | --- | --- |
| `MaxMessagesPerWindow` | `20` | Maximum messages allowed within the sliding window. Set to `0` to disable. |
| `RateLimitWindow` | `00:01:00` (1 minute) | The sliding window duration. |
| `MaxAnonymousSessionsPerWindow` | `5` | Maximum anonymous sessions allowed within the session-start window. Set to `0` to disable. |
| `AnonymousSessionRateLimitWindow` | `00:10:00` (10 minutes) | The anonymous session-start window duration. |

The default limiter behavior is privacy-first but configurable through `AIChatRateLimitingOptions`:

| Option | Default | What it does |
| --- | --- | --- |
| `AuthenticatedMessagePartitions` | `AuthenticatedUser` | Keys authenticated message throttles by the signed-in user by default |
| `AnonymousMessagePartitions` | `Visitor, NetworkAddress, Session, Connection` | Keys anonymous message throttles by stable visitor ID, then the configured network-address representation, then session/connection fallbacks |
| `AnonymousSessionStartPartitions` | `Visitor, NetworkAddress` | Keys anonymous session-start throttles by stable visitor ID plus the configured network-address representation |

### Per-profile override

Anti-spam throttling can be customized per AI Profile (or AI Profile-source template) using `PromptSecurityProfileSettings`. Any field left `null` falls back to the site-level default:

```csharp
profile.WithSettings(new PromptSecurityProfileSettings
{
    MaxMessagesPerWindow = 10,
    RateLimitWindow = TimeSpan.FromMinutes(2),
    MaxAnonymousSessionsPerWindow = 3,
    AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(10),
});
```

Both the message throttle (`DefaultChatRateLimiter`) and the anonymous session-start throttle (`DefaultChatSessionStartRateLimiter`) honor these overrides, falling back to the site defaults on `PromptSecurityOptions` when a value is not set. Set `MaxMessagesPerWindow` (or `MaxAnonymousSessionsPerWindow`) to `0` on a profile to disable that throttle for the profile even when site-level rate limiting is enabled.

### How it works

1. The message rate limiter resolves one or more tracking keys from `AIChatRateLimitingOptions`. By default, authenticated requests use the user identity, and anonymous requests use the stable visitor ID plus the configured network-address representation, with session and connection fallbacks.
2. A sliding window queue tracks timestamp entries per key.
3. Expired entries (outside the window) are evicted on each evaluation.
4. If the count equals or exceeds the maximum, a `Blocked` result is returned with `rule-id = "rate-limit"` and a `RetryAfterSeconds` hint.
5. Audit logging records rate limit blocks alongside other security events.

Anonymous session creation is protected separately:

1. Sample hosts issue a long-lived first-party visitor cookie for anonymous browsers.
2. New anonymous session starts are throttled in two layers:
   - an ASP.NET Core endpoint rate-limit policy on explicit session-start routes
   - a hub-level anonymous session-start limiter that uses the same visitor key and configured network-address partition
3. Remote-address handling is configurable:
   - `Disabled` stores nothing
   - `Hashed` stores only a salted hash for privacy-first abuse partitioning
   - `PlainText` stores the raw address for operator-managed blocklists, allowlists, or investigations
   - `Encrypted` stores a protected address value for at-rest confidentiality and still records a salted hash for rate limiting and abuse partitioning

### Replacing the default implementation

Register a custom `IChatRateLimiter` implementation in DI to use Redis, a database, or another distributed state store for multi-instance deployments.

You can also keep the built-in implementation and change its partitioning strategy through the options pattern:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddChatInteractions(chat => chat
            .ConfigureVisitorIdentity(options =>
            {
                options.RemoteAddressMode = AIVisitorRemoteAddressMode.Hashed;
            })
            .ConfigureChatRateLimiting(options =>
            {
                options.AnonymousMessagePartitions =
                    ChatRateLimitPartition.Visitor |
                    ChatRateLimitPartition.NetworkAddress;

                options.AnonymousSessionStartPartitions = ChatRateLimitPartition.NetworkAddress;
            })
        )
    )
);
```

You can change the rate-limit thresholds themselves through `PromptSecurityOptions`:

```csharp
builder.Services.Configure<PromptSecurityOptions>(options =>
{
    options.MaxMessagesPerWindow = 12;
    options.RateLimitWindow = TimeSpan.FromMinutes(2);
    options.MaxAnonymousSessionsPerWindow = 3;
    options.AnonymousSessionRateLimitWindow = TimeSpan.FromMinutes(15);
});
```

If you want to keep CrestApps' endpoint wiring but supply your own ASP.NET Core rate-limit policy, disable the built-in policy registration and point the chat endpoints at your policy name:

```csharp
builder.Services.Configure<AIChatEndpointRateLimitingOptions>(options =>
{
    options.RegisterAnonymousSessionStartPolicy = false;
    options.AnonymousSessionStartPolicyName = "MyCustomChatPolicy";
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("MyCustomChatPolicy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});
```

That override replaces only the ASP.NET Core endpoint/hub layer. The framework's chat-aware `IChatRateLimiter` still protects prompt traffic unless you replace that service too.

## Configuration example

```json
{
  "CrestApps": {
    "AI": {
      "PromptSecurity": {
        "EnableInjectionDetection": true,
        "EnableOutputFiltering": true,
        "EnableSecurityPreamble": true,
        "EnableInputDelimiters": true,
        "EnableAuditLogging": true,
        "MaxPromptLength": 8000,
        "BlockingThreshold": "High",
        "MaxMessagesPerWindow": 20,
        "RateLimitWindow": "00:01:00",
        "MaxAnonymousSessionsPerWindow": 5,
        "AnonymousSessionRateLimitWindow": "00:10:00",
        "LowRiskScoreThreshold": 10,
        "MediumRiskScoreThreshold": 20,
        "HighRiskScoreThreshold": 35,
        "CriticalRiskScoreThreshold": 50,
        "CustomBlockedPatterns": [
          "secret\\\\s+code\\\\s+alpha"
        ]
      }
    }
  }
}
```

## Extending with custom rules

The security layer is designed to be extensible. You can add custom detection rules without modifying any built-in code by implementing `IPromptSecurityRule` and registering it in DI.

### `IPromptSecurityRule` interface

```csharp
public interface IPromptSecurityRule
{
    /// <summary>
    /// A unique identifier for the rule (e.g., "my-custom-canary-detection").
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Evaluates the rule against the normalized prompt.
    /// Return null if the rule does not match; otherwise return a PromptSecurityRuleResult.
    /// </summary>
    ValueTask<PromptSecurityRuleResult> EvaluateAsync(
        PromptSecurityEvaluationContext context,
        CancellationToken cancellationToken = default);
}
```

### Writing a custom rule

```csharp
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Security;

public sealed partial class InternalCanaryRule : IPromptSecurityRule
{
    public string RuleId => "internal-canary-detection";

    public ValueTask<PromptSecurityRuleResult> EvaluateAsync(
        PromptSecurityEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.NormalizedInput))
        {
            return ValueTask.FromResult<PromptSecurityRuleResult>(null);
        }

        if (CanaryRegex().IsMatch(context.NormalizedInput))
        {
            return ValueTask.FromResult(new PromptSecurityRuleResult
            {
                RuleId = RuleId,
                Categories = ["data-exfiltration"],
                Severity = PromptRiskLevel.Critical,
                Score = 40,
                Reason = "Detected internal canary token in prompt.",
                Metadata = new Dictionary<string, string>
                {
                    ["ruleType"] = "custom-regex",
                },
            });
        }

        return ValueTask.FromResult<PromptSecurityRuleResult>(null);
    }

    [GeneratedRegex(
        @"\b(?:CANARY_TOKEN_ABC123|internal-secret-marker)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex CanaryRegex();
}
```

### Registering a custom rule

Add your rule to the DI container as an `IPromptSecurityRule`. Use `TryAddEnumerable` to coexist with the built-in rules:

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IPromptSecurityRule, InternalCanaryRule>());
```

Your rule will be automatically picked up by the `PromptInjectionPatternDetector` on the next evaluation. The scoring engine aggregates your rule's score alongside the built-in rules.

### `PromptSecurityEvaluationContext` fields

| Field | Description |
| --- | --- |
| `OriginalInput` | The raw user input before any normalization. |
| `NormalizedInput` | After Unicode normalization, zero-width removal, whitespace collapse. |
| `FoldedInput` | After homoglyph folding (Cyrillic/Greek lookalike replacement). |
| `MaxPromptLength` | The effective max length for the current evaluation. |
| `BlockingThreshold` | The effective blocking threshold. |
| `Telemetry` | Normalization metadata (e.g., whether invisible chars were removed). |

### `PromptSecurityRuleResult` fields

| Field | Description |
| --- | --- |
| `RuleId` | Unique identifier matching the rule. |
| `Categories` | One or more category tags (used for telemetry and filtering). |
| `Severity` | The assessed risk level for this match. |
| `Score` | The numeric weight contributed to the aggregate score. |
| `Reason` | Human-readable explanation for the match. |
| `Metadata` | Additional key-value metadata describing the match. |
| `MatchedOnFoldedInput` | Whether the match required homoglyph folding to succeed. |

### Best practices for custom rules

1. **Always set a regex timeout** to prevent catastrophic backtracking.
2. **Evaluate against `NormalizedInput`** first. Only fall back to `FoldedInput` if you expect homoglyph attacks.
3. **Choose an appropriate score.** Low confidence rules (score 8–12) contribute to aggregate detection. High confidence rules (score 20+) can trigger a block on their own when they exceed the threshold.
4. **Use category tags** to classify your detection for telemetry and filtering.
5. **Return `null`** when the rule does not match — this signals the engine to continue.

## Security preamble

The security preamble is a hardened system prompt prepended before any profile-defined system prompt. It instructs the model to:

- Reject persona manipulation (DAN, Anti-Assistant, developer mode)
- Refuse to reveal system instructions, tools, or configuration
- Resist true/false games and character-by-character extraction
- Handle sensitive user data with care
- Respond neutrally to manipulation without explaining the security mechanism

The preamble is wrapped in `[SECURITY DIRECTIVES — IMMUTABLE AND NON-NEGOTIABLE]` / `[END SECURITY DIRECTIVES]` delimiters. These markers are only used for the security preamble. Other system prompt templates (RAG instructions, memory context, etc.) intentionally do **not** include their own security delimiters because:

- The security preamble is always the **first** content the model sees, establishing authority.
- Adding identical delimiters to every template would dilute their significance and create confusion.
- Operational templates inherit their protection from the preamble; they don't need to restate it.

## Input delimiters

User messages are wrapped with `<|user_input_begin|>` / `<|user_input_end|>` delimiters. An instruction appended to the system prompt tells the model to treat all content within these delimiters as untrusted user input that must never be interpreted as instructions. The framework also sanitizes user input by stripping any injected delimiter tokens before wrapping.

## Security rationale for the major design choices

### Normalization before regex evaluation

Attackers frequently try to break literal-pattern checks with zero-width characters, homoglyph substitutions, or whitespace abuse. Normalizing first makes the regex rules more resilient without needing to duplicate every pattern for every obfuscation variant.

### Weighted scoring instead of first-match blocking

A first-match model is brittle in both directions:

- it can **miss** attacks that distribute intent across several weaker signals
- it can **overreact** to one ambiguous phrase without considering surrounding indicators

Weighted scoring improves both detection quality and operator explainability.

### Rule metadata and telemetry

Operators need to know **why** a prompt was blocked. Returning matched rule IDs, categories, scores, and normalization telemetry makes the detector auditable without logging raw prompt fragments into application logs.

### Regex-only design

This layer deliberately avoids classifier dependencies and external security services. That keeps it:

- deployable in offline or regulated environments
- deterministic under test
- transparent for debugging and audit review

## Remaining limitations

Regex-based prompt security is valuable, but it is not complete protection.

### Weaknesses that still remain

1. **Novel semantic jailbreaks** can avoid known keyword patterns while still steering model behavior.
2. **Benign-looking multi-turn attacks** can distribute intent across many turns so no single prompt looks strongly malicious.
3. **Domain-specific phrasing** may slip through until you add tenant-specific custom patterns.
4. **Legitimate operator requests** can still look suspicious if they ask about tools, prompts, or configuration in a genuinely administrative workflow.
5. **Non-text channels** such as malicious documents or images still need separate validation and retrieval safeguards.

### Bypasses that are still hard for regex to catch

- subtle paraphrases that avoid obvious extraction verbs
- long-form social engineering that gradually builds trust before asking for disclosure
- attacks that rely on model reasoning quirks rather than explicit lexical patterns
- multi-message decomposition where each message appears individually harmless
- encoded or transformed instructions split across many turns or attachments

Because of those limits, keep this detector paired with:

- least-privilege tool authorization
- strong system prompts and boundaries
- output filtering
- retrieval hardening
- tenant-aware auditing and monitoring

## File upload security scanning

CrestApps.Core provides an extensibility point for scanning uploaded files (documents and images) before they are stored or processed. This helps protect against malicious file uploads in AI chat sessions.

### How it works

All file uploads (via both `UploadChatSessionDocument` and `UploadChatInteractionDocument` endpoints) pass through `IUploadedFileScanner.ScanAsync()` **before** any storage or processing occurs. If the scan returns anything other than `Clean`, the upload is rejected immediately.

### Default behavior

The framework ships with `NoOpUploadedFileScanner`, which always returns `Clean`. This means uploads are unrestricted by default — the infrastructure is in place for consumers to add real scanning without modifying framework code.

### Implementing a custom scanner

Create a class that implements `IUploadedFileScanner`:

```csharp
using CrestApps.Core.AI.Documents;
using Microsoft.AspNetCore.Http;

public sealed class ClamAvFileScanner : IUploadedFileScanner
{
    private readonly IClamAvClient _client;

    public ClamAvFileScanner(IClamAvClient client)
    {
        _client = client;
    }

    public async Task<FileScanResult> ScanAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var result = await _client.ScanAsync(stream, cancellationToken);

            if (result.IsInfected)
            {
                return FileScanResult.Infected(
                    result.VirusName,
                    $"ClamAV detected: {result.VirusName}");
            }

            return FileScanResult.Clean;
        }
        catch (Exception ex)
        {
            // Fail-closed: treat scanner errors as unsafe.
            return FileScanResult.Error($"Scan failed: {ex.Message}");
        }
    }
}
```

### Registering a custom scanner

Replace the default no-op scanner in your DI configuration:

```csharp
services.AddSingleton<IUploadedFileScanner, ClamAvFileScanner>();
```

Because the framework uses `TryAddSingleton`, registering your implementation **before** calling `AddCoreAIDocumentProcessing()` ensures your scanner takes precedence.

### `FileScanResult` states

| Status | `IsSafe` | Meaning |
|--------|----------|---------|
| `Clean` | `true` | No threats detected; upload proceeds |
| `Infected` | `false` | Malicious content detected; upload rejected |
| `Error` | `false` | Scan failed (timeout, unavailable); upload rejected |

### Fail-closed vs fail-open

The default behavior is **fail-closed**: if the scanner returns `Error`, the upload is rejected. This is the safest default for production deployments. If you need fail-open behavior (allow uploads when the scanner is unavailable), implement that logic in your custom scanner by returning `FileScanResult.Clean` on error conditions.

### Best practices

- **Scan before storage** — the framework guarantees the scan runs before any file touches disk or object storage.
- **Keep scans fast** — uploads block on scan completion. Consider async queuing for large files if latency is critical.
- **Log rejections** — the framework logs rejected uploads at `Warning` level with file name and threat details.
- **Test with EICAR** — use the EICAR test file to verify your scanner integration without real malware.

## Related docs

- [AI Core](./ai-core.md)
- [AI Profiles](./ai-profiles.md)
- [Chat Interactions](./chat.md)
