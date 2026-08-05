# ListenShelf database safety and compatibility

ListenShelf stores its local catalog, metadata, settings, bookmarks, and
listening progress in `%LocalAppData%\ListenShelf\listenshelf.db` on Windows.
Managed audiobooks and cached covers live beside that database under `Library`
and `Covers`. Application upgrades and uninstallers do not own this directory.

## Versioned migrations

The database records every applied migration in `schema_migrations` and mirrors
the current version in SQLite's `user_version`. Migration numbers are permanent:
released migrations are not reordered, renamed, or rewritten. New schema
changes receive the next number and are applied in order.

Before changing an existing unversioned or older database, ListenShelf:

1. opens the database and runs SQLite's `quick_check`;
2. validates its migration history and supported version;
3. creates a consistent database copy under
   `%LocalAppData%\ListenShelf\Database Recovery`;
4. applies each pending migration in its own transaction; and
5. checks database integrity and the required current schema again.

A failed migration is rolled back. A database with a version newer than the
running application supports is never downgraded or modified; the user must
install a compatible newer ListenShelf build or explicitly restore an older
backup.

## Startup failure categories

The normal library window is only created after database initialization
succeeds. Otherwise, ListenShelf displays its database recovery window with a
specific category:

- **Damaged:** SQLite integrity failed, the file is not a database, migration
  history is invalid, or required current tables or columns are missing.
- **Unavailable:** the database is locked, inaccessible, or blocked by a
  filesystem or permission problem.
- **Migration failed:** a numbered upgrade could not complete and its
  transaction was rolled back.
- **Newer version:** the database requires a newer ListenShelf schema version.

Retrying is always available. The recovery screen can also open the data folder
and restore a validated `.listenshelf-backup` file. Recovery restore preserves
the complete previous data directory beside the live directory and restores it
automatically if replacement fails.

## Rebuilding after damage

When actual database damage is detected and no usable backup exists, the user
can explicitly rebuild a basic catalog. ListenShelf first moves the database,
WAL, and shared-memory files into a timestamped folder under `Database Recovery`.
It then creates a fresh current database and re-registers supported M4B, M4A,
and MP3 files found directly inside standard `Library/<book-id>/` directories.
A cached cover named for the same book identifier is reattached when found.

The rebuild does not delete or rewrite audiobook files. Nonstandard folders,
extra audiobooks, and other unrecognized files remain on disk for Storage Care.
Metadata, bookmarks, settings, and listening progress that existed only in the
damaged database may not be recoverable, which is why restoring a complete
backup is the preferred path.
