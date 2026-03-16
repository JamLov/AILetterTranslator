# ADR 003: Use Google Project for Authentication (Log In With Google)

## Status
Accepted

## Context

To provide a seamless authentication experience and leverage widely-used identity providers, the project will be set up as a Google Cloud Project. This will enable the use of "Log In With Google" for user authentication. Integrating Google Sign-In offers the following benefits:
- Simplifies user onboarding and login
- Reduces friction by allowing users to authenticate with existing Google accounts
- Provides secure, standards-based OAuth 2.0 authentication
- Enables access to additional Google APIs if needed in the future

## Decision

- The application will be registered as a project in the Google Cloud Console.
- OAuth 2.0 credentials will be created for the application (Web client ID and secret).
- The frontend will implement "Log In With Google" using the official Google Identity Services SDK.
- The backend will verify Google ID tokens and manage user sessions securely.
- User data will be stored and associated with their Google account ID.
- The system will be designed to allow for future addition of other authentication providers if needed.

## Consequences

- Users will be able to log in using their Google accounts.
- The project will require configuration and management in the Google Cloud Console.
- The application must comply with Google API Services User Data Policy and OAuth branding requirements.
- Additional setup and documentation will be required for local development and deployment to ensure correct OAuth redirect URIs and credentials.

