"""Behaviour tasks used by the orchestrator."""

from .anti_afk import AntiAfkTask
from .base import BaseTask
from .fishing import FishingTask, Phase
from .survival import SurvivalTask
from .trash import TrashTask
from .trunk import TrunkTask

__all__ = [
    "AntiAfkTask",
    "BaseTask",
    "FishingTask",
    "Phase",
    "SurvivalTask",
    "TrashTask",
    "TrunkTask",
]
