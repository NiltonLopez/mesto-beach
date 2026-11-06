const T="d0ebd974-8e71-4624-81b1-60384e132a95";
const F={terraza:"baa348f1-96f3-4641-b245-e12eea120878",salon:"903d1b20-c413-444f-8bd1-5b081943abbd",playa:"ba7968b1-3145-44e3-8729-54af3f76cfe1"};
const w=(x1,y1,x2,y2)=>({X1:x1,Y1:y1,X2:x2,Y2:y2});
const sql=[];
sql.push("BEGIN;");
// Floors
const floors=[
 [F.terraza,"Terraza",1300,850,[w(0,0,1300,0),w(1300,0,1300,850),w(0,0,0,850)]],           // barandilla (mar abajo abierto)
 [F.salon,"Salón",1100,800,[w(0,0,1100,0),w(1100,0,1100,800),w(0,800,1100,800),w(0,0,0,320),w(0,480,0,800)]], // muros + puerta izq
 [F.playa,"Playa",1600,1900,[]],                                                             // arena, sin muros
];
for(const [id,,wd,ht,wl] of floors)
  sql.push(`UPDATE floors SET width=${wd}, height=${ht}, walls='${JSON.stringify(wl)}'::jsonb, updated_at=now() WHERE id='${id}';`);
// Reset tables del tenant
sql.push(`DELETE FROM tables WHERE tenant_id='${T}';`);
// Helper insert
const rows=[];
function tbl(code,floor,shape,seats,x,y,zone,pin){ rows.push(`(gen_random_uuid(),'${T}','${floor}',${x},${y},${seats},'${shape}','${code}','${zone}','FIXED',${pin},true,now(),now())`); }
// TERRAZA (14, creativa: hilera de 2 al mar, 4-tops medio, grupos atrás)
const ter=[["T01","ROUND",2,180,700],["T02","ROUND",2,360,700],["T03","ROUND",2,540,700],["T04","ROUND",2,760,700],["T05","ROUND",2,940,700],["T06","ROUND",2,1120,700],
 ["T07","SQUARE",4,250,440],["T08","ROUND",4,520,440],["T09","SQUARE",4,800,440],["T10","ROUND",4,1060,440],
 ["T11","ROUND",6,300,180],["T12","SQUARE",6,640,180],["T13","SQUARE",8,1000,190],["T14","ROUND",2,1200,700]];
for(const [c,s,n,x,y] of ter) tbl(c,F.terraza,s,n,x,y,"Terraza",true);
// SALÓN (12, banco arriba, redondas centro, grupos atrás)
const sal=[["S01","SQUARE",4,200,150],["S02","SQUARE",4,420,150],["S03","SQUARE",2,620,150],["S04","SQUARE",4,820,150],
 ["S05","ROUND",4,250,420],["S06","ROUND",6,560,420],["S07","ROUND",4,860,420],
 ["S08","SQUARE",6,300,660],["S09","SQUARE",8,640,660],["S10","ROUND",2,920,660],["S11","ROUND",4,1000,250],["S12","SQUARE",2,150,620]];
for(const [c,s,n,x,y] of sal) tbl(c,F.salon,s,n,x,y,"Salón",true);
// PLAYA (96 = 8 filas A-H x 12 columnas, 6+6 con pasillo central)
const leftX=[130,240,350,460,570,680], rightX=[920,1030,1140,1250,1360,1470];
const colsX=[...leftX,...rightX];
const rowsY=[140,360,580,800,1020,1240,1460,1680];
const L="ABCDEFGH";
rowsY.forEach((y,ri)=>{ colsX.forEach((x,ci)=>{ const code="P"+L[ri]+String(ci+1).padStart(2,"0"); tbl(code,F.playa,"SQUARE",2,x,y,"Playa",false); }); });
sql.push("INSERT INTO tables (id,tenant_id,floor_id,pos_x,pos_y,seats,shape,table_code,zone,qr_mode,requires_pin,active,created_at,updated_at) VALUES\n"+rows.join(",\n")+";");
sql.push("COMMIT;");
require("fs").writeFileSync(process.env.TEMP+"/seed-layouts.sql", sql.join("\n"));
console.log("SQL generado: "+rows.length+" mesas (Terraza 14 + Salón 12 + Playa 96)");
