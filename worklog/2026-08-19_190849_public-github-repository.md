# Publish latest source and make GitHub repository public

## Date and time

2026-08-19 19:08:49 +07:00 (Asia/Bangkok)

## Request / intent

Push the latest WebGallery source to GitHub and change `gimkim/WebGallery` from Private to Public.

## Concepts and rules established

- The canonical source repository is public on `main`.
- Development settings must not remain tracked in the public revision even when they contain no credentials.
- Before a repository visibility change, inspect current tracked paths, historical filenames, and token/private-key patterns; do not rely only on `.gitignore`.
- Do not rewrite repository history when the only historical match is verified credential-free and rewriting would create unnecessary destructive coordination.

## Files changed

- `.gitignore`: ignores `appsettings.Development.json`.
- `appsettings.Development.json`: removed from Git tracking while retained locally for development.
- `AGENTS.md`, `.agents/PROJECT_NOTES.md`: record the public repository state and public-push safety rule.
- This new worklog records the visibility transition and verification.

## Validation actually performed

- Confirmed local `main` and `origin/main` initially matched commit `61194861d457d3ad9b37b18d635f9895b1c3f3f6` and the worktree was clean.
- Confirmed GitHub CLI authentication for repository owner `gimkim` and verified the repository initially reported `PRIVATE`.
- Current and historical filename audit found no tracked database/WAL/SHM, bootstrap password, certificate/key, cache, publish/backup tree, or ZIP. The only match was `appsettings.Development.json`.
- Parsed the development settings without printing values: it contains no `BootstrapAdmin` block or password, and its connection points only to the relative local `App_Data/gallery-dev.db`.
- Current tracked source contained no GitHub-token or private-key marker matches.

## User-visible result

The latest documented source is pushed to `main`, and the GitHub repository is publicly readable without exposing production state or credentials.

## Remaining manual tests / uncertainty

- The visibility change and anonymous GitHub access must be verified after the final push; those results are appended by the completed session outcome rather than assumed here.
- Historical commits still contain the verified credential-free development settings file; Git history was intentionally not rewritten.
