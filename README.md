# Workbench Connect

## Secure branch-only automation

This repository uses GitHub Actions with a single trusted path:

- direct push to `main` / `master` for CI
- direct push to `main` / `master` or version tag (`v*`) for release pipeline
- no `pull_request` trigger

### Required repository settings

1. Protect `main`/`master` branch and restrict push permission to trusted maintainers only.
2. Create a protected environment named `steam-production` with required reviewers.
3. Put deployment secrets in that environment (not repository-wide):
   - `STEAM_USERNAME`
   - `STEAM_PASSWORD`
   - `STEAM_TOTP_SECRET`
4. Optionally set default workflow token permissions to `Read repository contents`.
