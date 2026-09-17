const fs=require('node:fs'),assert=require('node:assert/strict');
const {chromium}=require(require.resolve('playwright',{paths:['C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules']}));
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:5294/Account/Login');await page.locator('[name=UserName]').fill('admin');await page.locator('[name=Password]').fill(fs.readFileSync('App_Data/spa-smoke/bootstrap-admin.txt','utf8').match(/Password: (.+)/)[1].trim());await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL('http://127.0.0.1:5294/');
 for(const width of [1600,1000,390]){
  await page.setViewportSize({width,height:900});await page.goto('http://127.0.0.1:5294/?path=%40root-1%2Fpaging-fixture');
  if(width===390)await page.evaluate(()=>window.scrollTo(0,800));await page.waitForTimeout(350);
  const measure=()=>page.evaluate(()=>[...document.querySelectorAll('.gallery-toolbar-main,.sticky-folder-actions,.mobile-sticky-summary')].filter(e=>e.getBoundingClientRect().height).map(e=>{const r=e.getBoundingClientRect();return [e.className,r.x,r.y,r.width,r.height];}));
  const before=await measure();await page.evaluate(()=>{const input=document.querySelector('.file-select');input.checked=true;input.dispatchEvent(new Event('change',{bubbles:true}));});await page.waitForTimeout(150);const after=await measure();
  assert.equal(after.length,before.length);before.forEach((row,i)=>row.slice(1).forEach((v,j)=>assert(Math.abs(v-after[i][j+1])<1,`toolbar moved at ${width}: ${JSON.stringify({before,after})}`)));
  assert(await page.locator('#selection-toolbar').isVisible());await page.locator('#clear-selection').click();assert(!(await page.locator('#selection-toolbar').isVisible()));
 }
 assert.deepEqual(errors,[]);console.log('PASS stable toolbar positions at desktop/tablet/mobile; selection row appears and clears');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
