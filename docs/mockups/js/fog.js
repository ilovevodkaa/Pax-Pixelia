'use strict';
/* Fog of war. fog[p]: 0 unexplored, 1 explored (memory, not seen now), 2 visible. expl[p] = ever seen (persists).
   Vision: own provinces r2 (capital r3), player trade routes r1, scouts r2. Per-province pixel index + cloud texture
   allow incremental repaint of mb (paintRect/paintProv/modeParams live in render.js). Scouts walk BFS paths over land. */
let fogOn=true,expl=null,metA=null,pxOff=null,pxList=null,pBx0=null,pBx1=null,pBy0=null,pBy1=null,fogN=null,fogT=null,rvm=[];
let scouts=[],scoutPick=false,scoutSeq=0,scLast=0;
const fogMs={mode:0,inc:0,idx:0,tex:0,n:0};
const FB=8,FA=4,FDC=FB/2+FA,SC_MAX=2,SC_R=2,SC_AUTO=36,SC_SPD=[0,.5,1,1.7,2.6,4];
const met=n=>!fogOn||!!(metA&&metA[n]);
/* multi-source BFS with per-source range: src=[[p,r],...] -> rem[p] (remaining range, -1 = not reached). Sea zones are ~4x larger: cost 2 */
function reach(src){const rem=new Int8Array(P).fill(-1),B=[];for(const[p,r]of src)if(r>rem[p]){rem[p]=r;(B[r]||(B[r]=[])).push(p)}
  for(let r=B.length-1;r>0;r--){const b=B[r];if(b)for(const p of b){if(rem[p]!==r)continue;for(const n of adj[p]){const m=r-(pLand[n]?1:2);if(m>=0&&rem[n]<m){rem[n]=m;(B[m]||(B[m]=[])).push(n)}}}}
  return rem}
function visSrc(){const s=[],c=natCap[0];for(let p=0;p<P;p++)if(own[p]===0)s.push([p,p===c?3:2]);
  for(const rt of routes)if(rt[0]===c||rt[rt.length-1]===c)for(const p of rt){s.push([p,1]);for(const n of adj[p])s.push([n,0])}
  for(const sc of scouts)s.push([sc.path[sc.k],SC_R]);return s}
/* recompute visibility; returns flat [p,oldState,...] of changed provinces; newly met nations -> fogMet */
let fogMet=[];
function visFog(){const rem=reach(visSrc()),ch=[],om=metA.slice();
  for(let p=0;p<P;p++)if(rem[p]>=0)expl[p]=1;
  for(let p=0;p<P;p++)if(!expl[p]&&adj[p].every(q=>expl[q]))expl[p]=1;   // enclosed pockets
  for(let p=0;p<P;p++){const v=rem[p]>=0?2:expl[p]?1:0;if(v!==fog[p]){ch.push(p,fog[p]);fog[p]=v}}
  metA.fill(0);for(let p=0;p<P;p++)if(fog[p]&&own[p]>=0)metA[own[p]]=1;
  fogMet=[];for(let n=0;n<metA.length;n++)if(metA[n]&&!om[n])fogMet.push(n);return ch}
/* full reset after world generation: explored = depth<=6 around the player */
function computeFog(){expl=new Uint8Array(P);fog=new Uint8Array(P);metA=new Uint8Array(NAT.length);scouts=[];scoutPick=false;
  const src=[];for(let p=0;p<P;p++)if(own[p]===0)src.push([p,6]);const r=reach(src);for(let p=0;p<P;p++)if(r[p]>=0)expl[p]=1;
  visFog();fogMet=[];riverMask()}
function riverMask(){rvm=riverPaths.map(rp=>{const m=new Uint8Array(rp.pts.length);let any=0;
  for(let k=0;k<m.length;k++){let x=Math.floor(rp.pts[k][0])%WW;if(x<0)x+=WW;const y=Math.min(WH-1,Math.max(0,Math.floor(rp.pts[k][1])));if(fog[prov[y*WW+x]]){m[k]=1;any=1}}
  m.any=any;return m})}
/* per-province pixel index (counting sort) + bbox in x unwrapped around pCX */
function buildPxIndex(){pxOff=new Int32Array(P+1);pxList=new Int32Array(N);for(let i=0;i<N;i++)pxOff[prov[i]+1]++;for(let p=0;p<P;p++)pxOff[p+1]+=pxOff[p];
  const w=pxOff.slice(0,P);for(let i=0;i<N;i++)pxList[w[prov[i]]++]=i;
  pBx0=new Int32Array(P);pBx1=new Int32Array(P);pBy0=new Int32Array(P);pBy1=new Int32Array(P);const H=WW>>1;
  for(let p=0;p<P;p++){const c=pCX[p],a=pxOff[p],e=pxOff[p+1];let x0=1e9,x1=-1e9;
    for(let k=a;k<e;k++){const i=pxList[k];let x=i%WW-c;if(x>H)x-=WW;else if(x<-H)x+=WW;if(x<x0)x0=x;if(x>x1)x1=x}
    pBx0[p]=c+x0;pBx1[p]=c+x1;pBy0[p]=(pxList[a]/WW)|0;pBy1[p]=(pxList[e-1]/WW)|0}}
/* cloud texture on 2x2 blocks: fogN = rank-normalised noise 0..255, fogT = tone index lvl*3+(0 flat,1 lit top,2 shaded bottom) */
function buildFogTex(){const bw=WW>>1,bh=(WH+1)>>1,n=bw*bh,k=Math.max(2,Math.round(WW/96)),kw=Math.max(1,Math.round(WW/512)),v=new Float32Array(n);let mn=9,mx=-9;
  const G=32,gw=Math.ceil(WW/G),gh=Math.ceil(WH/G)+1,WX=new Float32Array(gw*gh),WY=new Float32Array(gw*gh);   // low-freq domain warp sampled on a 32px grid
  for(let gy=0;gy<gh;gy++)for(let gx=0;gx<gw;gx++){WX[gy*gw+gx]=60*(fbm(gx*G,gy*G,kw,2,S+411)-.5);WY[gy*gw+gx]=60*(fbm(gx*G,gy*G,kw,2,S+412)-.5)}
  for(let by=0;by<bh;by++){const y=by*2,gy=Math.min(gh-2,(y/G)|0),fy=y/G-gy;
    for(let bx=0;bx<bw;bx++){const x=bx*2,gx=(x/G)|0,fx=x/G-gx,ex=1-fx,ey=1-fy,g0=gy*gw+gx,g1=gy*gw+(gx+1)%gw,
      ox=(WX[g0]*ex+WX[g1]*fx)*ey+(WX[g0+gw]*ex+WX[g1+gw]*fx)*fy,oy=(WY[g0]*ex+WY[g1]*fx)*ey+(WY[g0+gw]*ex+WY[g1+gw]*fx)*fy,e=fbm(x+ox,y+oy,k,3,S+410);
      v[by*bw+bx]=e;if(e<mn)mn=e;if(e>mx)mx=e}}
  const H=new Float32Array(1025),sc=1024/(mx-mn||1);for(let i=0;i<n;i++)H[((v[i]-mn)*sc)|0]++;for(let b=1;b<1025;b++)H[b]+=H[b-1];for(let b=0;b<1025;b++)H[b]/=n;
  fogN=new Uint8Array(n);fogT=new Uint8Array(n);const L=new Uint8Array(n);
  const TH=[0,.36,.64,.86,1];
  for(let i=0;i<n;i++){const u=H[((v[i]-mn)*sc)|0];fogN[i]=(u*255)|0;L[i]=u<TH[1]?0:u<TH[2]?1:u<TH[3]?2:3}
  for(let by=0;by<bh;by++)for(let bx=0;bx<bw;bx++){const i=by*bw+bx,l=L[i],xl=by*bw+(bx?bx-1:bw-1),f=(fogN[i]/255-TH[l])/(TH[l+1]-TH[l]);
    const ld=l<3&&(f-.62)/.38>BAY[((by&3)<<2)|(bx&3)]?l+1:l;   // ordered-dither the top of each level into the next one
    fogT[i]=ld*3+(by&&L[i-bw]<l?1:(by>1&&L[xl-2*bw]>l)||(by&&L[xl-bw]>l)?2:0)}}
function initFog(){const t0=performance.now();fogMs.inc=fogMs.n=0;computeFog();buildPxIndex();const t1=performance.now();buildFogTex();fogMs.idx=t1-t0;fogMs.tex=performance.now()-t1}
/* incremental: recompute vision, repaint only changed provinces (+ dither band around newly explored ones) */
function updateFog(){if(!expl||!P)return 0;const t0=performance.now(),ch=visFog();if(!ch.length)return 0;const nw=[];
  for(let k=0;k<ch.length;k+=2)if(!ch[k+1])nw.push(ch[k]);
  if(nw.length)riverMask();
  if(fogOn){modeParams();for(let k=0;k<ch.length;k+=2)if(ch[k+1])paintProv(ch[k]);if(nw.length)paintAround(nw);drawMini();drawMiniView()}
  dirtyMap=dirtyOv=true;fogMs.inc=performance.now()-t0;fogMs.n=ch.length/2;
  if(fogMet.length&&typeof fogEvent==='function')fogEvent('met',fogMet);return nw.length}
function paintAround(ps){const cl=[];
  for(const p of ps){const x0=pBx0[p],x1=pBx1[p],y0=pBy0[p],y1=pBy1[p];let hit=false;
    for(const c of cl){const o=Math.round((pCX[p]-c[4])/WW)*WW,a=Math.min(c[0],x0-o),b=Math.max(c[1],x1-o),u=Math.min(c[2],y0),v=Math.max(c[3],y1);
      if(b-a<384&&v-u<384){c[0]=a;c[1]=b;c[2]=u;c[3]=v;hit=true;break}}
    if(!hit)cl.push([x0,x1,y0,y1,pCX[p]])}
  const m=FDC+1;for(const c of cl)paintRect(c[0]-m,c[2]-m,c[1]+m+1,c[3]+m+1)}

/* ---------- scouts ---------- */
function landBfs(src){const dist=new Int32Array(P).fill(-1),prev=new Int32Array(P).fill(-1),q=[];for(const s of src)if(dist[s]<0){dist[s]=0;q.push(s)}
  for(let h=0;h<q.length;h++){const p=q[h];for(const n of adj[p])if(dist[n]<0&&pLand[n]){dist[n]=dist[p]+1;prev[n]=p;q.push(n)}}return{dist,prev,q}}
function trace(B,t){const r=[];for(let p=t;p>=0;p=B.prev[p])r.push(p);return r.reverse()}
/* nearest unexplored land, pushed away from where the other scouts are and are heading */
function autoTarget(from,self){const B=landBfs([from]),o=scouts.filter(s=>s!==self),O=o.length?landBfs(o.flatMap(s=>[s.path[s.k],s.path[s.path.length-1]])):null;let best=-1,bs=1e9;
  for(const p of B.q){if(expl[p])continue;const od=O?(O.dist[p]<0?8:Math.min(8,O.dist[p])):8,s=B.dist[p]-.75*od;if(s<bs){bs=s;best=p}}
  return best<0?null:trace(B,best)}
/* t<0 = auto. returns '' on success or an error key */
function sendScout(t){if(scouts.length>=SC_MAX)return 'max';const c=natCap[0];let path;
  if(t<0){path=autoTarget(c,null);if(!path)return 'none'}
  else{if(!pLand[t])return 'sea';if(t===c)return 'here';const B=landBfs([c]);if(B.dist[t]<0)return 'far';path=trace(B,t)}
  const s={id:++scoutSeq,path,k:0,t:0,auto:t<0,steps:0,found:0};scouts.push(s);s.found+=updateFog();dirtyOv=true;return ''}
function scoutDone(s){scouts=scouts.filter(x=>x!==s);updateFog();dirtyOv=true;if(typeof fogEvent==='function')fogEvent('done',s)}
function scoutTick(now){const dt=scLast?Math.min(100,now-scLast):0;scLast=now;if(!scouts.length||paused)return;
  const v=dt*SC_SPD[speed]/600;let stepped=false;
  for(const s of scouts.slice()){s.t+=v;
    while(s.t>=1){s.t-=1;s.k++;s.steps++;stepped=true;s.found+=updateFog();const at=s.path[s.k];
      if(s.auto){if(s.steps>=SC_AUTO){scoutDone(s);break}
        if(s.k>=s.path.length-1||expl[s.path[s.path.length-1]]){const np=autoTarget(at,s);if(!np||np.length<2){scoutDone(s);break}s.path=np;s.k=0}}
      else if(s.k>=s.path.length-1){scoutDone(s);break}}
    if(s.k>=s.path.length-1)s.t=0}
  if(stepped&&typeof fogEvent==='function')fogEvent('step')}
