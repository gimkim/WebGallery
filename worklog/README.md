# Worklog Convention

The `worklog` directory contains one append-only Markdown file for each substantive project editing session.

## File naming

Use:

`YYYY-MM-DD_HHmmss_short-description.md`

The timestamp uses the local project time zone (`Asia/Bangkok`, UTC+07:00) and records when the session entry is created.

## Required content

Each worklog should include:

- Date and time, including time zone
- User request or session intent
- Concepts or rules established
- Files changed
- Validation actually performed
- User-visible result
- Remaining manual tests, uncertainty, or follow-up work

## History rules

- Create a new file for every substantive editing session.
- Do not silently edit old entries or use one rolling log file.
- If an older entry is wrong, create a new timestamped correction that links to or names the old entry.
- Do not describe build, browser, UI, deployment, or release validation unless it was actually performed.

