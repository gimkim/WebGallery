const fs=require('node:fs'), assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage();
  const cards=Array.from({length:1200},(_,i)=>`<article class="gallery-card"><a href="/Gallery?path=child"><div class="media">${i}</div><div class="item-info">Folder ${i}</div></a></article>`).join('');
  await page.route('**/*',route=>route.fulfill({contentType:'text/html',body:`<body data-app-base=""><header class="topbar">Gallery</header><main class="page-shell"><div class="breadcrumbs">Home</div><div id="gallery-items" style="display:grid;grid-template-columns:repeat(8,1fr)">${cards}</div></main></body>`}));
  await page.goto('http://spa-fixture.local/');
  await page.addStyleTag({content:fs.readFileSync('wwwroot/css/site.css','utf8')});
  await page.evaluate(()=>{window.GalleryPages={mount(){},unmount(){}};window.fixtureFetches=0;window.fetch=()=>{window.fixtureFetches++;return new Promise(()=>{});};});
  await page.addScriptTag({content:fs.readFileSync('wwwroot/js/spa-router.js','utf8')});
  const cdp=await page.context().newCDPSession(page);await cdp.send('Performance.enable');
  const before=await cdp.send('Performance.getMetrics');
  const timing=await page.evaluate(async()=>{
   const start=performance.now();document.querySelector('.gallery-card a').click();const handler=performance.now()-start;
   let beforePaint;
   await new Promise(resolve=>requestAnimationFrame(()=>{beforePaint=window.fixtureFetches;requestAnimationFrame(resolve);}));
   return {handlerMs:handler,paintOpportunityMs:performance.now()-start,shown:!document.querySelector('.spa-navigation-status').hidden,fetchesBeforeFirstPaint:beforePaint};
  });
  const after=await cdp.send('Performance.getMetrics');
  const metric=(v,name)=>v.metrics.find(x=>x.name===name).value;
  const styleRecalculations=metric(after,'RecalcStyleCount')-metric(before,'RecalcStyleCount');
  console.log(JSON.stringify({...timing,styleRecalculations}));assert(timing.shown);
  if(process.argv.includes('--assert-fixed')) { assert(styleRecalculations<20,'Placeholder setup must not trigger a style recalculation for every element');assert.equal(timing.fetchesBeforeFirstPaint,0,'Paint must precede API work'); }
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
