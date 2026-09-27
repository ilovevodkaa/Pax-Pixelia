// node jsdump.js [mockup js dir = ../../docs/mockups/js] [seed = 1337] > reference/js_SEED.txt — prints per-array FNV-1a hashes of the mockup generator's output
// (same format as WorldGenTests --hashes) so the C# port can be compared stage by stage.
const fs=require('fs'),vm=require('vm'),path=require('path');
const dir=process.argv[2]||path.join(__dirname,"../../docs/mockups/js"),seed=+process.argv[3]||1337;
vm.runInThisContext(fs.readFileSync(path.join(dir,'core.js'),'utf8'),{filename:'core.js'});
vm.runInThisContext(fs.readFileSync(path.join(dir,'worldgen.js'),'utf8'),{filename:'worldgen.js'});
vm.runInThisContext(fs.readFileSync(path.join(dir,'fog.js'),'utf8'),{filename:'fog.js'});
const R=s=>vm.runInThisContext(s);
class H{constructor(){this.h=0x811c9dc5}b(v){this.h=Math.imul(this.h^(v&255),16777619)>>>0}i32(v){v|=0;this.b(v);this.b(v>>8);this.b(v>>16);this.b(v>>24)}
  f32(v){const a=new Float32Array([v]);const u=new Uint8Array(a.buffer);for(const x of u)this.b(x)}bytes(ta){const u=new Uint8Array(ta.buffer,ta.byteOffset,ta.byteLength);for(let i=0;i<u.length;i++)this.b(u[i])}
  str(s){for(let i=0;i<s.length;i++){const c=s.charCodeAt(i);this.b(c);this.b(c>>8)}}hex(){return this.h.toString(16).padStart(8,'0')}}
const one=f=>{const h=new H();f(h);return h.hex()};
(async()=>{R(`S=${seed}`);await R('generateWorld')();
  const g=R('({WW,WH,N,P,land,hgt,biome,baseCol,river,riverPaths,prov,pSize,pLand,pCX,pCY,pBiome,pH,pRiver,pCoast,pName,adj,own,pPop,pFert,pRel,pBld,pRes,pCap,pTown,pSlots,pMood,natCap,routes,BLD,ORES})');
  const out=[];const put=(k,v)=>out.push(k+' '+v);
  put('P',g.P);
  for(const k of ['land','hgt','biome','baseCol','river','prov','pSize','pLand','pCX','pCY','pBiome','pH','pRiver','pCoast','pFert'])
    put(k,one(h=>{const a=g[k];if(a instanceof Float32Array||a instanceof Uint8Array||a instanceof Uint32Array)h.bytes(a);else for(const v of a)h.i32(v)}));
  put('adj',one(h=>{for(const l of g.adj){h.i32(l.length);for(const v of l)h.i32(v)}}));
  put('names',one(h=>{for(const s of g.pName){h.str(s);h.str('|')}}));
  put('rivers',one(h=>{h.i32(g.riverPaths.length);for(const r of g.riverPaths){h.i32(r.pts.length);for(const [x,y] of r.pts){h.f32(x);h.f32(y)}h.f32(r.y0);h.f32(r.y1)}}));
  put('caps',one(h=>{for(const v of g.natCap)h.i32(v)})+' '+g.natCap.join(','));
  put('own',one(h=>{for(const v of g.own)h.i32(v)}));
  put('pop',one(h=>h.bytes(g.pPop)));
  put('rel',one(h=>{for(const v of g.pRel)h.i32(v)}));
  put('mood',one(h=>{for(const v of g.pMood)h.i32(v)}));
  put('slots',one(h=>{for(const v of g.pSlots)h.i32(v)}));
  const bk=Object.keys(g.BLD);
  put('bld',one(h=>{for(const l of g.pBld){h.i32(l.length);for(const b of l)h.i32(bk.indexOf(b))}}));
  put('ore',one(h=>{for(const r of g.pRes){h.i32(r?g.ORES.indexOf(r.k):-1);h.i32(r&&r.found?1:0)}}));
  put('cap',one(h=>{for(const v of g.pCap)h.i32(v)}));
  put('town',one(h=>{for(const v of g.pTown)h.i32(v)}));
  put('routes',one(h=>{h.i32(g.routes.length);for(const r of g.routes){h.i32(r.length);for(const v of r)h.i32(v)}}));
  console.log(out.join('\n'));process.exit(0)})().catch(e=>{console.error(e);process.exit(1)});
