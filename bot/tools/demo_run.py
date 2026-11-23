"""``python -m bot demo`` - run the whole automation against the simulator.

Nothing touches a real game: the simulator renders frames, answers taps and
slowly drains hunger/thirst, breaks nets and throws junk on them.  Good for

* verifying the 500 kg auto-stop and every recovery path,
* learning what the logs look like before trusting a night run,
* CI (exit code 0 = the bot reached the weight limit cleanly).
"""

from __future__ import annotations

import time
from pathlib import Path

from ..config import Config
from ..logging_setup import setup_logging
from ..runner import BotRunner, RunOptions
from ..state import BotState
from .demo.simulator import GameSimulator
from .make_templates import build as build_templates


def _demo_config(cfg: Config, templates: Path) -> Config:
    data = dict(cfg.raw)
    data.setdefault("vision", {})["templates_dir"] = str(templates)
    # the simulator's HUD bars are pure colours in the default ranges
    data.setdefault("anti_afk", {})["interval"] = 20.0
    # QTE demo: the simulator draws a prompt letter and clears junk on key-hold;
    # there is no OCR in CI, so the detector is stubbed with a fixed letter
    data.setdefault("trash", {})["qte"] = True
    data.setdefault("qte", {})["enabled"] = True
    data.setdefault("guards", {})["lost_connection"] = ""
    data.setdefault("guards", {})["loading_screen"] = ""
    data.setdefault("moving", {})["enabled"] = False
    return Config(data, cfg.path)


class DemoRunner(BotRunner):
    """BotRunner wired to the simulator, with a wall-clock speed factor."""

    def __init__(self, cfg: Config, sim: GameSimulator, options: RunOptions) -> None:
        super().__init__(cfg, options, capture=sim, touch=sim)
        self.sim = sim
        self._install_fake_ocr()
        self._last = time.time()
        # any blocking wait inside a task advances the simulated world, so the
        # demo finishes in seconds instead of real fishing time
        self.actions.sleep = self._demo_sleep

    def _install_fake_ocr(self) -> None:
        """No Tesseract in the demo: answer every glyph with the drawn letter."""
        from .demo.simulator import GameSimulator as _Sim
        from ..prompt_key import GlyphClassifier, PromptKeyConfig

        # the simulator draws one of these; P is the confusable one, keep it simple
        for letter in ("P", "Q", "X", "O", "K"):
            pass
        cfg_qte = PromptKeyConfig.from_config(self.cfg)
        classifier = GlyphClassifier(cfg_qte, fake=lambda _glyph: [("K", 95.0), ("K", 90.0)])
        detector = self.actions.prompt_key_detector()
        detector.classifier = classifier

    def _demo_sleep(self, seconds: float) -> None:
        if seconds and seconds > 0:
            self.sim.step(min(float(seconds), 5.0))

    def run(self) -> BotState:  # type: ignore[override]
        self.install_signal_handlers()
        self.log.info("DEMO: simulator driving, limit %.0f kg, speed x%.0f",
                      self.weight_limit, self.sim.speed)
        interval = 1.0 / max(0.5, self.cfg.num("loop.hz", 4.0))
        tick = 0
        try:
            while True:
                now = time.time()
                dt = max(0.0, now - self._last)
                self._last = now
                self.sim.step(dt)
                self._check_stop_rules()
                if self.stop_requested:
                    break
                try:
                    self.tick()
                except Exception as exc:  # noqa: BLE001 - demo must survive its own bugs
                    self.log.exception("demo tick error: %s", exc)
                    self.state.consecutive_failures += 1
                tick += 1
                self.store.save(self.state)
                if tick % 20 == 0:
                    self.log.info("progress %s", self.state.summary())
                time.sleep(min(interval, 0.05))
        finally:
            self.shutdown()
        return self.state


def run_demo(cfg: Config, args) -> int:
    templates = Path(getattr(args, "templates", "") or "demo_templates")
    if not templates.exists() or not any(templates.glob("*.png")):
        print(f"generating demo templates in {templates} ...")
        build_templates(cfg, templates)
    demo_cfg = _demo_config(cfg, templates)
    setup_logging(demo_cfg, verbose=getattr(args, "verbose", False))

    sim = GameSimulator(demo_cfg, fish_kg=float(getattr(args, "fish_kg", 4.0) or 4.0),
                        speed=float(getattr(args, "speed", 10.0) or 10.0),
                        break_every=int(getattr(args, "break_every", 0) or 0))
    options = RunOptions(dry_run=False, max_kg=float(getattr(args, "kg", 500.0) or 500.0),
                         max_seconds=float(getattr(args, "max_minutes", 0.0) or 0.0) * 60)
    if getattr(args, "max_minutes", 0):
        options.max_seconds = float(args.max_minutes) * 60

    save_frames = bool(getattr(args, "save_frames", False))
    frame_dir = Path("demo_frames")
    if save_frames:
        frame_dir.mkdir(exist_ok=True)

    runner = DemoRunner(demo_cfg, sim, options)
    if save_frames:
        original_tick = runner.tick

        def tick_with_frames():  # pragma: no cover - debug aid
            import cv2

            original_tick()
            cv2.imwrite(str(frame_dir / f"frame-{sim.frames_drawn:05d}.png"), sim.render())

        runner.tick = tick_with_frames  # type: ignore[assignment]

    started = time.time()
    state = runner.run()
    elapsed = time.time() - started

    print("\n=== DEMO RESULT ===")
    print(f"simulated fish: {sim.sim.fish_count} ({sim.sim.fish_kg:.1f} kg)")
    print(f"bot counted:    {state.fish_count} ({state.total_kg:.1f} kg)")
    print(f"nets broken:    {state.nets_broken} | restocked: {state.nets_restocked} "
          f"| trunk trips: {state.trunk_trips}")
    print(f"ate {state.snacks_eaten}x, drank {state.drinks}x, trash {state.trash_cleared}, "
          f"anti-afk {state.anti_afk_actions}")
    print(f"stop reason:    {state.stop_reason}")
    print(f"wall time:      {elapsed:.1f}s (faster than real time is expected)")
    if sim.sim.log:
        print("last game events:")
        for line in sim.sim.log[-8:]:
            print(f"  - {line}")
    return 0 if state.stop_reason == "weight_limit_reached" else 1
