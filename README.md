# Workbench Connect

## Secure branch-only automation

This repository uses GitHub Actions with a single trusted path:

- direct push to `main` / `master` for CI
- direct push to `main` / `master` or version tag (`v*`) for release pipeline
- release/deploy workflow (`release.yml`) is **manually triggered** via `workflow_dispatch`
- no `pull_request` trigger

### How to release

1. Merge changes into `main`/`master`.
2. Go to **Actions → Release and Deploy → Run workflow**.
3. Enter the version number (e.g. `1.1.0`) and click **Run workflow**.
4. The workflow will build the DLL, create a GitHub release, and upload to Steam Workshop.

1. Protect `main`/`master` branch and restrict push permission to trusted maintainers only.
2. Create a protected environment named `steam-production` with required reviewers.
3. Put deployment secrets in that environment (not repository-wide):
   - `STEAM_USERNAME`
   - `STEAM_PASSWORD`
   - `STEAM_TOTP_SECRET`
4. Optionally set default workflow token permissions to `Read repository contents`.
