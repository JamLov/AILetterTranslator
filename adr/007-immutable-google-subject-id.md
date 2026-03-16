# Architecture Decision Record 007: Immutable Google Subject ID for Directory Structuring

## Status
Accepted

## Context
To ensure data privacy and isolated file storage (as per the functional specification), every user must have their own dedicated root directory within the shared `/data` volume.

We need a unique identifier to name these directories. While the user's email address is readily available from the Google JWT, using it as a directory name presents several issues:
1. Email addresses contain characters (like `@`) that can sometimes cause issues in complex URL routing or certain file systems.
2. Email addresses can theoretically change (e.g., name changes, domain migrations). If an email changes, their data folder link breaks.
3. Storing email addresses in plain text as folder names exposes Personally Identifiable Information (PII) unnecessarily within the infrastructure.

## Decision
We will extract and use the **Google Subject ID (`sub` claim)** from the OAuth token to generate the root directory for each user (e.g., `/data/{google_subject_id}/`).

The `sub` claim is a unique, immutable identifier assigned by Google to every account. It consists only of alphanumeric characters.

## Consequences
- **Positive:** The folder structure is completely stable. If a user changes their email address, their data remains accessible and correctly linked to their Google account.
- **Positive:** Removes PII from the filesystem structure, improving privacy.
- **Positive:** The `sub` claim is guaranteed to be filesystem-safe.
- **Negative:** When browsing the filesystem manually for debugging, it is difficult to know which folder belongs to which user without cross-referencing a database or logs.
