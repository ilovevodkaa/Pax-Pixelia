'use strict';
/* Pax Pixelia mockup — core: constants, noise, colour helpers, shared world state.
   Shared globals are declared here; other scripts read/write them (classic scripts share the global lexical scope). */
const WW=2560,WH=1440,N=WW*WH,KS=WW/1024;
/* noise scale: fbm k = cells across the whole world width. Pixel-scale layers use KF(k per 1024px) and pixel amplitudes,
   so their features keep the same size in pixels at any WW (these reproduce the hand-tuned values at WW=1536); continental
   layers (elevation 4, mountain mask 3, moisture 5, temperature 4) keep a fixed k, so continents keep their count and grow
   with the map. eOct adds elevation octaves so the finest coastline detail stays ~6px. */
const KF=k=>Math.max(1,Math.round(k*KS));
const NK={warp:KF(6),warpA:.387*WW/KF(6),ridge:KF(8),hill:KF(24),micro:KF(56),moist:KF(22),temp:KF(18),tone:KF(40),dune:KF(40),jit:KF(64),jitA:4.5,eOct:7+Math.round(Math.log2(KS/1.5))};
let S=1337;
const $=id=>document.getElementById(id);
const fmt=n=>Math.round(n).toLocaleString('ru-RU');

/* ---------- noise ---------- */
function h2(ix,iy,s){let h=(Math.imul(ix,374761393)+Math.imul(iy,668265263)+Math.imul(s,1442695041))|0;h=Math.imul(h^(h>>>13),1274126177);h^=h>>>16;return (h>>>0)/4294967296}
function vn(x,y,p,s){const ix=Math.floor(x),iy=Math.floor(y),fx=x-ix,fy=y-iy,ux=fx*fx*(3-2*fx),uy=fy*fy*(3-2*fy);let x0=ix%p;if(x0<0)x0+=p;const x1=x0+1===p?0:x0+1;const a=h2(x0,iy,s),b=h2(x1,iy,s),c=h2(x0,iy+1,s),d=h2(x1,iy+1,s);return a+(b-a)*ux+(c-a)*uy+(a-b-c+d)*ux*uy}
function fbm(x,y,k,oct,s){let u=x/WW*k,v=y/WW*k,a=.5,sum=0,nm=0,p=k;for(let o=0;o<oct;o++){sum+=a*vn(u,v,p,s+o*101);nm+=a;a*=.5;u*=2;v*=2;p*=2}return sum/nm}
const sm=(a,b,x)=>{let t=(x-a)/(b-a);t=t<0?0:t>1?1:t;return t*t*(3-2*t)};
const cl=v=>v<0?0:v>255?255:v|0;
const pack=(r,g,b)=>((0xff000000|(cl(b)<<16)|(cl(g)<<8)|cl(r))>>>0);
const dk=(c,f)=>pack((c&255)*f,((c>>8)&255)*f,((c>>16)&255)*f);
const lt=(c,f)=>{const r=c&255,g=(c>>8)&255,b=(c>>16)&255;return pack(r+(255-r)*f,g+(255-g)*f,b+(255-b)*f)};

/* ---------- world data ---------- */
const BN=['Вода','Ледник','Горные пики','Горы','Тундра','Тайга','Холодная степь','Болото','Лес','Луга','Равнина','Степь','Джунгли','Саванна','Пустыня'];
const BC=[[0,0,0],[214,221,228],[232,236,240],[122,116,108],[128,134,112],[58,84,66],[142,142,102],[74,88,64],[64,102,60],[110,144,74],[144,156,88],[170,160,100],[48,98,56],[172,154,86],[200,180,128]];
const FERT=[0,0,0,.05,.15,.35,.4,.4,.6,.9,.85,.55,.5,.5,.1];
const CLIM=b=>b===1||b===2||b===4?'полярный':b===5||b===6?'холодный':b>=12?'тропический':b===3?'горный':'умеренный';
const SYL=['ар','да','мир','ра','кес','ол','ва','тор','ин','ска','лу','бра','вен','ти','го','ря','зан','ель','мо','ши','ка','лин','дор','ас','эн','ул','ор','не','ви','са','ром','ли','тас','хе','бар','ну','гел','сим','та','ур','ис','ма','кор','те','вил','мер','сан','до','рик','ла'];
const SUF=['','','','ия','ск','ов','ин','ара','ет','он','ея'];
function mkName(k){const n=2+(h2(k,11,S)<.3?1:0);let s='';for(let j=0;j<n;j++)s+=SYL[(h2(k,20+j,S)*SYL.length)|0];s+=SUF[(h2(k,31,S)*SUF.length)|0];return s[0].toUpperCase()+s.slice(1)}
const NAT=[['Ардания','Вождество',[190,72,60],'арданская'],['Кесарат Мирры','Кесарат',[72,112,182],'мирранская'],['Торн','Племенной союз',[112,152,58],'торнская'],['Ксилия','Царство',[182,130,72],'ксильская'],['Лура','Вольные города',[70,164,154],'лурская'],['Ун','Жреческое государство',[140,92,172],'унская'],['Вения','Держава',[200,142,46],'венская'],['Скаллия','Вождество',[92,140,96],'скальская'],['Ольмерия','Царство',[180,96,124],'ольмерская'],['Бразан','Каганат',[106,112,166],'бразанская'],['Эльдора','Вождество',[158,164,74],'эльдорская'],['Гошар','Племенной союз',[164,92,58],'гошарская'],['Тасмир','Город-государство',[58,140,182],'тасмирская'],['Роменна','Царство',[186,156,108],'роменская'],['Нирея','Вождество',[108,178,124],'нирейская'],['Барахия','Жреческое государство',[142,58,92],'барахская']];
const REL=[['Культ Солнца',[214,176,84]],['Путь Мирры',[152,114,208]],['Древние духи',[112,162,112]],['Огненный завет',[216,112,64]]];
const NREL=[0,1,2,2,0,3,2,2,1,2,2,3,0,2,2,3];
const CLS=[['Общинники','#7fa35a'],['Рабы','#7d7a74'],['Жрецы','#d0ad5c'],['Знать','#b85c52']];
const BLD={farm:['Ферма','ti-plant'],lumb:['Лесопилка','ti-trees'],quar:['Каменоломня','ti-pick'],fish:['Рыбацкая пристань','ti-anchor'],past:['Пастбище','ti-paw'],shrn:['Святилище','ti-sun'],mark:['Рынок','ti-building-store'],gran:['Амбар','ti-building-warehouse']};
const ORES=['Медь','Олово','Железо','Золото','Соль'];

let land,hgt,biome,baseCol,river,riverPaths=[],prov,P=0;
let pSize,pLand,pCX,pCY,pBiome,pH,pRiver,pCoast,pName,adj,own,fog,pPop,pFert,pRel,pBld,pRes,pCap,pTown,pSlots,pMood;
let natCap=[],natCol=[],natBrd=[],routes=[];
