import {test,expect,type Page,type APIRequestContext} from '@playwright/test';
import {readFileSync} from 'node:fs';
import {ownerSession} from './session';

test.skip(!process.env['INVORA_E2E_ENV'],'Requires an isolated database.');
async function context(request:APIRequestContext) {
  const session=await ownerSession(request,{tradeName:'Isolated Counter Test Store',branchCode:'MAIN',branchName:'Test counter',ownerLogin:'browser-owner',ownerName:'Test Owner',password:'Browser-test-password-2026'});
  const branch=session.user.branchIds[0],headers={Authorization:'Bearer '+session.accessToken};
  const post=async(path:string,data:unknown)=>{const response=await request.post('/api/v1'+path,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data});expect(response.ok(),await response.text()).toBeTruthy();return response.json();};
  const get=async(path:string)=>{const response=await request.get('/api/v1'+path,{headers});expect(response.ok(),await response.text()).toBeTruthy();return response.json();};
  return {branch,headers,post,get};
}
async function login(page:Page) {
  await page.goto('/login');await page.getByLabel('Login',{exact:true}).fill('browser-owner');await page.getByLabel('Password',{exact:true}).fill('Browser-test-password-2026');await page.getByRole('button',{name:'Sign in →'}).click();await expect(page).toHaveURL(/\/$/);
}
const today=()=>new Date().toLocaleDateString('en-CA',{timeZone:'Asia/Kolkata'});
function offset(day:string,by:number){const date=new Date(day+'T12:00:00Z');date.setUTCDate(date.getUTCDate()+by);return date.toISOString().slice(0,10);}

test('LenDen customer search stays a single compact row and works on desktop and mobile',async({page,request})=>{
  const {post}=await context(request);const name='Compact LenDen '+Date.now();await post('/customers',{name,phone:'9000000071'});
  await login(page);await page.setViewportSize({width:1440,height:1000});await page.goto('/lenden');
  const search=page.getByRole('searchbox',{name:'Find LenDen customer'});await search.fill(name);await expect(page.locator('tbody tr')).toHaveCount(1);await expect(page.locator('tbody')).toContainText(name);
  const input=await search.boundingBox(),select=await page.getByRole('combobox',{name:'Show accounts',exact:true}).boundingBox();expect(input!.height).toBeGreaterThanOrEqual(44);expect(input!.height).toBeLessThanOrEqual(56);expect(input!.width).toBeLessThanOrEqual(440);expect(Math.abs(input!.y-select!.y)).toBeLessThanOrEqual(2);
  await page.screenshot({path:'../artifacts/lenden-compact-search-desktop.png',fullPage:true,animations:'disabled'});
  await page.setViewportSize({width:375,height:812});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();await expect.poll(async()=>{const box=await page.locator('.sidebar').boundingBox();return !!box&&box.x+box.width<=1;}).toBeTruthy();expect((await search.boundingBox())!.height).toBeLessThanOrEqual(56);
  await page.screenshot({path:'../artifacts/lenden-compact-search-mobile.png',fullPage:true,animations:'disabled'});
});

test('invoice business-date filters combine with customer search and exported rows',async({page,request})=>{
  test.setTimeout(90000);const {post,branch,headers}=await context(request),stamp=Date.now(),name='Dated invoices '+stamp;
  const customer=await post('/customers',{name,phone:'9000000072'}),tax=await post('/tax-rates',{name:'Dates tax '+stamp,rate:'0'});
  const product=await post('/products/with-stock',{branchId:branch,quantity:4,unitCost:'10',reason:'Date filter fixtures',product:{name:'Date cable '+stamp,brand:'Generic',category:'Electronics',hsn:'8544',taxRateId:tax,sku:'DATE-'+stamp,ram:'',storage:'',color:'',serialized:false,sellingPrice:'200',mrp:'200'}});
  const days=[offset(today(),-8),offset(today(),-1),today(),offset(today(),1)],invoices=[];
  for(const businessDate of days){const draft=await post('/sales',{branchId:branch,customerId:customer,businessDate,dueDate:null,interstate:false,items:[{productVariantId:product,quantity:1,unitPrice:'200'}],payments:[{amount:'200',method:2}]});invoices.push(await post('/sales/'+draft.id+'/complete',{}));}
  await login(page);await page.goto('/sales');await page.getByRole('textbox',{name:'Search directory'}).fill(name);await expect(page.locator('tbody tr')).toHaveCount(4);
  await page.getByLabel('From date',{exact:true}).fill(days[1]);await page.getByLabel('To date',{exact:true}).fill(days[2]);await expect(page.locator('tbody tr')).toHaveCount(2);await expect(page.locator('tbody')).toContainText(invoices[1].number);await expect(page.locator('tbody')).toContainText(invoices[2].number);
  const download=page.waitForEvent('download');await page.getByRole('button',{name:'Export CSV ↓'}).click();await(await download).saveAs('../artifacts/invoices-date-filtered.csv');const csv=readFileSync('../artifacts/invoices-date-filtered.csv','utf8');expect(csv).toContain(invoices[1].number);expect(csv).toContain(invoices[2].number);expect(csv).not.toContain(invoices[0].number);expect(csv).not.toContain(invoices[3].number);
  await page.getByRole('combobox',{name:'Invoice period',exact:true}).selectOption('today');await expect(page.getByLabel('From date',{exact:true})).toHaveValue(today());await expect(page.locator('tbody tr')).toHaveCount(1);await expect(page.locator('tbody')).toContainText(invoices[2].number);
  await page.getByRole('combobox',{name:'Invoice period',exact:true}).selectOption('yesterday');await expect(page.locator('tbody tr')).toHaveCount(1);await expect(page.locator('tbody')).toContainText(invoices[1].number);
  await page.getByRole('combobox',{name:'Invoice period',exact:true}).selectOption('week');await expect(page.locator('tbody tr')).toHaveCount(2);
  await page.getByLabel('From date',{exact:true}).fill(days[3]);await expect(page.locator('#invoice-date-note')).toContainText('From date must be on or before To date');await expect(page.getByRole('button',{name:'Export CSV ↓'})).toBeDisabled();
  await page.getByRole('button',{name:'Clear dates',exact:true}).click();await expect(page.getByRole('textbox',{name:'Search directory'})).toHaveValue(name);await expect(page.locator('tbody tr')).toHaveCount(4);
  await page.getByRole('combobox',{name:'Invoice period',exact:true}).selectOption('week');await expect(page.locator('tbody tr')).toHaveCount(2);await page.screenshot({path:'../artifacts/invoice-date-filters-desktop.png',fullPage:true,animations:'disabled'});
  await page.setViewportSize({width:375,height:812});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();await expect.poll(async()=>{const box=await page.locator('.sidebar').boundingBox();return !!box&&box.x+box.width<=1;}).toBeTruthy();await page.screenshot({path:'../artifacts/invoice-date-filters-mobile.png',fullPage:true,animations:'disabled'});
  const invalid=await request.get('/api/v1/sales?branchId='+branch+'&from=2026-10-10&to=2026-10-01',{headers});expect(invalid.status()).toBe(400);
});

test('sale counts cleared and split payments correctly through review, posting and LenDen',async({page,request})=>{
  test.setTimeout(90000);const {post,get,branch}=await context(request),stamp=Date.now(),name='Split pay customer '+stamp;
  const customer=await post('/customers',{name,phone:'9000000073'}),tax=await post('/tax-rates',{name:'Split tax '+stamp,rate:'0'});
  const product=await post('/products/with-stock',{branchId:branch,quantity:2,unitCost:'10',reason:'Split payment fixtures',product:{name:'Split cable '+stamp,brand:'Generic',category:'Electronics',hsn:'8544',taxRateId:tax,sku:'SPLIT-'+stamp,ram:'',storage:'',color:'',serialized:false,sellingPrice:'200.45',mrp:'200.45'}});
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));await login(page);
  const begin=async()=>{await page.goto('/sales/new?partyId='+customer);await page.getByRole('combobox',{name:'Product',exact:true}).selectOption(product);await page.getByRole('button',{name:'+ Add product',exact:true}).click();await expect(page.locator('.cart-table tbody')).toContainText('Split cable');};
  await begin();await page.getByLabel('Cash received',{exact:true}).fill('');await page.getByLabel('Card received',{exact:true}).fill('');await page.getByLabel('UPI received',{exact:true}).fill('100.10');
  await page.getByLabel('Cash received',{exact:true}).fill('-1');await expect(page.locator('#sale-payment-error')).toContainText('Cash: enter');await expect(page.getByRole('button',{name:'Review totals',exact:true})).toBeDisabled();await page.getByLabel('Cash received',{exact:true}).fill('');
  await page.getByLabel('Card received',{exact:true}).fill('0.001');await expect(page.locator('#sale-payment-error')).toContainText('Card: enter');await page.getByLabel('Card received',{exact:true}).fill('');
  const firstQuote=page.waitForResponse(r=>r.url().endsWith('/sales/quote'));await page.getByRole('button',{name:'Review totals',exact:true}).click();expect(Number((await(await firstQuote).json()).received)).toBe(100.10);
  const summary=page.getByRole('region',{name:'Payment summary'});await expect(summary).toContainText('₹100.35');await expect(page.getByRole('button',{name:'Complete sale',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Complete sale',exact:true}).click();await expect(page.locator('p.error').first()).toContainText('Enter a due date');
  await page.getByLabel('Cash received',{exact:true}).fill('50.10');await page.getByLabel('UPI received',{exact:true}).fill('70.20');await page.getByLabel('Card received',{exact:true}).fill('80.15');await expect(summary).toContainText('₹200.45');await expect(summary).toContainText('₹0.00');await expect(page.getByRole('button',{name:'Review totals',exact:true})).toBeVisible();
  await page.getByLabel('Card received',{exact:true}).fill('81.15');await expect(page.locator('#sale-payment-error')).toContainText('exceed this invoice by ₹1.00');await page.getByLabel('Card received',{exact:true}).fill('80.15');
  await page.getByRole('button',{name:'Review totals',exact:true}).click();await expect(page.getByRole('button',{name:'Complete sale',exact:true})).toBeVisible();await page.screenshot({path:'../artifacts/sale-split-payment-review.png',fullPage:true,animations:'disabled'});
  await page.getByRole('button',{name:'Complete sale',exact:true}).click();await expect(page).toHaveURL(/\/sales\/[a-f0-9-]+$/);const firstId=page.url().split('/').at(-1);const first=await get('/sales/'+firstId);expect(Number(first.paid)).toBe(200.45);expect(Number(first.outstanding)).toBe(0);
  let payments=await get('/payments?branchId='+branch+'&partyId='+customer);expect(payments.items.map((p:{method:number;amount:string})=>[p.method,Number(p.amount)]).sort()).toEqual([[1,50.1],[2,70.2],[3,80.15]]);
  await begin();await page.getByLabel('Due date for credit sale',{exact:true}).fill(offset(today(),7));await page.getByLabel('Cash received',{exact:true}).fill('10');await page.getByLabel('UPI received',{exact:true}).fill('20');await page.getByLabel('Card received',{exact:true}).fill('');await page.getByRole('button',{name:'Review totals',exact:true}).click();await expect(summary).toContainText('₹170.45');await page.getByRole('button',{name:'Complete sale',exact:true}).click();await expect(page).toHaveURL(/\/sales\/[a-f0-9-]+$/);const secondId=page.url().split('/').at(-1);
  const second=await get('/sales/'+secondId);expect(Number(second.paid)).toBe(30);expect(Number(second.outstanding)).toBe(170.45);expect(Number((await get('/customers/'+customer+'?branchId='+branch)).tradeBalance)).toBe(170.45);
  payments=await get('/payments?branchId='+branch+'&partyId='+customer);expect(payments.totalItems).toBe(5);
  await page.goto('/lenden');await page.getByRole('searchbox',{name:'Find LenDen customer'}).fill(name);await expect(page.locator('tbody')).toContainText('₹170.45');await page.getByRole('button',{name:'Invoice dues',exact:true}).click();await expect(page.locator('tbody tr')).toHaveCount(1);await expect(page.locator('tbody')).toContainText(second.number);expect(errors).toEqual([]);
});
