# -*- coding: utf-8 -*-
import io, re
import markdown

BASE = r"F:\00_project\上位机学习"
FILES = [
    ("M0_每日讲义_深度版.md", "M0 · C#/.NET 热身 + 工程骨架"),
    ("M1_串口通信_深度版.md", "M1 · 串口通信"),
    ("M2_Modbus_深度版.md", "M2 · Modbus RTU/TCP"),
    ("M3_PLC_深度版.md", "M3 · PLC 通信(S7)"),
    ("M4_数据持久化_深度版.md", "M4 · 数据持久化(EF Core)"),
    ("M5_实时可视化_深度版.md", "M5 · 实时可视化(LiveCharts)"),
    ("M6_报警引擎日志_深度版.md", "M6 · 报警引擎 + Serilog"),
    ("M7_OPCUA_MQTT_深度版.md", "M7 · OPC UA / MQTT 上云"),
    ("M8_工程化收尾_深度版.md", "M8 · 工程化收尾(MVVM+安装包)"),
    ("M9_工程素养_测试DI容错_深度版.md", "M9 · 工程素养(测试/DI/统一采集/容错)"),
    ("M10_报表_深度版.md", "M10 · 报表(聚合/可视化/导出)"),
    ("硬件替代方案与讲解_深度版.md", "附录A · 硬件替代方案与讲解(没硬件怎么练+硬件科普)"),
]
OUT = BASE + r"\学习总纲_全模块深度版.html"

# ---- concat with a module divider + module-level h1 ----
parts = []
for fn, title in FILES:
    with io.open(BASE + "\\" + fn, "r", encoding="utf-8") as f:
        txt = f.read().strip()
    # replace the first '# ...' line with a normalized module title
    txt = re.sub(r"^#\s+.*$", "# " + title, txt, count=1, flags=re.M)
    parts.append(txt)
text = "\n\n---\n\n".join(parts)

md = markdown.Markdown(extensions=["tables", "fenced_code", "sane_lists"])
html = md.convert(text)

# ---- assign ids to h1/h2/h3 and collect nav ----
nav = []
ctr = {"1": 0, "2": 0, "3": 0}
def repl_h(m):
    tag, content = m.group(1), m.group(2)
    lvl = tag[1]
    ctr[lvl] += 1
    cid = "sec-%s-%d" % (lvl, ctr[lvl])
    nav.append((int(lvl), cid, re.sub(r"<[^>]+>", "", content)))
    return '<%s id="%s">%s</%s>' % (tag, cid, content, tag)
html = re.sub(r"<(h[123])>(.*?)</\1>", repl_h, html, flags=re.S)

# ---- make 打卡[ ] interactive ----
day = [0]
def cb(m):
    day[0] += 1
    return ('<label class="cb"><input type="checkbox" class="daycb" data-day="%d" '
            'onchange="toggleDay(%d)"> 打卡</label>') % (day[0], day[0])
html = re.sub(r"打卡\[ \]", cb, html)
TOTAL = max(day[0], 1)

# ---- build 3-level nav (module h1 -> day h2 -> h3) ----
nav_html = ['<ul class="navlist">']
cur_h1 = None; cur_h2 = None
for lvl, cid, title in nav:
    if lvl == 1:
        if cur_h2: nav_html.append('</ul></li>')
        if cur_h1: nav_html.append('</ul></li>')
        nav_html.append('<li class="nav-h1"><a href="#%s">%s</a><ul class="navmod">' % (cid, title))
        cur_h1 = cid; cur_h2 = None
    elif lvl == 2:
        if cur_h2: nav_html.append('</ul></li>')
        nav_html.append('<li class="nav-h2"><a href="#%s">%s</a><ul class="navsub">' % (cid, title))
        cur_h2 = cid
    else:
        nav_html.append('<li class="nav-h3"><a href="#%s">%s</a></li>' % (cid, title))
if cur_h2: nav_html.append('</ul></li>')
if cur_h1: nav_html.append('</ul></li>')
nav_html.append('</ul>')
NAV = "\n".join(nav_html)

LEGEND = """
<div class="legend">
  <b>📖 3 类技术来源（必认，不然云里雾里）：</b>
  <span class="badge b-syntax">🟦 C# 语法</span> 语言自带，装好 .NET 就有，<b>不装包</b>
  <span class="badge b-bcl">🟩 .NET 类库/BCL</span> 微软标准库，<code>using</code> 即用，<b>默认不装包</b>
  <span class="badge b-nuget">🟧 第三方 NuGet</span> 必须 <code>dotnet add package 包名</code>
  <span class="mnem">口诀：语法天生物 · BCL 随 .NET · 第三方要装包</span>
</div>"""

SCRIPT = """
<script>
var TOTAL = __TOTAL__;
function key(n){return 'allmod_day_'+n;}
function toggleDay(n){
  var cb=document.querySelector('.daycb[data-day="'+n+'"]');
  if(cb.checked){localStorage.setItem(key(n),'1');} else {localStorage.removeItem(key(n));}
  syncAll();
}
function syncAll(){
  var done=0;
  document.querySelectorAll('.daycb').forEach(function(cb){
    var n=cb.getAttribute('data-day');
    var on=localStorage.getItem(key(n))==='1';
    cb.checked=on; if(on)done++;
  });
  var pct=Math.round(done/TOTAL*100);
  document.getElementById('bar').style.width=pct+'%';
  document.getElementById('ptext').textContent='已打卡 '+done+' / '+TOTAL+' 天 （'+pct+'%）';
}
window.onload=function(){syncAll();};
</script>
""".replace("__TOTAL__", str(TOTAL))

CSS = """
<style>
:root{--blue:#2f6fed;--green:#1f9d55;--orange:#e07b00;--ink:#1f2430;--muted:#67708a;--line:#e6e9f0;--bg:#f7f8fb;}
*{box-sizing:border-box;}
body{margin:0;font-family:-apple-system,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif;color:var(--ink);background:var(--bg);line-height:1.7;}
.topbar{position:sticky;top:0;z-index:50;background:#fff;border-bottom:1px solid var(--line);padding:10px 18px;box-shadow:0 1px 6px rgba(0,0,0,.05);}
.topbar h1{font-size:16px;margin:0 0 8px;font-weight:700;}
.progress{display:flex;align-items:center;gap:12px;}
.barwrap{flex:1;height:14px;background:#eef1f7;border-radius:8px;overflow:hidden;}
#bar{height:100%;width:0;background:linear-gradient(90deg,var(--blue),var(--green));transition:width .3s;}
#ptext{font-size:13px;color:var(--muted);white-space:nowrap;}
.legend{background:#fff;border-bottom:1px solid var(--line);padding:10px 18px;font-size:13px;display:flex;flex-wrap:wrap;gap:8px;align-items:center;}
.badge{padding:2px 8px;border-radius:12px;font-weight:600;font-size:12px;}
.b-syntax{background:#e8f0ff;color:var(--blue);}
.b-bcl{background:#e6f6ec;color:var(--green);}
.b-nuget{background:#fdf0e0;color:var(--orange);}
.legend code{background:#f0f2f7;padding:1px 5px;border-radius:4px;font-size:12px;}
.mnem{width:100%;color:var(--muted);font-size:12px;margin-top:2px;}
.layout{display:flex;align-items:flex-start;}
.sidebar{position:sticky;top:150px;width:280px;flex:none;max-height:calc(100vh - 160px);overflow:auto;padding:16px 12px;border-right:1px solid var(--line);background:#fff;}
.navlist{list-style:none;margin:0;padding:0;font-size:13px;}
.nav-h1>a{font-weight:800;color:var(--blue);text-decoration:none;}
.navmod{list-style:none;margin:4px 0 8px 10px;padding:0;}
.nav-h2>a{font-weight:700;color:var(--ink);text-decoration:none;}
.navsub{list-style:none;margin:3px 0 6px 12px;padding:0;}
.nav-h3>a{color:var(--muted);text-decoration:none;}
.navlist a:hover{color:var(--orange);}
.content{flex:1;min-width:0;padding:24px 32px 80px;max-width:980px;}
.content h1{font-size:25px;background:linear-gradient(90deg,#eef3ff,#eafaf0);padding:12px 16px;border-radius:8px;margin-top:10px;}
.content h2{font-size:21px;margin-top:34px;padding-top:10px;border-top:2px solid var(--line);}
.content h3{font-size:17px;margin-top:24px;color:var(--blue);}
.content table{border-collapse:collapse;width:100%;margin:14px 0;font-size:14px;}
.content th,.content td{border:1px solid var(--line);padding:7px 10px;text-align:left;vertical-align:top;}
.content th{background:#f1f4fa;}
.content blockquote{border-left:4px solid var(--blue);background:#f4f8ff;margin:14px 0;padding:10px 16px;color:#33405c;border-radius:0 6px 6px 0;}
.content code{background:#eef1f7;padding:1px 5px;border-radius:4px;font-size:13px;}
.content pre{background:#1f2430;color:#e6e9f0;padding:14px;border-radius:8px;overflow:auto;}
.content pre code{background:none;color:inherit;padding:0;font-size:12.5px;line-height:1.55;}
.cb{display:inline-flex;align-items:center;gap:4px;font-size:13px;color:var(--green);font-weight:600;margin-left:6px;}
.cb input{width:16px;height:16px;cursor:pointer;}
.content ul li{margin:4px 0;}
.foot{color:var(--muted);font-size:12px;text-align:center;padding:20px;}
@media(max-width:820px){.sidebar{display:none;}.content{padding:18px;}}
</style>
"""

TPL = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>DAQ Monitor · 全模块深度讲义</title>
%s
</head>
<body>
<div class="topbar">
  <h1>📘 DAQ Monitor · 全模块深度讲义（M0–M10）· 打卡看板</h1>
  <div class="progress">
    <div class="barwrap"><div id="bar"></div></div>
    <div id="ptext">已打卡 0 / %d 天 （0%%）</div>
  </div>
</div>
%s
<div class="layout">
  <aside class="sidebar">%s</aside>
  <main class="content">
%s
  <div class="foot">打卡状态自动保存在浏览器本地（localStorage）。</div>
  </main>
</div>
%s
</body>
</html>
"""

doc = TPL % (CSS, TOTAL, LEGEND, NAV, html, SCRIPT)
with io.open(OUT, "w", encoding="utf-8") as f:
    f.write(doc)
print("OK ->", OUT)
print("TOTAL days tracked:", TOTAL)
print("nav sections:", len(nav))
