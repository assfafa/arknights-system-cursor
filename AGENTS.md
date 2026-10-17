# Contributor instructions

This repository is a small Windows utility for switching the Arknights PC cursor by backing up and renaming its dedicated resource bundle.

- Read `docs/AI-ASSISTANTS.md` for operating an installed game. It documents GUI/CLI behavior, authorization boundaries, and recovery.
- Keep code readable: meaningful names, explicit multiline `if` / `else` with braces, straightforward control flow. Do not introduce ternaries or unrelated reformatting.
- Keep operations within the selected installation and per-installation backup directory. Preserve SHA-256 verification, independent backups, game-process checks, and update-safe restore behavior.
- Use synthetic UnityFS fixtures in tests. Never commit or redistribute game binaries, resource bundles, real backups, credentials, private configuration, or local user paths.
- Do not patch executables/DLLs, alter resource indexes or registry settings, inject into the game, or add anticheat bypasses.
- Test changes to resource discovery, decoding, enable/restore, concurrency, and persistence using `scripts/build.ps1` on Windows.
- GUI and CLI link the same `src/Core/*.cs` files; keep their behavior consistent.
- Target .NET Framework 4.8 for the distributed applications. Building uses a modern .NET SDK.
- Release artifacts go in ignored `artifacts/`. Publish only when the user requests it; do not create duplicate releases or overwrite existing release assets without a concrete reason.
