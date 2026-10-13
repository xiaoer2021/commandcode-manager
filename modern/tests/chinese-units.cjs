const assert=require('assert');
const {chineseCount:format}=require('../ui/src/format.ts');
for(const [n,label] of [[0,'0'],[9,'9'],[10,'10'],[100,'100'],[1000,'1千'],[10000,'1万'],[100000,'10万'],[1000000,'100万'],[10000000,'1000万'],[100000000,'1亿'],[1000000000,'10亿'],[10000000000,'100亿'],[100000000000,'1000亿'],[1000000000000,'1万亿'],[9999,'1万'],[99999999,'1亿'],[90575648,'9057.56万'],[275648,'27.56万'],[null,'—'],[undefined,'—'],[NaN,'—']])assert.equal(format(n),label);
console.log('PASS: common Chinese units, small exact counts, rounding boundaries, unknown counts');
