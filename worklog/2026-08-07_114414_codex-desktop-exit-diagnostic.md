# Codex desktop repeated-exit diagnostic

- Date: 2026-08-07 11:44:14 +07:00 (Asia/Bangkok)

## Request and intent

- Investigate why the Codex desktop application disappeared twice while this project was being edited.
- Determine whether the project/build caused it and apply any safe workaround within the agent's control.

## Evidence inspected

- Installed AppX package and active process versions/start times.
- Windows Application, AppModel-Runtime, TWinUI, AppX deployment, Windows Error Reporting, Reliability Monitor, and local crash-dump locations.
- Codex desktop logs under the packaged application's `LocalCache\Local\Codex\Logs\2026\08\07` directory.
- Current Sentry breadcrumb/session state, filtered for process, renderer, browser, error, crash, and memory signals.
- Official OpenAI troubleshooting guidance for app logs and session diagnostics.

## Findings

- Windows Store updated Codex from `26.730.8199.0` to `26.803.5235.0` at 10:00:05.
- First affected process `35460` ended at 11:28:48. Its final log records the last In-app Browser tab being finalized/closed at 11:28:47. The app relaunched as process `15432` at 11:28:51.
- Second process `15432` ended at 11:35:05. Immediately beforehand its log contains repeated `Node with given id does not belong to the document` browser errors, repeated `ResizeObserver loop completed with undelivered notifications`, and finalization/closure of the final In-app Browser tab at 11:35:04. The app relaunched as process `46456` at 11:35:07.
- No Codex `Application Error`, WER reliability failure, crash report, or local dump was recorded. The process/container boundary shows normal destruction rather than a recorded native fault or OOM kill.
- ASP.NET errors from the Gallery appeared separately in Event Viewer and did not coincide with or terminate the Codex process.
- The strongest supported cause is a Codex `26.803.5235.0` In-app Browser lifecycle regression, specifically closing/finalizing the last controlled browser tab. This is a high-confidence correlation, not source-level proof because the packaged app's internal crash telemetry does not include a terminal exception.

## Safe mitigation applied

- Added a project rule to avoid In-app Browser automation and `tabs.finalize` on Codex `26.803.5235.0`.
- Future validation will prefer HTTP/static/non-UI checks and will report real UI checks as manual unless the user explicitly chooses an external browser surface.
- No Codex cache, authentication state, sessions, or package installation was reset or deleted.

## Files changed

- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Remaining uncertainty and follow-up

- A later Codex app release should be checked and the In-app Browser workaround deliberately revalidated before removal.
- If the app exits again without In-app Browser use, collect the newest desktop log and exact timestamp; that would falsify or broaden the current diagnosis.
- A repair/reset/reinstall could be attempted only with explicit approval because it can affect local application state and the current evidence points to a version-specific lifecycle bug rather than corrupted project files.
