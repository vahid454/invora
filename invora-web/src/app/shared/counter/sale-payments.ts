import Decimal from 'decimal.js';

export function salePayments(values:{cash:unknown;upi:unknown;card:unknown}) {
  const components = ([['cash',1,'Cash'],['upi',2,'UPI'],['card',3,'Card']] as const).map(([key,method,label])=>{
    const input=values[key];
    const value=input===null||input===undefined||String(input).trim()===''?'0':String(input);
    let amount:Decimal;
    try { amount=new Decimal(value); } catch { throw new Error(label+': enter a valid amount.'); }
    if(!amount.isFinite()||amount.lt(0)||amount.gt('99999999999999')||amount.decimalPlaces()>2)
      throw new Error(label+': enter a nonnegative amount with up to two decimal places.');
    return {amount:amount.toFixed(2),method,label};
  }).filter(component=>new Decimal(component.amount).gt(0));
  return {components,total:components.reduce((sum,p)=>sum.plus(p.amount),new Decimal(0)).toFixed(2)};
}
