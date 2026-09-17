window.GalleryPages.mounts.push(scope => {
  const {document, window, fetch, IntersectionObserver, requestAnimationFrame} = scope;
  const grid = document.querySelector('#gallery-items'), rail = document.querySelector('.gallery-jump-rail');
  if (!grid || !rail || !grid.dataset.streamRevision) return;
  let first = +grid.dataset.streamPage, last = first, busy = false, failed = false;
  const pages = +grid.dataset.streamPages, revision = grid.dataset.streamRevision;
  const prev = grid.querySelector('[data-stream-prev]'), next = grid.querySelector('[data-stream-next]');
  prev.querySelector('button').hidden = true;
  let edgeFrame = 0;
  const urlFor = page => { const u = new URL(grid.dataset.streamUrl, location.href); u.searchParams.set('page',page); return u; };
  const visible = new Map();
  const positionObserver = new IntersectionObserver(entries => {
    for (const e of entries) { if (e.isIntersecting) visible.set(e.target,+e.target.dataset.ordinal); else visible.delete(e.target); }
    const offset = Math.min(...visible.values());
    let active;
    rail.querySelectorAll('[data-jump-offset]').forEach(a => { a.removeAttribute('aria-current'); if (+a.dataset.jumpOffset <= offset) active=a; });
    if (active && Number.isFinite(offset)) { active.setAttribute('aria-current','true'); rail.querySelector('[data-stream-position]').textContent = `${offset+1} / ${grid.dataset.streamTotal}`; }
  }, {rootMargin:'-130px 0px 0px 0px'});
  const observe = root => root.querySelectorAll('[data-ordinal]').forEach(card=>positionObserver.observe(card));
  observe(grid);
  async function load(previous=false) {
    if (busy || (previous ? first<=1 : last>=pages)) return;
    busy=true; failed=false;
    const edge=previous?prev:next, button=edge.querySelector('button'), status=edge.querySelector('span');
    button.disabled=true; button.hidden=previous; status.textContent='Loading…';
    const skeleton=document.createElement('div'); skeleton.className='stream-skeleton'; skeleton.setAttribute('aria-hidden','true');
    const count=Math.min(24, +grid.dataset.streamTotal-(previous?0:last*500));
    for(let i=0;i<count;i++){ const tile=document.createElement('div'); skeleton.append(tile); }
    if (!previous) edge.after(skeleton);
    try {
      const response=await fetch(urlFor(previous?first-1:last+1),{headers:{'X-Gallery-Page':'1','X-Gallery-Chunk':'1'}});
      if(!response.ok || !response.headers.get('content-type')?.includes('application/vnd.webgallery.page+json')) throw Error('Could not load items. Please sign in or retry.');
      const body=await response.json(), parsed=new DOMParser().parseFromString(body.html,'text/html');
      const source=parsed.querySelector('#gallery-items'), chunk=source?.querySelector('.gallery-chunk');
      if (!chunk || source.dataset.streamRevision!==revision) throw Error('Folder changed. Refresh to load the updated order.');
      skeleton.remove();
      const anchor=grid.querySelector('[data-ordinal]'), before=anchor?.getBoundingClientRect().top;
      if(previous){prev.after(chunk);first--;}else{next.before(chunk);last++;}
      document.dispatchEvent(new CustomEvent('gallery:items-added',{detail:{root:chunk}})); observe(chunk);
      prev.hidden=first<=1; next.hidden=last>=pages; status.textContent='';
      if(previous && anchor) window.scrollBy(0,anchor.getBoundingClientRect().top-before);
    } catch(error) { if(error.name!=='AbortError'){failed=true;status.textContent=error.message;button.textContent='Retry';button.hidden=false;} }
    finally { skeleton.remove(); button.disabled=false;busy=false;if(!failed)requestAnimationFrame(checkEdges); }
  }
  prev.querySelector('button').addEventListener('click',()=>load(true));
  next.querySelector('button').addEventListener('click',()=>load());
  function checkEdges() {
    edgeFrame=0;
    if(busy||failed||document.querySelector('dialog[open]'))return;
    const near=edge=>!edge.hidden && edge.getBoundingClientRect().top<window.innerHeight+400 && edge.getBoundingClientRect().bottom>0;
    if(near(prev))load(true);else if(near(next))load();
  }
  const edgeObserver=new IntersectionObserver(checkEdges,{rootMargin:'400px'});
  edgeObserver.observe(next);edgeObserver.observe(prev);
  window.addEventListener('scroll',()=>{if(!edgeFrame)edgeFrame=requestAnimationFrame(checkEdges);},{passive:true});
  rail.addEventListener('click',e=>{
    const link=e.target.closest('[data-jump-offset]'); if(!link||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey||e.button!==0)return;
    const card=document.getElementById('gallery-item-'+link.dataset.jumpOffset);
    if(card){ e.preventDefault();e.stopPropagation();card.scrollIntoView({block:'start'}); }
  });
  fetch(rail.dataset.manifestUrl).then(async response=>{
    if(!response.ok)return;const manifest=await response.json();
    if(manifest.revision!==revision)return;
    const nav=rail.querySelector('nav'); nav.replaceChildren();
    for(const section of manifest.sections){const a=document.createElement('a');const u=urlFor(Math.floor(section.offset/manifest.pageSize)+1);u.hash='gallery-item-'+section.offset;a.href=u.href;a.dataset.jumpOffset=section.offset;a.textContent=section.label;a.title=`${section.count} items · ${section.offset+1}–${section.offset+section.count}`;nav.append(a);}
  }).catch(()=>{});
  const jump=()=>{const id=location.hash.slice(1);if(id.startsWith('gallery-item-'))requestAnimationFrame(()=>document.getElementById(id)?.scrollIntoView({block:'start'}));};
  document.addEventListener('gallery:navigation-end',jump);jump();
});
