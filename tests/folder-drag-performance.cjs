const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage();
 const cards=Array.from({length:2000},(_,i)=>`<article class="gallery-card directory-card" data-relative-path="@root-1/f${i}"><a href="/?path=f${i}"><div class="media"><img src="data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7" /></div><div class="item-info">Folder ${i}</div></a></article>`).join('');
 await page.route('**/*',r=>r.fulfill({contentType:'text/html',body:`<body><main class="page-shell"><section data-write-path="@root-1"></section><div id="gallery-items" class="gallery-grid" style="--columns:8">${cards}</div></main></body>`}));await page.goto('http://drag-test.local/');
 await page.addStyleTag({content:fs.readFileSync('wwwroot/css/site.css','utf8')});await page.addScriptTag({content:fs.readFileSync('wwwroot/js/file-operations.js','utf8')});
 const result=await page.evaluate(async()=>{
  const cards=document.querySelectorAll('.directory-card'),transfer=new DataTransfer();
  cards[0].dispatchEvent(new DragEvent('dragstart',{bubbles:true,cancelable:true,dataTransfer:transfer}));
  const observer=new MutationObserver(()=>{});observer.observe(document.querySelector('#gallery-items'),{subtree:true,attributes:true,attributeFilter:['class']});
  const t=performance.now();
  for(let i=0;i<2000;i++)cards[1].querySelector('img').dispatchEvent(new DragEvent('dragover',{bubbles:true,cancelable:true,dataTransfer:transfer}));
  const sameTargetMutations=observer.takeRecords().length;
  cards[2].dispatchEvent(new DragEvent('dragover',{bubbles:true,cancelable:true,dataTransfer:transfer}));
  const changedTargetMutations=observer.takeRecords().length;
  const result={handlerMs:performance.now()-t,sameTargetMutations,changedTargetMutations,highlighted:document.querySelectorAll('.file-drop-target').length,animation:getComputedStyle(cards[2]).animationName};
  document.dispatchEvent(new DragEvent('dragend',{bubbles:true}));observer.disconnect();return result;
 });console.log(JSON.stringify(result));if(process.argv.includes('--assert-fixed')){assert(result.sameTargetMutations<=1);assert(result.changedTargetMutations<=2);assert.equal(result.highlighted,1);assert.equal(result.animation,'none');}
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
