# Record Source and IIS Deployment Scope

- Date: 2026-08-07 10:06:21 +07:00
- Session intent: Record the source workspace, IIS deployment target, and public application URL as durable project rules.

## Request

The user specified that source-code work belongs in `C:\Users\tatsa\source\webgallery`, while deployment belongs in `C:\Web\imagegallery`. The deployment directory is already configured as an IIS application and is exposed at `https://gimgim.ddns.net:45570/Gallery`.

## Concepts and rules established

- Keep the source-code and IIS deployment directories separate.
- Perform development and project documentation work in the source workspace.
- Copy intended deployable output to the IIS deployment target only as an explicit deployment operation.
- Track source validation, deployed-file validation, and public browser/request validation separately.

## Files changed

- Updated `AGENTS.md` with the source, deployment, URL, and separation rules.
- Updated `.agents/PROJECT_NOTES.md` with the workspace and deployment model.
- Added this worklog.

## Validation performed

- Confirmed that both `C:\Users\tatsa\source\webgallery` and `C:\Web\imagegallery` exist locally.
- Re-read the current agent notes, worklog convention, and newest prior worklog before editing.
- Reviewed the documented paths and URL against the values supplied by the user.

## User-visible result

Future sessions can now identify the correct development workspace, IIS deployment target, and public application endpoint without conflating source edits with deployment.

## Remaining manual tests or uncertainty

- The IIS configuration and application mapping were not inspected in this documentation-only session.
- The public URL was recorded from the user's instruction but was not opened or tested in this session.
- No application files were deployed or changed under `C:\Web\imagegallery`.

