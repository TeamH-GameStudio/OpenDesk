"""pytest conftest — Middleware 루트를 sys.path 에 추가해 providers 패키지를 import 가능하게 함."""

import sys
from pathlib import Path

_MIDDLEWARE_ROOT = Path(__file__).resolve().parent.parent
if str(_MIDDLEWARE_ROOT) not in sys.path:
    sys.path.insert(0, str(_MIDDLEWARE_ROOT))
