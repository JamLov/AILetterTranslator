# Architecture Decision Record 006: Whitelist-Based User Authorization

## Status
Accepted

## Context
The application utilizes Google Sign-In for authentication (ADR 003). While this securely verifies *who* the user is, it does not dictate who is *allowed* to use the application. If left unrestricted, anyone with a valid Google account could log in, upload files, and incur costs against the Gemini Pro API.

Because this is a personal/private tool, we need a mechanism to explicitly restrict access to a small, known set of individuals.

## Decision
We will implement authorization by validating the Google JWT and cross-referencing the extracted email address against a **hardcoded whitelist**.

- A list of `AllowedUsers` (email addresses) will be maintained in the application's configuration (e.g., `appsettings.json`, environment variables).
- During the login flow and subsequent secure API requests, a custom `IUserService` will extract the `email` claim from the provided JWT.
- If the email is missing or not present in the `AllowedUsers` array (case-insensitive check), the API will immediately reject the request with a `403 Forbidden` HTTP status code.

## Consequences
- **Positive:** Highly secure and effectively prevents unwanted public usage and API cost overruns.
- **Positive:** Simple to implement and maintain for a small number of users. Requires no database tables or complex role-based access control (RBAC) UI.
- **Negative:** Adding a new user requires modifying configuration and restarting the application (or relying on dynamic config reloading). It is not suitable if the user base grows large or requires self-service registration.
