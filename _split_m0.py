import json

src = r"E:\workbudy\2026-07-10-10-35-57\M0_每日讲义_深度版.md"
out = r"E:\workbudy\2026-07-10-10-35-57\_m0_chunks.jsonl"

with open(src, "r", encoding="utf-8") as f:
    text = f.read()

lines = text.split("\n")
chunks = []
cur = []
cur_len = 0
MAX = 1400
in_code = False

for line in lines:
    stripped = line.strip()
    if stripped.startswith("```"):
        in_code = not in_code
    line_len = len(line) + 1  # newline

    # Flush before adding this line if adding would exceed MAX and we're at a safe break point
    safe_break = (stripped == "" or line.startswith("## ") or line.startswith("### "))
    if cur and not in_code and cur_len + line_len > MAX and safe_break:
        chunks.append("\n".join(cur))
        cur = []
        cur_len = 0

    cur.append(line)
    cur_len += line_len

    # If a single accumulated chunk is way over (e.g. big code block) and we just exited code, flush
    if not in_code and cur_len > MAX and (stripped == "" or line.startswith("## ") or line.startswith("### ")):
        chunks.append("\n".join(cur))
        cur = []
        cur_len = 0

if cur:
    chunks.append("\n".join(cur))

# Report sizes
for i, c in enumerate(chunks):
    print(f"chunk {i+1}: {len(c)} chars")

print(f"TOTAL chunks: {len(chunks)}")

with open(out, "w", encoding="utf-8") as f:
    for i, c in enumerate(chunks):
        f.write(json.dumps({"i": i + 1, "t": c}, ensure_ascii=False) + "\n")

print("written to", out)
