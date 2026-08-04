# -*- coding: utf-8 -*-
import io, re
import markdown

SRC = r"E:\workbudy\2026-07-10-10-35-57\上位机项目制学习路线.md"
OUT = r"E:\workbudy\2026-07-10-10-35-57\上位机项目制学习路线.html"

with io.open(SRC, "r", encoding="utf-8") as f:
    text = f.read()

md = markdown.Markdown(extensions=["tables", "fenced_code", "sane_lists"])
html = md.convert(text)

# ---- assign ids to h2/h3 and collect nav ----
nav = []
ctr = {"2": 0, "3": 0}
def repl_h(m):
    tag, content = m.group(1), m.group(2)
    lvl = tag[1]
    ctr[lvl] += 1
    cid = "sec-%s-%d" % (lvl, ctr[lvl])
    nav.append((int(lvl), cid, re.sub(r"<[^>]+>", "", content)))
    return '<%s id="%s">%s</%s>' % (tag, cid, content, tag)
html = re.sub(r"<(h[23])>(.*?)</\1>", repl_h, html, flags=re.S)

# ---- make 打卡[ ] interactive (section 四, days 1..N) ----
day = [0]
def cb(m):
    day[0] += 1
    return ('<label class="cb"><input type="checkbox" class="daycb" data-day="%d" '
            'onchange="toggleDay(%d)"> 打卡</label>') % (day[0], day[0])
html = re.sub(r"打卡\[ \](?=</li>)", cb, html)
TOTAL = day[0]

# ---- build nav html (group h3 under h2) ----
nav_html = ['<ul class="navlist">']
cur_h2 = None
for lvl, cid, title in nav:
    if lvl == 2:
        nav_html.append('</ul>' if cur_h2 else '')
        nav_html.append('<li class="nav-h2"><a href="#%s">%s</a><ul class="navsub">' % (cid, title))
        cur_h2 = cid
    else:
        nav_html.append('<li class="nav-h3"><a href="#%s">%s</a></li>' % (cid, title))
nav_html.append('</ul></li></ul>')
NAV = "\n".join(nav_html)

LEGEND = """
<div class="legend">
  <b>📖 看之前先认 3 类东西（不然会云里雾里）：</b>
  <span class="badge b-syntax">🟦 C# 语法</span> 语言自带，装好 .NET 就有，<b>不装包</b>
  <span class="badge b-bcl">🟩 .NET 类库/BCL</span> 微软标准库，<code>using</code> 即用，<b>默认不装包</b>（SerialPort 等少数需加一行 <code>dotnet add package</code>，但仍官方）
  <span class="badge b-nuget">🟧 第三方 NuGet</span> 必须 <code>dotnet add package 包名</code> 才能用
  <span class="mnem">口诀：语法天生物 · BCL 随 .NET · 第三方要装包</span>
</div>"""

SCRIPT = """
<script>
var TOTAL = __TOTAL__;
function key(n){return 'swj_day_'+n;}
function toggleDay(n){
  var cb=document.querySelector('.daycb[data-day="'+n+'"]');
  if(cb.checked){localStorage.setItem(key(n),'1');}
  else{localStorage.removeItem(key(n));}
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
  document.getElementById('ptext').textContent='已完成 '+done+' / '+TOTAL+' 项 （'+pct+'%）';
  document.title='上位机13K进度 '+done+'/'+TOTAL+' · '+pct+'%';
}
window.onload=function(){syncAll();};
</script>
"""
SCRIPT = SCRIPT.replace("__TOTAL__", str(TOTAL))

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
.sidebar{position:sticky;top:110px;width:260px;flex:none;max-height:calc(100vh - 120px);overflow:auto;padding:16px 12px;border-right:1px solid var(--line);background:#fff;}
.navlist{list-style:none;margin:0;padding:0;font-size:13px;}
.nav-h2>a{font-weight:700;color:var(--ink);text-decoration:none;}
.navsub{list-style:none;margin:4px 0 8px 12px;padding:0;}
.nav-h3>a{color:var(--muted);text-decoration:none;}
.navlist a:hover{color:var(--blue);}
.content{flex:1;min-width:0;padding:24px 32px 80px;max-width:920px;}
.content h1{font-size:24px;}
.content h2{font-size:21px;margin-top:38px;padding-top:10px;border-top:2px solid var(--line);}
.content h3{font-size:17px;margin-top:26px;color:var(--blue);}
.content table{border-collapse:collapse;width:100%;margin:14px 0;font-size:14px;}
.content th,.content td{border:1px solid var(--line);padding:7px 10px;text-align:left;}
.content th{background:#f1f4fa;}
.content blockquote{border-left:4px solid var(--blue);background:#f4f8ff;margin:14px 0;padding:10px 16px;color:#33405c;border-radius:0 6px 6px 0;}
.content code{background:#eef1f7;padding:1px 5px;border-radius:4px;font-size:13px;}
.content pre{background:#1f2430;color:#e6e9f0;padding:14px;border-radius:8px;overflow:auto;}
.content pre code{background:none;color:inherit;padding:0;}
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
<title>上位机转行13K · 学习进度</title>
%s
</head>
<body>
<div class="topbar">
  <h1>🚀 上位机转行 13K · 学习进度看板</h1>
  <div class="progress">
    <div class="barwrap"><div id="bar"></div></div>
    <div id="ptext">已完成 0 / %d 项 （0%%）</div>
  </div>
</div>
%s
<div class="layout">
  <aside class="sidebar">%s</aside>
  <main class="content">
%s
  <div class="foot">本页打卡状态自动保存在你浏览器本地（localStorage），换设备不会同步。想全平台同步就照计划里的腾讯文档打卡总表填写。</div>
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
