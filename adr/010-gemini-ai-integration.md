# Architecture Decision Record 010: Google Gemini AI Integration and Prompt Design

## Status
Accepted

## Context
The core value proposition of the application is transcribing and translating handwritten letters using AI. We needed to select an AI provider and design a prompt strategy that reliably produces structured, parseable output containing three distinct sections: transcription, translation, and contextually annotated translation.

## Decision

### SDK Selection
We will use the **official Google GenAI SDK** (`Google.GenAI` NuGet package from `googleapis/dotnet-genai`) to interact with the Gemini API. An unofficial community package (`Google_GenerativeAI` by gunpal5) was considered but rejected in favour of the officially maintained SDK linked from Google Cloud documentation.

### Model
The default model is `gemini-2.5-pro`, configurable via `Gemini:Model` in application settings. This model supports multimodal input (text + images) which is essential for processing letter images.

### Prompt Strategy
A single request is sent containing:
1. All uploaded images as inline binary parts (with MIME type detection from file extension).
2. A structured text prompt requesting three outputs separated by the delimiter `---SECTION_BREAK---`.
3. Optional user-provided notes appended as additional context.

The worker parses the response by splitting on the delimiter, producing three markdown strings. If fewer than three sections are returned, fallback text is used and a warning is logged.

### API Key Management
The Gemini API key is provided via configuration (`Gemini:ApiKey`), loaded from environment variables in Docker or .NET user-secrets during local development. The service validates the key at runtime and throws `InvalidOperationException` if missing.

## Consequences
- **Positive:** Using the official SDK ensures long-term support and compatibility with API changes.
- **Positive:** The delimiter-based parsing is simple and reliable. The model consistently follows structured output instructions.
- **Positive:** Multimodal support means we can send multiple page images in a single request for multi-page letters.
- **Negative:** The prompt is baked into the service code. If prompt tuning is needed, it requires a code change and redeployment.
- **Negative:** No retry logic is currently implemented for transient Gemini API failures (a future enhancement).
