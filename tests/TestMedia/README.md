# Synthetic smoke-test media

No audiobook recordings are committed to this repository. Run the generator
from the repository root to create short, deterministic sine-tone fixtures:

```powershell
./build/Generate-SmokeTestMedia.ps1
```

The generated, ignored `artifacts/test-media` directory contains M4A, MP3, M4B,
chaptered M4B, and Unicode-filename cases plus SHA-256 checksums. The tones are
only for testing load, chapter discovery, seeking, completion, replay, and path
handling; they are not useful listening content and carry no third-party audio.

Generation requires `ffmpeg` on `PATH`. The files are generated rather than
checked in so their codec provenance and exact inputs remain clear.
