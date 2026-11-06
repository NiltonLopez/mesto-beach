const fs=require("fs"), path=require("path");
const OUT=path.join(__dirname,"..","frontend","apps","menu","public");
const ORO="#AA8453", INK="#2A2118";
const slug=s=>s.normalize("NFD").replace(/[\u0300-\u036f]/g,"").toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-|-$/g,"");
const TINT={
  "Entrantes":["#F7F1E7","#ECE1CE"],
  "Del Mar":["#EFEDE6","#DEE4DC"],
  "La Parrilla":["#F4E9D8","#E7D1B4"],
  "Cocteles":["#F7F2EC","#EEE6D6"],
  "Bebidas":["#F5F1EA","#E8E0D2"],
};
const ITEMS=[
 ["Gambas al Pil Pil","Entrantes"],["Ensalada Marinera","Entrantes"],
 ["Ceviche de la Casa","Del Mar"],["Fritura Marinera","Del Mar"],
 ["Espeto de Sardinas","La Parrilla"],["Pulpo a la Brasa","La Parrilla"],["Entrecot de Retinto","La Parrilla"],
 ["Mojito de la Casa","Cocteles"],["Sangria de Cava (jarra)","Cocteles"],
 ["Tinto de Verano","Bebidas"],["Agua Mineral","Bebidas"],
];
const hash=s=>{let h=0;for(const c of s)h=(h*31+c.charCodeAt(0))>>>0;return h;};
function wrap(name){
  if(name.length<=17) return [name];
  const w=name.split(" "); let a="",b="";
  for(const t of w){ if((a+" "+t).trim().length<=Math.ceil(name.length/2)) a=(a+" "+t).trim(); else b=(b+" "+t).trim(); }
  return b?[a,b]:[a];
}
const esc=s=>s.replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;");
function tile(name,cat){
  const [c1,c2]=TINT[cat]||["#F7F1E7","#ECE1CE"];
  const h=hash(name);
  const sunX=150+(h%110), sunY=96+((h>>3)%26), sunR=30+((h>>6)%16);
  const horizon=178+((h>>9)%10);
  let dots=""; for(let i=0;i<70;i++){const x=(i*137+(h%50))%400, y=horizon+8+((i*89)%(300-horizon-8)); dots+='<circle cx="'+x+'" cy="'+y+'" r="1" fill="'+ORO+'" opacity="0.06"/>';}
  const lines=wrap(name);
  const fsz = lines.length>1?21:(name.length>13?23:26);
  const baseY = 250 - (lines.length-1)*13;
  const nameSvg=lines.map((l,i)=>'<text x="200" y="'+(baseY+i*(fsz+4))+'" text-anchor="middle" font-family="Georgia, serif" font-size="'+fsz+'" fill="'+INK+'" letter-spacing="0.01em">'+esc(l)+'</text>').join("");
  return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300" width="400" height="300" role="img" aria-label="'+esc(name)+'">'
   +'<defs>'
   +'<linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="'+c1+'"/><stop offset="1" stop-color="'+c2+'"/></linearGradient>'
   +'<linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="'+ORO+'" stop-opacity="0.14"/><stop offset="1" stop-color="'+ORO+'" stop-opacity="0"/></linearGradient>'
   +'<radialGradient id="sun" cx="0.5" cy="0.5" r="0.5"><stop offset="0" stop-color="'+ORO+'" stop-opacity="0.30"/><stop offset="1" stop-color="'+ORO+'" stop-opacity="0.05"/></radialGradient>'
   +'</defs>'
   +'<rect width="400" height="300" fill="url(#g)"/>'
   +'<rect x="0" y="0" width="400" height="'+horizon+'" fill="url(#sea)"/>'
   +'<circle cx="'+sunX+'" cy="'+sunY+'" r="'+sunR+'" fill="url(#sun)"/>'
   +'<circle cx="'+sunX+'" cy="'+sunY+'" r="'+sunR+'" fill="none" stroke="'+ORO+'" stroke-opacity="0.42" stroke-width="1.25"/>'
   +'<line x1="26" y1="'+horizon+'" x2="374" y2="'+horizon+'" stroke="'+ORO+'" stroke-opacity="0.38" stroke-width="1.25"/>'
   +dots
   +'<rect x="16" y="16" width="368" height="268" fill="none" stroke="'+ORO+'" stroke-opacity="0.5" stroke-width="1"/>'
   +'<text x="200" y="'+(horizon-14)+'" text-anchor="middle" font-family="Georgia, serif" font-size="11" letter-spacing="0.22em" fill="'+ORO+'" opacity="0.85">'+esc(cat.toUpperCase())+'</text>'
   +nameSvg
   +'<text x="200" y="278" text-anchor="middle" font-size="9" fill="'+ORO+'" opacity="0.7">\u25C6</text>'
   +'</svg>';
}
fs.mkdirSync(path.join(OUT,"mock"),{recursive:true});
const map={};
for(const [name,cat] of ITEMS){ const s=slug(name); fs.writeFileSync(path.join(OUT,"mock",s+".svg"), tile(name,cat)); map[name]="/mock/"+s+".svg"; }
console.log("generados "+Object.keys(map).length+" tiles");
