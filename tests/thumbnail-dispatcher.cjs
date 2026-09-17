const fs = require('node:fs'), vm = require('node:vm'), assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../wwwroot/js/site.js'), 'utf8');
const limit = Number(source.match(/const maximumThumbnailRequests = (\d+);/)[1]);
const code = source.slice(source.indexOf('  function pumpThumbnailQueue()'), source.indexOf('  function resumeThumbnailRequests()'));
const images = Array.from({length:70}, (_,i)=>({dataset:{thumbnailSrc:`/thumb/${i}`}}));
const states = new Map(images.map(image=>[image,{visible:true,queued:true,loaded:false,assigned:false}]));
states.get(images[0]).visible = false;
const calls=[];
const ctx={maximumThumbnailRequests:limit,activeThumbnailRequests:0,pendingThumbnails:[...images],thumbnailRequestsSuspended:false,
 thumbnailImages:images,thumbnailStates:states,thumbnailResumeTimer:null,thumbnailHost:()=>null,AbortController,setTimeout,clearTimeout,
 assignThumbnailBlob:(image,state)=>{state.assigned=true;},setThumbnailError:()=>{},queueThumbnail:()=>{throw Error('Unexpected resume');},
 pumpMediumQueue:()=>{},
 fetch:(url,opts)=>new Promise((resolve,reject)=>{calls.push({url,resolve,signal:opts.signal});opts.signal.addEventListener('abort',()=>reject(Object.assign(new Error('aborted'),{name:'AbortError'})));})};
vm.createContext(ctx);vm.runInContext(code,ctx);
(async()=>{
 assert.equal(limit,32);
 ctx.pumpThumbnailQueue();assert.equal(calls.length,32);assert.equal(ctx.activeThumbnailRequests,32);
 assert(!calls.some(c=>c.url==='/thumb/0'),'off-screen entry must not start');
 calls[0].resolve({ok:true,blob:async()=>({})});await new Promise(setImmediate);
 assert.equal(calls.length,33,'one completion immediately fills a slot, no batch barrier');assert.equal(ctx.activeThumbnailRequests,32);
 ctx.cancelThumbnailRequests(false);await new Promise(setImmediate);
 assert.equal(ctx.activeThumbnailRequests,0);assert.equal(ctx.pendingThumbnails.length,0);assert.equal(calls.length,33);
 assert(calls.every(c=>c.signal.aborted || c===calls[0]));
 console.log('PASS: 32-request bound, off-screen skip, rolling slot refill, navigation/viewer cancellation without resume. Mocked dispatcher, not live throughput.');
})().catch(e=>{console.error(e);process.exitCode=1;});
