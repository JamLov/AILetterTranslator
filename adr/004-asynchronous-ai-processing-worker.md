# Architecture Decision Record 004: Asynchronous AI Processing via Isolated Background Worker

## Status
Accepted

## Context
The core functionality of this application involves taking uploaded images (JPG) of handwritten letters, passing them to the Google Gemini Pro Vision API alongside contextual notes, and returning a transcription and translation. 

The API call to Gemini can be slow (several seconds to over a minute), is susceptible to rate limiting, and can experience transient network failures. Handling this processing synchronously within the .NET Web API request lifecycle would:
1. Tie up server threads, degrading overall application performance.
2. Result in poor user experience, forcing the user to wait on a loading screen.
3. Make it difficult to implement robust retry logic (e.g., exponential backoff) without dropping HTTP connections.

## Decision
We will decouple the AI translation logic into a dedicated, scheduled **Background Worker agent**.
- The .NET Web API will only be responsible for handling authentication, accepting file uploads, and writing initial state data to disk. It will return a rapid "Success" response to the user.
- A completely separate Docker container will host the Background Worker process.
- This worker will poll for new jobs, handle the interaction with the Gemini API, and write the final results back to storage.

## Consequences
- **Positive:** The web frontend remains fast and responsive.
- **Positive:** We can easily implement robust retry policies (using libraries like Polly in .NET) inside the worker for handling API rate limits without worrying about HTTP timeout issues.
- **Positive:** The worker can be scaled independently of the Web API if the job queue grows.
- **Negative:** Adds architectural complexity by introducing a secondary process/container to manage and deploy.
