# Public repository sanitization

The public repository intentionally excludes private Action Replay firmware dumps and the historical proprietary Datel PC executable used during reverse-engineering research.

Automated firmware validation uses a deterministic synthetic 128 KiB fixture containing only the structural markers required by the validator. No private firmware payload is needed for CI.

GitHub Release assets produced by the project remain separate from private research material.
