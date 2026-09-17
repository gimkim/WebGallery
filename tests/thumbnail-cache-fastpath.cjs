const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('wwwroot/js/site.js','utf8');
const block=source.slice(source.indexOf('  const thumbnailStates ='),source.indexOf("  document.addEventListener('click', event => {",source.indexOf('  const thumbnailStates =')));
let observer;const calls=[],blocked=[];
const images=Array.from({length:160},(_,i)=>{
 const handlers={};return {dataset:{thumbnailSrc:'/Thumbnail?path='+i},naturalWidth:480,naturalHeight:360,classList:{add(){},remove(){}},
 closest(){return {classList:{add(){},remove(){}}};},getBoundingClientRect(){return {width:100,height:75};},
 addEventListener(name,fn){(handlers[name]??=[]).push(fn);},set src(value){queueMicrotask(()=>handlers.load?.forEach(fn=>fn()));}};
});
class Observer{constructor(fn){observer=fn;}observe(){}}
const cacheBlob={};
const ctx={document:{querySelectorAll:()=>images},window:{location:{href:'https://fixture/'},IntersectionObserver:Observer,devicePixelRatio:1},IntersectionObserver:Observer,ResizeObserver:undefined,
 URL:class extends URL{static createObjectURL(){return 'blob:fixture';}static revokeObjectURL(){}},AbortController,setTimeout,clearTimeout,requestAnimationFrame:fn=>setTimeout(fn,16),
 fetch:(url,opts)=>{calls.push({url,opts});if(opts.cache==='only-if-cached')return Promise.resolve({ok:!url.includes('uncached'),status:200,blob:async()=>cacheBlob});return new Promise(resolve=>blocked.push(resolve));}};
vm.createContext(ctx);vm.runInContext(block+'\nglobalThis.medium=fetchMediumThumbnail;',ctx);
(async()=>{
 observer(images.map(target=>({target,isIntersecting:true})));
 await new Promise(setImmediate);
 assert.equal(calls.filter(c=>c.opts.cache==='only-if-cached').length,160);
 assert.equal(calls.filter(c=>c.opts.cache!=='only-if-cached').length,0);
 console.log('PASS 160 regular cached thumbnails bypass network queue, zero network requests');
 for(let i=0;i<4;i++)ctx.medium('/Thumbnail?path=uncached'+i,new AbortController().signal);
 await new Promise(setImmediate);assert.equal(blocked.length,4);
 const hits=await Promise.all(Array.from({length:160},(_,i)=>ctx.medium('/Thumbnail?path='+i,new AbortController().signal)));
 assert(hits.every(x=>x===cacheBlob));assert.equal(blocked.length,4);
 console.log('PASS 160 medium cache hits complete while all four generation/network slots remain blocked');
 blocked.forEach(resolve=>resolve({ok:false,status:204}));
})().catch(e=>{console.error(e);process.exitCode=1;});
