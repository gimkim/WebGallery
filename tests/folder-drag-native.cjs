const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({viewport:{width:1600,height:1000}});page.setDefaultTimeout(15000);
 await page.goto('http://127.0.0.1:5294/Account/Login');
 await page.locator('[name=UserName]').fill('admin');await page.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForSelector('.directory-card');
 await page.route('**/__drag-fixture-image*',r=>r.fulfill({contentType:'image/png',body:fs.readFileSync('App_Data/spa-smoke/content/child/medium-fixture.png')}));
 await page.evaluate(()=>{
  GalleryPages.unmount();const grid=document.querySelector('#gallery-items');const template=grid.querySelector('.directory-card');
  const preview='/__drag-fixture-image';
  grid.replaceChildren(...Array.from({length:2000},(_,i)=>{const card=template.cloneNode(true);card.dataset.name='folder'+i;card.dataset.relativePath+='-'+i;
   card.querySelector('.media').outerHTML=`<div class="media folder-media folder-cover"><div class="folder-cover-shell"><span class="folder-cover-tab"></span><div class="folder-cover-grid cover-count-4">${Array.from({length:4},()=>`<span class="thumbnail-slot thumbnail-loading"><img data-thumbnail-src="${preview}" alt="" decoding="async"></span>`).join('')}</div></div></div>`;
   return card;}));
  GalleryPages.mount();document.dispatchEvent(new Event('gallery:navigation-end'));
  window.dragStats={events:0,long:[]};document.addEventListener('dragover',()=>dragStats.events++);new PerformanceObserver(list=>dragStats.long.push(...list.getEntries().map(x=>x.duration))).observe({type:'longtask'});
 });await page.waitForTimeout(1500);
 const animationStats=await page.evaluate(()=>{
  const hosts=[...document.querySelectorAll('.thumbnail-loading')];
  return {hosts:hosts.length,offscreenAnimated:hosts.filter(h=>!h.classList.contains('thumbnail-visible')&&getComputedStyle(h,'::before').animationName!=='none').length,animations:document.getAnimations().length};
 });console.log({animationStats});assert.equal(animationStats.offscreenAnimated,0);assert(animationStats.animations<200,'Only visible thumbnails may animate');
 await page.evaluate(()=>{dragStats.long=[];});
 let writeRequests=0;page.on('request',r=>{if(r.url().includes('/Files/')&&r.method()==='POST')writeRequests++;});
 const cdp=await page.context().newCDPSession(page);await cdp.send('Performance.enable');
 const trace=[];cdp.on('Tracing.dataCollected',e=>trace.push(...e.value));await cdp.send('Tracing.start',{categories:'devtools.timeline,disabled-by-default-devtools.timeline',transferMode:'ReportEvents'});
 const before=await cdp.send('Performance.getMetrics');
 const boxes=await page.locator('.directory-card').evaluateAll(cards=>Array.from(cards).slice(0,8).map(c=>{const r=c.getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+30};}));
 console.log('start',boxes);await page.mouse.move(boxes[0].x,boxes[0].y);await page.mouse.down();
 for(const [i,b] of boxes.slice(1).entries()){const t=Date.now();await page.mouse.move(b.x,b.y,{steps:8});console.log('cross ms',Date.now()-t);assert.equal(await page.locator('.file-drop-target').getAttribute('data-name'),'folder'+(i+1));}
 await page.keyboard.press('Escape');await page.mouse.up();
 const after=await cdp.send('Performance.getMetrics');const ended=new Promise(r=>cdp.once('Tracing.tracingComplete',r));await cdp.send('Tracing.end');await ended;
 const totals={};for(const e of trace)if(e.dur)totals[e.name]=(totals[e.name]||0)+e.dur/1000;console.log(Object.entries(totals).sort((a,b)=>b[1]-a[1]).slice(0,25));
 assert(await page.locator('.file-drag-surface').isHidden());assert.equal(await page.locator('.file-drop-target').count(),0);assert.equal(writeRequests,0);
 assert((totals.UpdateLayoutTree||0)<1000,'Offscreen shimmer must not consume seconds of style work during drag');
 // Keep the next viewport pending to verify actual observer-driven loading animation.
 await page.unroute('**/__drag-fixture-image*');
 await page.route('**/__drag-fixture-image*',async r=>{await new Promise(resolve=>setTimeout(resolve,1500));await r.fulfill({contentType:'image/png',body:fs.readFileSync('App_Data/spa-smoke/content/child/medium-fixture.png')}).catch(()=>{});});
 await page.evaluate(()=>scrollTo(0,document.body.scrollHeight));
 await page.waitForFunction(()=>document.querySelectorAll('.thumbnail-loading.thumbnail-visible').length>0);
 assert(await page.evaluate(()=>document.getAnimations().length>0),'Newly visible loading thumbnails animate');
 assert.equal(await page.evaluate(()=>[...document.querySelectorAll('.thumbnail-loading:not(.thumbnail-visible)')].filter(h=>getComputedStyle(h,'::before').content!=='none').length),0,'Offscreen shimmer has no render layer');
 await page.emulateMedia({reducedMotion:'reduce'});
 assert.equal(await page.evaluate(()=>[...document.querySelectorAll('.thumbnail-loading')].filter(h=>getComputedStyle(h,'::before').animationName!=='none').length),0,'Respect reduced motion for visible skeletons');
 console.log(JSON.stringify({stats:await page.evaluate(()=>dragStats),metrics:after.metrics.filter(x=>/Duration|Count/.test(x.name)).map(x=>({name:x.name,value:x.value-(before.metrics.find(y=>y.name===x.name)?.value||0)}))}));
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
