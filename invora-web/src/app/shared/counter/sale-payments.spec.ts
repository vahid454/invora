import {test} from 'node:test';
import assert from 'node:assert/strict';
import {salePayments} from './sale-payments';

test('cleared payment fields count as zero and paise sum exactly across methods',()=>{
  assert.deepEqual(salePayments({cash:null,upi:'',card:0}),{components:[],total:'0.00'});
  const result=salePayments({cash:0.1,upi:'0.20',card:'100.01'});
  assert.equal(result.total,'100.31');
  assert.deepEqual(result.components.map(({amount,method})=>({amount,method})),[{amount:'0.10',method:1},{amount:'0.20',method:2},{amount:'100.01',method:3}]);
});
test('invalid payment entries are rejected instead of silently dropping or rounding money',()=>{
  for(const cash of [-1,'-0.01','0.001','NaN','Infinity','100000000000000','not-money'])
    assert.throws(()=>salePayments({cash,upi:0,card:0}),/Cash: enter/);
});
