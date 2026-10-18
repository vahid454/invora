import {test,expect,type APIRequestContext,type Page} from '@playwright/test';
import {ownerSession} from './session';
test.use({actionTimeout:15000});test.skip(!process.env['INVORA_E2E_ENV'],'Requires an isolated shop database.');
async function fixture(request:APIRequestContext){
 const session=await ownerSession(request,{tradeName:'Isolated Counter Test Store',branchCode:'MAIN',branchName:'Test counter',ownerLogin:'browser-owner',ownerName:'Test Owner',password:'Browser-test-password-2026'});
 const headers={Authorization:'Bearer '+session.accessToken};const post=async(path:string,data:unknown)=>{const response=await request.post('/api/v1'+path,{headers:{...headers,'Idempotency-Key':crypto.randomUUID()},data});expect(response.ok(),await response.text()).toBeTruthy();return response.json();};const get=async(path:string)=>{const response=await request.get('/api/v1'+path,{headers});expect(response.ok(),await response.text()).toBeTruthy();return response.json();};
 const stamp=Date.now(),branch=(await post('/branches',{code:'L'+String(stamp).slice(-10),name:'Collections '+stamp})).id;return{branch,post,get,stamp};
}
async function login(page:Page,branch:string){await page.goto('/login');await page.getByLabel('Login',{exact:true}).fill('browser-owner');await page.getByLabel('Password',{exact:true}).fill('Browser-test-password-2026');await page.getByRole('button',{name:'Sign in →'}).click();await expect(page).toHaveURL(/\/$/);await page.getByLabel('Active branch',{exact:true}).selectOption(branch);await page.goto('/lenden');}

test('register entry, partial receipt, per-entry promise and reviewed reminder work together',async({page,request})=>{
 test.setTimeout(90000);const {branch,post,get,stamp}=await fixture(request);const name='Register customer '+stamp,phone='9000000161';const customer=await post('/customers',{name,phone,alternatePhone:'9000000162'});const errors:string[]=[];page.on('pageerror',e=>errors.push(e.message));await login(page,branch);
 await page.getByRole('button',{name:'+ Add existing due',exact:true}).click();let dialog=page.getByRole('dialog');await dialog.getByRole('searchbox',{name:'Find customer',exact:true}).fill(phone);await expect(dialog.getByRole('combobox',{name:'Customer',exact:true}).locator('option').filter({hasText:name})).toHaveCount(1);await dialog.getByRole('combobox',{name:'Customer',exact:true}).selectOption(customer);
 await dialog.getByLabel('Register / old invoice reference',{exact:true}).fill('BOOK-'+stamp);await dialog.getByLabel('Amount still owed to shop (₹)',{exact:true}).fill('5000');await dialog.getByLabel('Original sale / register date',{exact:true}).fill('2026-09-01');await dialog.getByLabel('Promised / due date',{exact:true}).fill('2026-10-20');await dialog.getByLabel('Phone model / reason',{exact:true}).fill('Galaxy down payment');await dialog.getByRole('button',{name:'Record existing due',exact:true}).click();await expect(dialog).toHaveCount(0);await expect(page.locator('.collections tbody')).toContainText('₹5,000.00');
 await page.locator('.collections tbody').getByRole('link',{name:'Receive',exact:true}).click();await expect(page.getByLabel('Amount',{exact:true})).toHaveValue(/5000(?:\.00)?/);await page.getByLabel('Amount',{exact:true}).fill('2000');await page.getByLabel('Method',{exact:true}).selectOption('2');await page.getByRole('button',{name:'Record payment',exact:true}).click();await expect(page).toHaveURL(/\/payments$/);
 await page.goto('/lenden');await page.getByRole('searchbox',{name:'Find LenDen customer'}).fill(name);await page.getByRole('button',{name:'Recent entries',exact:true}).click();await expect(page.locator('.activity-list')).toContainText('Existing register due');await expect(page.locator('.activity-list')).toContainText('Payment received');await expect(page.locator('.activity-list')).toContainText('₹2,000.00');await expect(page.locator('.collection-summary')).toContainText('₹3,000.00');await page.getByRole('button',{name:'Promise / notes',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByRole('button',{name:'In 20 days',exact:true}).click();await dialog.getByLabel('Conversation / note',{exact:true}).fill('Customer will pay the remaining down payment.');await dialog.getByRole('button',{name:'Save follow-up',exact:true}).click();await expect(dialog).toHaveCount(0);await page.getByRole('combobox',{name:'Show',exact:true}).selectOption('promised');await expect(page.locator('.collections tbody tr')).toHaveCount(1);
 await page.setViewportSize({width:1440,height:1000});await page.screenshot({path:'../artifacts/collections-desktop.png',fullPage:true});await page.setViewportSize({width:390,height:844});await expect.poll(()=>page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();await expect.poll(async()=>{const box=await page.locator('.sidebar').boundingBox();return !!box&&box.x+box.width<=1;}).toBeTruthy();await page.screenshot({path:'../artifacts/collections-mobile.png',fullPage:true,animations:'disabled'});await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('button',{name:'Select eligible customers on this page',exact:true}).click();await page.getByRole('button',{name:'Review WhatsApp reminders',exact:true}).click();dialog=page.getByRole('dialog');await expect(dialog.getByLabel('Message to review',{exact:true})).toHaveValue(/₹3,000.00/);await dialog.getByLabel('Contact number',{exact:true}).selectOption('9000000162');await expect(dialog.getByRole('button',{name:'Open WhatsApp draft ↗',exact:true})).toBeEnabled();await expect(dialog.getByLabel('Message to review',{exact:true})).toHaveValue(/Isolated Counter Test Store/);await expect(dialog).toContainText('Opening a draft does not record it as sent or delivered');
 // Review the draft without opening WhatsApp or sending any message.
 await dialog.getByRole('button',{name:'Finish review',exact:true}).click();const followups=await get('/customers/'+customer+'/followups?branchId='+branch);expect(followups.items).toHaveLength(1);expect(followups.items[0].kind).toBe('Promise');const rows=await get('/lenden/collections?branchId='+branch);expect(Number(rows.totalOutstanding)).toBe(3000);expect(errors).toEqual([]);
});

test('CSV is reviewed, imported once, exported and mistaken entries retain a correction',async({page,request})=>{
 test.setTimeout(60000);const {branch,get,stamp}=await fixture(request);await login(page,branch);await page.getByRole('button',{name:'Import register',exact:true}).click();let dialog=page.getByRole('dialog');
 const csv='name,phone,alternatePhone,reference,description,financeReference,amount,originalDate,dueDate\nCSV customer '+stamp+',9'+String(stamp).slice(-9)+',8'+String(stamp).slice(-9)+',CSV-'+stamp+',Old phone,Finance reference,2345.67,2026-09-01,2026-10-30';await dialog.getByLabel('CSV contents',{exact:true}).fill(csv);await dialog.getByRole('button',{name:'Validate & preview',exact:true}).click();await expect(dialog.locator('.import-review')).toContainText('₹2,345.67');const commit=dialog.getByRole('button',{name:'Import 1 reviewed entries',exact:true});await expect(commit).toBeDisabled();await dialog.getByRole('checkbox').check();await commit.click();await expect(dialog).toHaveCount(0);await expect(page.locator('.collections tbody')).toContainText('CSV-'+stamp);
 const download=page.waitForEvent('download');await page.getByRole('button',{name:'Export CSV',exact:true}).click();expect((await download).suggestedFilename()).toMatch(/^invora-collections-.*\.csv$/);
 await page.getByRole('button',{name:'Correct entry',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByLabel('Correction reason',{exact:true}).fill('Wrong row from the register');await dialog.getByRole('button',{name:'Cancel mistaken entry',exact:true}).click();await expect(dialog).toHaveCount(0);await page.getByRole('combobox',{name:'Show',exact:true}).selectOption('cancelled');await expect(page.locator('.collections tbody')).toContainText('Cancelled');const rows=await get('/lenden/collections?branchId='+branch+'&status=all');expect(rows.items).toHaveLength(1);expect(rows.items[0].cancelled).toBe(true);expect(Number(rows.totalOutstanding)).toBe(0);
});

test('unallocated receipts block reminders and can be applied without recording money again',async({page,request})=>{
 const {branch,post,get,stamp}=await fixture(request),name='Credit customer '+stamp;const customer=await post('/customers',{name,phone:'9000000181'});await post('/lenden/existing-dues',{branchId:branch,customerId:customer,reference:'CREDIT-'+stamp,description:'Down payment',financeReference:'',amount:'1000',businessDate:'2026-09-01',dueDate:'2026-10-30'});await post('/payments',{branchId:branch,partyId:customer,amount:'400',method:1,businessDate:'2026-10-16',reference:'',note:'Already received',allocations:[]});await login(page,branch);await expect(page.getByRole('checkbox',{name:'Select '+name,exact:true})).toBeDisabled();await page.getByRole('button',{name:'Apply existing receipts',exact:true}).click();await expect(page.locator('.collection-summary')).toContainText('₹600.00');await expect(page.getByRole('checkbox',{name:'Select '+name,exact:true})).toBeEnabled();const payments=await get('/payments?branchId='+branch);expect(payments.items).toHaveLength(1);expect(Number(payments.items[0].amount)).toBe(400);
});

test('internal safety flag removes contact actions while preserving the due',async({page,request})=>{
 const {branch,post,get,stamp}=await fixture(request),name='Safety customer '+stamp;
 const customer=await post('/customers',{name,phone:'9000000193'});
 await post('/lenden/existing-dues',{branchId:branch,customerId:customer,reference:'SAFETY-'+stamp,description:'Old phone balance',financeReference:'',amount:'1200',businessDate:'2026-09-01',dueDate:'2026-10-30'});
 await login(page,branch);await page.goto('/customers/'+customer);
 const safety=page.getByRole('region',{name:'Internal customer safety'});
 await expect(safety).toContainText('Clear · normal account');
 await safety.getByLabel('Change internal status').selectOption('FraudConcern');
 await safety.getByLabel('Reason for change').fill('Owner reviewed a safety incident');
 await safety.getByRole('button',{name:'Save internal flag'}).click();
 await expect(safety).toContainText('DO NOT CONTACT');
 await expect(page.getByRole('link',{name:'Create sale'})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Call customer'})).toHaveCount(0);
 await page.goto('/lenden');
 await expect(page.locator('.collection-summary')).toContainText('₹1,200.00');
 await expect(page.getByRole('checkbox',{name:'Select '+name})).toBeDisabled();
 expect((await get('/lenden/collections?branchId='+branch)).items[0].safetyStatus).toBe('FraudConcern');
 await page.goto('/customers/'+customer);
 await safety.getByLabel('Change internal status').selectOption('Clear');
 await safety.getByLabel('Reason for change').fill('Owner reviewed and cleared the flag');
 await safety.getByRole('button',{name:'Save internal flag'}).click();
 await expect(safety).toContainText('Contact allowed');
});
