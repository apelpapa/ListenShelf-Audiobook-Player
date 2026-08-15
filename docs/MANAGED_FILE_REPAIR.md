# Managed-file repair

ListenShelf owns its managed audiobook copies, not the user's originals. Repair
replaces bytes at the existing managed path; it does not change the book's path
to point at an external source and does not import a new catalog entry.

## User flow

1. Open **Storage Care**. Use **Verify library files** to check a suspected book.
2. Under **Repair managed copy**, choose the cataloged book and choose its
   original file or another known-good copy.
3. Review the book and source path, then click **Repair managed copy** to confirm.
   Picking a source alone makes no changes.
4. Wait for copying, verification, and final replacement. The source is only read.
5. Check that the repaired book plays correctly at its saved position. Any old
   managed file is listed in the storage check as **Retained repair copy**. Use
   its cleanup action and inline confirmation only when it is no longer needed.

The source's size and SHA-256 must match the saved import baseline exactly.
Renaming a matching source is fine; a different edition, encoding, or embedded-tag
edit is not. No saved baseline means this form cannot safely repair the book.
Restore a known-good full-library backup instead. The original baseline is never
updated or created by repair.

## Safety boundaries

- Catalog ID, path, metadata, cover, bookmarks, fingerprint, and saved listening
  records are not rewritten by the repair service. Normal playback unloading
  saves the latest position before releasing the current audiobook handle.
- The current book is reopened paused after repair, never auto-played; unloading
  stops its sleep timer. Other books can keep playing during repair.
- Imports, removals, orphan recovery/cleanup, repair, and normal backup
  export/restore share a per-database operation lock across current app processes.
  The UI also blocks conflicting library actions while repair is active.
- Target validation requires a supported audiobook directly under its cataloged
  GUID folder. Filesystem links/junctions in source or destination paths are
  refused. The selected source cannot be the target path.
- An exclusive, uniquely named `<attempt-id>.repairing` file is created beside
  the target. Copying hashes the selected source; a second read hashes the staged
  bytes. The file is flushed before installation and held read-locked against
  Windows writers through the final checks. That handle is released immediately
  before the Windows replacement call, which requires its own writable handle.
- After validation, an existing target is replaced using .NET `File.Replace`,
  with a unique same-directory `<attempt-id>.repair-backup` path for the old
  file. A missing target is installed with a non-overwriting move. There is no
  delete-target-first fallback and no database migration is needed.
- Cancellation before final replacement removes only the attempt's temporary
  copy (and its newly created empty book directory). A late cancellation waits
  for final replacement rather than interrupting it.
- If final replacement reports an error, all remaining repair artifacts are
  preserved and the user is told to verify the current target before retrying.
  A forced process exit can likewise leave artifacts. Startup never automatically
  deletes or installs them. The catalog still points at the same audiobook path.
- Retained/unfinished repair files appear immediately in the structural storage
  check as separate attention items, without a startup popup. Cleanup revalidates
  the selected item and refuses cataloged paths or filesystem links. The picker
  offers **All files** so a complete retained repair artifact can itself be used
  as a read-only repair source, but it must still pass the saved fingerprint.
- Full-library exports include retained artifacts. Confirmed whole-book removal
  deletes the managed book folder, including repair artifacts in that folder.

The Windows replacement uses the platform operation exposed by
[.NET File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-10.0).
Filesystem, permission, disk, and external-program failures are still possible;
repair does not claim that a matching fingerprint proves audio is playable.
Keep an independent backup. Cross-platform validation remains deferred.

## Windows manual pass checklist

Use disposable test audiobooks and back up your real library before manually
simulating missing or damaged storage. Never corrupt your original source file.

- [ ] Import a disposable file, edit its details/cover, save a bookmark, and play
  far enough to save a position. Keep an untouched copy of the source.
- [ ] Close ListenShelf, alter only that book's managed copy, reopen, and confirm
  **Verify selected** reports **Changed**.
- [ ] Choose a different file of the same size for repair. Confirm it is refused
  and the old managed bytes and listening records remain intact.
- [ ] Choose the original and confirm repair. Verify it reports unchanged, plays,
  and preserves the title, cover, bookmark, and listening position.
- [ ] Repeat with a missing managed file (and, separately, a missing book folder).
  Confirm the same catalog entry is restored, with no duplicate book created.
- [ ] Repair a currently loaded book. Confirm it is reopened paused at its saved
  position, its chapter controls work, and playback does not restart on its own.
- [ ] Cancel during a large copy and during verification. Confirm the previous
  managed file remains unchanged and only the unfinished attempt is cleaned up.
- [ ] Close the window during repair. Confirm it waits for cancellation cleanup
  or final replacement, then reopens normally on the next launch.
- [ ] Confirm retained copies survive restart, show the Storage Care attention
  indicator, and are deleted only by inline-confirmed cleanup (or confirmed
  whole-book removal). Canceling the cleanup confirmation must keep the file.
- [ ] Confirm the form, progress, and cleanup labels are readable in light/dark
  modes and at the minimum supported window size.

Automated tests use synthetic bytes in isolated temporary workspaces; they do
not alter real user library data and are not a substitute for audible playback
or visual UI testing.
