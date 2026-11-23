"""Entry point so ``python -m bot`` works from a checkout."""

from .cli import main

if __name__ == "__main__":
    raise SystemExit(main())
