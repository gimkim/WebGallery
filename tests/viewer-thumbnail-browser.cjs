const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const {chromium} = require(require.resolve('playwright', {paths:[process.env.CODEX_NODE_MODULES || 'C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 try {
  const page=await browser.newPage();
  const source=fs.readFileSync(path.join(__dirname,'../wwwroot/js/site.js'),'utf8');
  const fn=source.slice(source.indexOf('  function snapshotViewerThumbnail('),source.indexOf('  function showViewerImage('));
  await page.setContent('<img id="tile"><img id="preview">');
  await page.addScriptTag({content:fn});
  const result=await page.evaluate(async()=>{
   const tile=document.querySelector('#tile');
   const canvas=document.createElement('canvas'); canvas.width=48;canvas.height=36;
   canvas.getContext('2d').fillRect(0,0,48,36);
   const blob=await new Promise(resolve=>canvas.toBlob(resolve));
   const url=URL.createObjectURL(blob); tile.src=url;await tile.decode();URL.revokeObjectURL(url);
   const old=new Image();old.src=url;let oldFailed=false;try{await old.decode();}catch{oldFailed=true;}
   const preview=document.querySelector('#preview');preview.src=snapshotViewerThumbnail(tile);await preview.decode();
   return {oldFailed,width:preview.naturalWidth,height:preview.naturalHeight,empty:snapshotViewerThumbnail(new Image())};
  });
  assert.deepEqual(result,{oldFailed:true,width:48,height:36,empty:null});
  console.log('PASS in Edge: old revoked Blob reuse fails; decoded-pixel snapshot renders; missing tile safely falls back.');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
