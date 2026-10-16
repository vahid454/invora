import {test,expect,type APIRequestContext,type Page} from '@playwright/test';
import {ownerSession} from './session';

test.skip(!process.env['INVORA_E2E_ENV'],'Requires an isolated database.');
async function fixture(request:APIRequestContext){
  const session=await ownerSession(request,{tradeName:'Isolated Counter Test Store',branchCode:'MAIN',branchName:'Test counter',ownerLogin:'browser-owner',ownerName:'Test Owner',password:'Browser-test-password-2026'});
  const branch=session.user.branchIds[0],headers={Authorization:'Bearer '+session.accessToken};
  const post=async(path:string,data:unknown)=>{const response=await request.post('/api/v1'+path,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data});expect(response.ok(),await response.text()).toBeTruthy();return response.json();};
  const get=async(path:string)=>{const response=await request.get('/api/v1'+path,{headers});expect(response.ok(),await response.text()).toBeTruthy();return response.json();};
  return {branch,post,get};
}
async function login(page:Page,branch:string){await page.goto('/login');await page.getByLabel('Login',{exact:true}).fill('browser-owner');await page.getByLabel('Password',{exact:true}).fill('Browser-test-password-2026');await page.getByRole('button',{name:'Sign in →'}).click();await expect(page).toHaveURL(/\/$/);await page.getByLabel('Active branch').selectOption(branch);}

test('thermal device stickers preview, reprint and scan the correct phone at the counter',async({page,request})=>{
  test.setTimeout(90000);const {branch,post,get}=await fixture(request),stamp=Date.now(),name='Galaxy Label '+stamp;
  const tax=await post('/tax-rates',{name:'Label zero '+stamp,rate:'0'});
  await post('/products/with-stock',{branchId:branch,quantity:2,unitCost:'0',reason:'Label fixture',product:{name,brand:'Samsung',category:'Mobile',hsn:'85171300',taxRateId:tax,sku:'LABEL-PHONE-'+stamp,ram:'',storage:'',color:'',serialized:true,sellingPrice:'3000',mrp:'3500'},deviceStock:[{device:{imei1:String(stamp)+'01'},ram:'8GB',storage:'128GB',color:'Blue',unitCost:'1000',sellingPrice:'3000',mrp:'3500'},{device:{imei1:String(stamp)+'02'},ram:'12GB',storage:'256GB',color:'Green',unitCost:'1500',sellingPrice:'3500',mrp:'4000'}]});
  const stock=await get('/inventory?branchId='+branch+'&search='+encodeURIComponent(name));expect(stock.items).toHaveLength(2);
  const labels=await get('/inventory/labels?branchId='+branch+'&kind=unit&ids='+stock.items.map((x:{id:string})=>x.id).join(','));expect(labels[0].code).not.toEqual(labels[1].code);
  const customer=await post('/customers',{name:'QR customer '+stamp,phone:'9000000081'});
  const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));await login(page,branch);await page.setViewportSize({width:1440,height:1000});await page.goto('/inventory?search='+encodeURIComponent(name));
  await expect(page.locator('tbody tr')).toHaveCount(2);await page.getByRole('button',{name:'Print labels',exact:true}).click();
  const dialog=page.getByRole('dialog');await expect(dialog.getByRole('heading',{name:'Print item stickers'})).toBeVisible();await expect(dialog.locator('.label-choice')).toHaveCount(2);await expect(dialog.getByLabel('Sticker size')).toHaveValue('50x30');
  const boxes=dialog.getByRole('checkbox');await boxes.first().uncheck();await expect(dialog.locator('.label-list-heading')).toContainText('1 of 2 selected');await boxes.first().check();
  await dialog.getByLabel('Copies per item').fill('2');const download=page.waitForEvent('download');await dialog.getByRole('button',{name:'Download print PDF ↓'}).click();await (await download).saveAs('../artifacts/phone-labels-browser.pdf');await expect(dialog.getByRole('status')).toContainText('4 stickers ready');
  await page.screenshot({path:'../artifacts/item-labels-desktop.png',fullPage:true,animations:'disabled'});
  await page.setViewportSize({width:375,height:812});await expect.poll(()=>dialog.evaluate(el=>el.scrollWidth<=el.clientWidth+1)).toBeTruthy();await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();await page.screenshot({path:'../artifacts/item-labels-mobile.png',fullPage:true,animations:'disabled'});
  await dialog.getByRole('button',{name:'Close',exact:true}).click();await expect(dialog).toHaveCount(0);await page.getByRole('textbox',{name:'Search directory'}).fill(labels[0].code);await expect(page.locator('tbody tr')).toHaveCount(1);await expect(page.locator('tbody')).toContainText(labels[0].identifiers[0].split(': ')[1]);
  await page.setViewportSize({width:1440,height:1000});await page.goto('/sales/new?partyId='+customer);const scanner=page.getByRole('textbox',{name:'Scan IMEI, serial or barcode'});await scanner.fill(labels[0].code);await scanner.press('Enter');await expect(page.locator('.cart-table tbody tr')).toHaveCount(1);await expect(page.locator('.cart-table')).toContainText(labels[0].identifiers[0].split(': ')[1]);
  await scanner.fill(labels[0].code);await scanner.press('Enter');await expect(page.locator('.cart-table tbody tr')).toHaveCount(1);
  const unit=stock.items.find((x:{id:string})=>x.id===labels[0].id);await page.getByLabel('Cash received',{exact:true}).fill(unit.sellingPrice);await page.getByRole('button',{name:'Review totals',exact:true}).click();await page.getByRole('button',{name:'Complete sale',exact:true}).click();await expect(page).toHaveURL(/\/sales\/[a-f0-9-]+$/);
  const sale=await get('/sales/'+page.url().split('/').at(-1));expect(Number(sale.outstanding)).toBe(0);
  await page.goto('/sales/new?partyId='+customer);await scanner.fill(labels[0].code);await scanner.press('Enter');await expect(page.locator('p.error').first()).toContainText('This device is Sold.');expect(errors).toEqual([]);
});

test('quantity product labels reuse a code and repeated scans increase sale quantity',async({page,request})=>{
  const {branch,post,get}=await fixture(request),stamp=Date.now(),name='Label cable '+stamp;
  const tax=await post('/tax-rates',{name:'Label cable tax '+stamp,rate:'0'});
  const product=await post('/products/with-stock',{branchId:branch,quantity:5,unitCost:'50',reason:'Label cable fixture',product:{name,brand:'Generic',category:'Accessories',hsn:'8544',taxRateId:tax,sku:'LABEL-CABLE-'+stamp,ram:'',storage:'',color:'Black',serialized:false,sellingPrice:'100',mrp:'150'}});
  const labels=await get('/inventory/labels?branchId='+branch+'&kind=product&ids='+product);
  await login(page,branch);await page.goto('/catalog?search='+encodeURIComponent(name));await page.getByRole('button',{name:'Print label',exact:true}).click();await expect(page.getByRole('dialog')).toContainText('QUANTITY ITEM');await page.getByRole('dialog').getByRole('button',{name:'Close',exact:true}).click();
  await page.goto('/inventory?search='+encodeURIComponent(labels[0].code));await expect(page.getByRole('button',{name:'Quantity stock',exact:true})).toHaveClass(/selected/);await expect(page.locator('tbody')).toContainText(name);
  await page.goto('/sales/new');const scanner=page.getByRole('textbox',{name:'Scan IMEI, serial or barcode'});for(let i=0;i<2;i++){await scanner.fill(labels[0].code);await scanner.press('Enter');await expect(page.locator('.cart-table tbody tr')).toHaveCount(1);}
  await expect(page.locator('.cart-table input[type=number]').first()).toHaveValue('2');
});
