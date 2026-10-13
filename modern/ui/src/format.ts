const units:readonly [number,string][]=[[1e12,'万亿'],[1e8,'亿'],[1e4,'万'],[1e3,'千']];
const compactFormat=new Intl.NumberFormat('zh-CN',{maximumFractionDigits:2,useGrouping:false});
const integerFormat=new Intl.NumberFormat('zh-CN',{maximumFractionDigits:0});
export function chineseCount(value:number|null|undefined):string{
if(typeof value!=='number'||!Number.isFinite(value))return '—';
const size=Math.abs(value);
for(let i=0;i<units.length;i++){
const [base,label]=units[i];
if(size<base)continue;
const rounded=Math.round(size/base*100)/100;
if(i>0&&rounded*base>=units[i-1][0])return compactFormat.format(value/units[i-1][0])+units[i-1][1];
return compactFormat.format(value/base)+label;
}
return integerFormat.format(value);
}
export function exactCount(value:number|null|undefined):string{return typeof value==='number'&&Number.isFinite(value)?integerFormat.format(value):'—';}
