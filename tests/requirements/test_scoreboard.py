"""Check generated scoreboard data and escaping without browser dependencies."""
import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('scoreboard', ROOT / 'scripts/requirements_scoreboard.py')
scoreboard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scoreboard)


class ScoreboardTests(unittest.TestCase):
    def test_counts_and_untrusted_text_are_rendered_safely(self):
        rows = [dict(requirement_id='<script>alert(1)</script>', priority='P0',
                     implementation_status='Planned', github_issue='https://github.com/example/issues/1')]
        output = scoreboard.render(rows)
        self.assertIn('Total: 1 requirements', output)
        self.assertIn('&lt;script&gt;', output)
        self.assertNotIn('<script>alert(1)</script>', output)
        self.assertIn('not percentages complete', output)

    def test_empty_inventory_is_valid(self):
        self.assertIn('Total: 0 requirements', scoreboard.render([]))
