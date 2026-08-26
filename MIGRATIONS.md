# Migrations

Breaking changes that require manual action when upgrading an existing deployment. Entries are added when a change lands, in descending order (newest first). Non-breaking changes aren't listed here — see the [GitHub Releases](https://github.com/christt105/cinegram/releases) for the full changelog.

## v2.0.0

`IMPORT_MOVIES_DIR` and `IMPORT_SHOWS_DIR` no longer exist. `bot-net` now mounts a single `MEDIA_ROOT` directory (with `movies`/`shows` as subfolders of it) so moves within the library stay on one filesystem instead of silently falling back to a slow copy.

**To upgrade:**

1. If your movies and shows directories don't already share a parent directory on the host, move one under the other first.
2. In `.env`, replace:
   ```
   IMPORT_MOVIES_DIR=/path/to/movies
   IMPORT_SHOWS_DIR=/path/to/shows
   ```
   with:
   ```
   MEDIA_ROOT=/path/to/media
   MOVIES_SUBDIR=movies
   SHOWS_SUBDIR=shows
   ```
   `MOVIES_SUBDIR`/`SHOWS_SUBDIR` default to `movies`/`shows`, so you only need to set them if your subfolder names differ.
3. Pull the new `docker-compose.yml` (the `bot-net` volume mount changed from two binds to one) and run `docker compose up -d`.
