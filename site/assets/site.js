
// 打卡：按页面独立存储(localStorage)
function key(slug,n){return 'daq_'+slug+'_day_'+n;}
function toggleDay(slug,n){
  var cb=document.querySelector('.daycb[data-day="'+n+'"]');
  if(cb.checked){localStorage.setItem(key(slug,n),'1');} else {localStorage.removeItem(key(slug,n));}
  syncAll(slug);
}
function syncAll(slug){
  var done=0,total=0;
  document.querySelectorAll('.daycb').forEach(function(cb){
    total++;
    var n=cb.getAttribute('data-day');
    var on=localStorage.getItem(key(slug,n))==='1';
    cb.checked=on; if(on)done++;
  });
  var pct=total?Math.round(done/total*100):0;
  var bar=document.getElementById('bar'); if(bar)bar.style.width=pct+'%';
  var pt=document.getElementById('ptext'); if(pt)pt.textContent='本页打卡 '+done+' / '+total+' （'+pct+'%）';
}
function toggleToc(){document.body.classList.toggle('toc-open');}
window.addEventListener('DOMContentLoaded',function(){
  // 页面 slug 由 body data-slug 提供
  var slug=document.body.getAttribute('data-slug')||'page';
  syncAll(slug);
  // 手机上点目录项后自动收起抽屉
  document.querySelectorAll('.toc a').forEach(function(a){
    a.addEventListener('click',function(){document.body.classList.remove('toc-open');});
  });
});
