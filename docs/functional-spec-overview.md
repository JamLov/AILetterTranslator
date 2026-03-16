# Functional Specification: Letter Translation Application

## 1. Overview
The Letter Translation Application is a web-based platform designed to help users translate handwritten letters and historical documents between multiple languages. The application provides a secure, user-friendly interface for uploading, translating, and managing documents. Authentication is handled via Google Sign-In. 

A core component of the system is an autonomous, scheduled AI Agent powered by Gemini Pro, which asynchronously processes user uploads, transcribes the handwritten content, and translates it into English.

## 2. Goals and Objectives
- Enable users to securely log in using their Google accounts.
- Allow users to upload document images (JPG) and provide contextual notes.
- Provide accurate, high-quality transcriptions and translations using Google's Gemini Pro API.
- Process documents asynchronously via a background worker to ensure the web UI remains responsive.
- Allow users to view, download, and manage their translated documents.
- Ensure data privacy and isolated file storage per user.

## 3. System Architecture
The application is composed of three main architectural components, containerized using Docker:

1. **Frontend (Vue.js)**: The user-facing web application.
2. **Backend API (.NET 10)**: Handles authentication, job creation, file uploads, and serves job status/results to the frontend.
3. **Translation Agent (Background Worker)**: A scheduled process running in a separate Docker container that polls for pending jobs and interacts with the Gemini Pro API.
4. **Data Volume**: A shared Docker volume (`/data`) where all user files and metadata are stored. Both the Backend API and Translation Agent mount this volume.

## 4. User Roles
- **Authenticated User**: Can log in, upload documents, request translations, view/download results, and manage their own documents.
- **Admin** (Future): Can manage users, monitor usage, and access system analytics.

## 5. Data & Folder Structure
Data is managed entirely on the filesystem within a shared volume, acting as both storage and a simple file-based queuing system.

**Path Template**: `/data/{user_google_id}/data/{job_guid}/`

Within each Job Folder:
- `files/` - Directory containing the uploaded images (e.g., `page1.jpg`, `page2.jpg`).
- `notes.txt` - Plain text file containing the user's contextual notes.
- `metadata.json` - Tracks the job's state and details.
- `Transcribed.md` - (Generated) The original language transcription.
- `Transcribed_Translated.md` - (Generated) The English translation.
- `Transcribed_Translated_With_Notes.md` - (Generated) The English translation enriched with the user's contextual notes.

### 5.1 `metadata.json` Schema
```json
{
  "jobId": "uuid",
  "jobName": "Letter from 1946",
  "createdAt": "2026-03-15T10:00:00Z",
  "status": "Not Started" | "In Progress" | "Finished" | "Failed",
  "errorMessage": null,
  "originalFileCount": 2
}
```

## 6. Functional Requirements

### 6.1 Authentication
- Users must log in using Google Sign-In (OAuth 2.0).
- Backend validates the Google JWT and establishes a secure session.
- User data folders are strictly segregated by the Google User ID.

### 6.2 Job Creation & Upload
- Users can create a "New Job" by providing a Job Name, uploading multiple JPG files, and entering optional contextual notes.
- Backend API creates the folder structure, saves the files, writes `notes.txt`, and initializes `metadata.json` with status `"Not Started"`.

### 6.3 Background Translation Agent (Gemini Pro)
The Translation Agent is an isolated worker process responsible for the heavy lifting.

- **Scheduling**: The agent runs on a defined schedule (e.g., every 1-5 minutes) or as a continuous polling loop.
- **Discovery**: It scans the `/data` directory recursively to find any `metadata.json` files where `"status": "Not Started"`.
- **Locking**: Upon picking up a job, it immediately updates the status to `"In Progress"` to prevent race conditions if multiple worker instances are running.
- **Processing Flow**:
  1. Reads `notes.txt` and gathers all images from the `files/` directory.
  2. Constructs a prompt incorporating the system brief, the user's notes, and the images.
  3. Submits the payload to the **Gemini Pro Vision API**.
  4. Parses the AI response to extract the transcription and translations.
- **Output**: Writes the three required Markdown files (`Transcribed.md`, `Transcribed_Translated.md`, `Transcribed_Translated_With_Notes.md`) to the job folder.
- **Completion**: Updates `metadata.json` status to `"Finished"`.
- **Error Handling**: If the API fails, rate limits occur, or files are invalid, the agent updates the status to `"Failed"` and populates the `"errorMessage"` field. It may implement exponential backoff for transient API errors.

### 6.4 Job Management & Viewing
- **My Jobs List**: The frontend polls or fetches the list of the user's job folders, reading the `metadata.json` to display the current status of each job.
- **View Job**: Once a job is `"Finished"`, the user can click into it to view the uploaded images alongside the generated Markdown files, which the frontend will render as HTML.
- **Deletion**: Users can delete a job, which prompts the Backend API to completely remove the corresponding `{job_guid}` directory.

## 7. Non-Functional Requirements
- **Scalability**: Decoupling the Translation Agent into its own container allows for scaling the worker processes independently of the web API.
- **Resilience**: The file-based queue ensures that if the agent crashes, jobs remain in `"Not Started"` or `"In Progress"` state and can be picked up again or reset.
- **Security**: Strict path validation must be enforced in the Backend API to prevent Directory Traversal attacks (ensuring a user can only access `/data/{their_id}/`).

## 8. User Flows

### 8.1 New Job Flow
1. User logs in via Google.
2. Navigates to "New Job".
3. Uploads images, sets a name, and adds contextual notes (e.g., "This was a letter from my father while he was travelling to Indonesia in 1946").
4. Submits the form. The Backend responds with success.
5. User is redirected to "My Jobs" where the new job appears as "Not Started".

### 8.2 Background Processing Flow (Invisible to User)
1. Agent wakes up and finds the new job's `metadata.json`.
2. Agent marks it "In Progress".
3. Agent sends images and notes to Gemini Pro.
4. Agent writes resulting `.md` files to the folder.
5. Agent marks job as "Finished".

### 8.3 View Results Flow
1. User sees the job marked as "Finished" on the dashboard.
2. User clicks the job to view details.
3. The UI presents a split view or tabbed interface showing the original images alongside the Transcribed text, Translated text, and the Contextualized Translation.

## 9. Future Enhancements
- Integration with an actual message broker (RabbitMQ/Redis) if the file-based queue becomes a bottleneck.
- Support for PDF uploads (with preprocessing to split into images).
- Email notifications when a background job finishes.
- Admin dashboard for monitoring system health, API usage, and failed jobs.

## 10. Open Questions
- What are the exact file size constraints for the image uploads, keeping Gemini API limits in mind?
- How should we structure the Gemini prompt to reliably output the three distinct text variations? (e.g., Requesting a structured JSON response from Gemini containing the three Markdown strings).
- What is the data retention policy for these sensitive letters?