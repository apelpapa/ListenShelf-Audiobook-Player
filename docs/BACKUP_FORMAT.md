# ListenShelf local backup format

ListenShelf exports one portable file with the extension
`.listenshelf-backup`. The file is a ZIP-compatible container so it can be
inspected with standard archive tools when necessary, but users should restore
it through ListenShelf so every integrity and path-safety check is applied.

## Version 1 contents

- `manifest.json` identifies the format version, creation time, application
  version, cataloged books, archived directories, and every stored file.
- `data/listenshelf.db` is a consistent SQLite snapshot containing the catalog,
  metadata, settings, bookmarks, and listening progress.
- `data/Library/` contains the ListenShelf-managed audiobook library, including
  unreferenced files that may still be recoverable.
- `data/Covers/` contains locally cached cover images.

Every archived file has its uncompressed size and SHA-256 digest recorded in
the manifest. ListenShelf verifies all entries before a restore can be
confirmed. Audiobook and image files are already compressed formats, so they
are stored without wasteful recompression; the database and manifest use ZIP
compression.

## Restore behavior

A restore is an exact replacement, not a merge. ListenShelf validates and
stages the entire selected backup before changing live data, rewrites managed
paths for the current installation, and checks the staged SQLite database.
The staged database is upgraded through the same versioned migrations used at
normal startup before it can replace the live data.
Immediately before replacement it exports the current library to a separate
pre-restore `.listenshelf-backup` file beside the selected backup. If
replacement or final validation fails, the live directory is rolled back.

Restore confirmation is shown inline in Settings. ListenShelf does not restore
automatically and does not silently merge or discard the current library.

If the live database cannot be opened, the startup recovery screen uses a
separate restore path. The selected backup is still fully validated and staged
first, but ListenShelf cannot create a normal database-backed safety archive
from unreadable data. Instead, it moves the entire existing data directory to
a timestamped `ListenShelf Recovered Data` directory beside the live data
directory. That preserved directory is retained after a successful restore and
is moved back automatically if replacement or final validation fails.

## Privacy and future cloud support

Backups are local files and ListenShelf does not upload them anywhere. They are
not encrypted, because they contain the user's own audiobook files and local
library data; users should store them somewhere they trust. A future optional
cloud-backup feature can upload the same versioned artifact without changing
the local backup format.
