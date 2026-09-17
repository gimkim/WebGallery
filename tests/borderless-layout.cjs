// Isolated rendering fixture: real stylesheet, representative Gallery markup.
// Does not exercise authentication, filesystem requests or the live NAS.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require(require.resolve('playwright', { paths: [process.env.CODEX_NODE_MODULES || 'C:/Users/tatsa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'] }));
(async () => {
  const browser = await chromium.launch({channel:'msedge', headless:true});
  try {
    const page = await browser.newPage();
    const cards = Array.from({length:24}, (_,i) => `<article class="gallery-card file-card" data-item-kind="image"><label class="select-toggle"><input type="checkbox" aria-label="Select image ${i}"/><span>✓</span></label><button class="media image-button" aria-label="Open image ${i}"><img alt="Landscape ${i}" src="data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="640" height="480"><rect width="640" height="480" fill="hsl(${180+i*13} 23% 32%)"/><circle cx="490" cy="110" r="50" fill="#e8d5aa"/><path d="M0 480V330L180 180L360 400L500 260L640 400V480" fill="#233c42"/></svg>`)}"/></button><div class="item-info"><a class="file-name-download" href="#">Landscape-${i}.jpg</a><small>12 MB</small></div></article>`).join('');
    await page.setContent(`<html><head><meta name="viewport" content="width=device-width,initial-scale=1"/><style>${fs.readFileSync(path.join(__dirname,'../wwwroot/css/site.css'),'utf8')}</style></head><body class="photo-gallery"><header class="topbar"><a class="brand"><span class="brand-mark">G</span>Gallery</a><nav class="topnav"><span class="user-chip">Photographer</span><button class="link-button">Sign out</button></nav></header><nav class="library-nav"><span class="library-caption">LIBRARY</span><a class="active"><span>▧</span>Photos</a><a><span>▤</span>Collections</a><a><span>⚙</span>Management</a></nav><main class="page-shell"><section class="gallery-heading"><h1>Weekend memories</h1></section><section class="gallery-toolbar panel"><div class="gallery-toolbar-main"><label class="gallery-search"><input placeholder="Search current folder..."/></label><button>All</button><button>Images</button><select aria-label="Sort"><option>Name</option></select><button>Grid</button></div></section><section class="gallery-grid" style="--columns:4">${cards}</section></main></body></html>`);
    for (const width of [1440,768,390]) {
      await page.setViewportSize({width,height:960});
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `Overflow at ${width}`);
      assert.equal(await page.locator('.gallery-card').first().evaluate(el=>getComputedStyle(el).borderWidth),'0px');
      assert.equal(await page.locator('.media').first().evaluate(el=>getComputedStyle(el).borderRadius),'0px');
      assert.equal(await page.locator('.media img').first().evaluate(el=>getComputedStyle(el).objectFit),'contain');
      await page.screenshot({path:path.join(process.env.TEMP,`webgallery-borderless-${width}.png`),fullPage:true});
    }
    await page.setViewportSize({width:1440,height:960});
    await page.locator('.file-name-download').first().focus();
    await page.waitForTimeout(200);
    assert.equal(await page.locator('.item-info').first().evaluate(el=>getComputedStyle(el).opacity),'1');
    await page.locator('.gallery-grid').evaluate(el=>el.classList.add('list-view'));
    assert.equal(await page.locator('.item-info').first().evaluate(el=>getComputedStyle(el).position),'static');
    console.log('PASS: desktop/tablet/mobile no overflow; borderless, uncropped images; keyboard filename reveal; List metadata layout. Screenshots in TEMP. Fixture only.');
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
