#!/usr/bin/env python3
"""Generate a portable, dependency-free requirements scoreboard from canonical CSV."""
import csv
import html
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def render(rows):
    """Escape all tracker values; a status count is never a completion percentage."""
    counts = Counter(row['implementation_status'] for row in rows)
    cards = ''.join(f'<div class="stat"><strong>{count}</strong>{html.escape(status)}</div>'
                    for status, count in sorted(counts.items()))
    body = ''
    for row in rows:
        cells = ''.join(f'<td>{html.escape(row[key])}</td>' for key in
                        ('requirement_id', 'priority', 'implementation_status'))
        issue = html.escape(row['github_issue'], quote=True)
        body += f'<tr>{cells}<td><a href="{issue}">Tracked work</a></td></tr>'
    return '''<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>BenchDouble requirements scoreboard</title><style>
body{font:16px/1.5 system-ui;margin:2rem auto;padding:0 1rem;max-width:1100px;background:#111b29;color:#edf3f8}
h1{font-size:2rem}a{color:#8bdece}.stats{display:flex;flex-wrap:wrap;gap:1rem}.stat{padding:1rem;background:#223349;border-radius:8px;overflow-wrap:anywhere}strong{display:block;font-size:2rem}
input{padding:.7rem;margin:1rem 0;width:90%;max-width:600px}table{border-collapse:collapse;width:100%;font-size:.9rem}th,td{text-align:left;padding:.6rem;border-bottom:1px solid #3c4c60}tr[hidden]{display:none}.table{overflow:auto}
</style><h1>Requirements scoreboard</h1><p>StrictDoc baseline · xUnit / Reqnroll verification · GitHub work items</p>
<p>Source committed is not verified behavior. These counts are evidence categories, not percentages complete.</p>
<section class="stats">''' + cards + f'''</section><p>Total: {len(rows)} requirements.</p>
<label for="filter">Filter by ID, priority or status</label><br><input id="filter" type="search">
<div class="table"><table><thead><tr><th>ID</th><th>Priority</th><th>Evidence status</th><th>Issue</th></tr></thead><tbody>{body}</tbody></table></div>
<script>document.getElementById('filter').addEventListener('input',function(){{const q=this.value.toLowerCase();document.querySelectorAll('tbody tr').forEach(r=>r.hidden=!r.textContent.toLowerCase().includes(q));}});</script></html>'''


def main():
    with (ROOT / 'requirements/traceability.csv').open(newline='', encoding='utf-8') as source:
        rows = list(csv.DictReader(source))
    output = ROOT / 'build/requirements-scoreboard.html'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(render(rows), encoding='utf-8')
    print(output)


if __name__ == '__main__':
    main()
