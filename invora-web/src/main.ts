import {bootstrapApplication} from '@angular/platform-browser';
import {Component,inject} from '@angular/core';
import {provideHttpClient,withInterceptors} from '@angular/common/http';
import {CanActivateFn,provideRouter,Router,RouterOutlet,Routes} from '@angular/router';
import {AuthService} from './app/core/auth';
import {authInterceptor,BranchContext} from './app/core/api';
import {Shell} from './app/layout/shell';
const guard:CanActivateFn=async()=>{const auth=inject(AuthService),branches=inject(BranchContext),router=inject(Router);if(!await auth.ready())return router.createUrlTree(['/login']);if(!branches.id())await branches.load();return true;};
const permission:CanActivateFn=(route)=>{const auth=inject(AuthService),router=inject(Router);if(Array.isArray(route.data['permissions'])&&!route.data['permissions'].every((p:string)=>auth.can(p)))return router.createUrlTree(['/account']);return !route.data['permission']||auth.can(String(route.data['permission']))||router.createUrlTree(['/account']);};
@Component({standalone:true,template:''})class EmptyPage{}
const directories=['sales','purchases','customers','suppliers','inventory','catalog','payments','expenses','transfers','staff','audit'];
const perms:Record<string,string>={sales:'sales.view',purchases:'purchase.view',customers:'customers.view',suppliers:'supplier.view',inventory:'inventory.view',catalog:'inventory.view',payments:'payments.view',expenses:'expenses.view',transfers:'inventory.transfer',staff:'users.manage',audit:'audit.view'};
const routes:Routes=[{path:'setup',loadComponent:()=>import('./app/features/auth/auth-page').then(m=>m.AuthPage)},{path:'login',loadComponent:()=>import('./app/features/auth/auth-page').then(m=>m.AuthPage)},{path:'',component:Shell,canActivate:[guard],children:[
{path:'',pathMatch:'full',canActivate:[permission],data:{permission:'reports.view'},loadComponent:()=>import('./app/features/dashboard/dashboard').then(m=>m.Dashboard)},
{path:'branch-switch',component:EmptyPage},
{path:'sales/new',canActivate:[permission],data:{permission:'sales.create',mode:'sale'},loadComponent:()=>import('./app/features/workflows/trade-form').then(m=>m.TradeForm)},
{path:'purchases/new',canActivate:[permission],data:{permission:'purchase.create',mode:'purchase'},loadComponent:()=>import('./app/features/workflows/trade-form').then(m=>m.TradeForm)},
{path:'lenden/new',canActivate:[permission],data:{permissions:['ledger.adjust','payments.create','customers.credit.view'],independent:true},loadComponent:()=>import('./app/features/payments/payment-form').then(m=>m.PaymentForm)},
{path:'payments/new',canActivate:[permission],data:{permission:'payments.create'},loadComponent:()=>import('./app/features/payments/payment-form').then(m=>m.PaymentForm)},
{path:'sales/:id',canActivate:[permission],data:{permission:'sales.view',mode:'sale'},loadComponent:()=>import('./app/features/workflows/document-detail').then(m=>m.DocumentDetail)},
{path:'purchases/:id',canActivate:[permission],data:{permission:'purchase.view',mode:'purchase'},loadComponent:()=>import('./app/features/workflows/document-detail').then(m=>m.DocumentDetail)},
{path:'transfers/:id',canActivate:[permission],data:{permission:'inventory.transfer'},loadComponent:()=>import('./app/features/workflows/transfer-detail').then(m=>m.TransferDetail)},
{path:'customers/:id',canActivate:[permission],data:{permission:'customers.view',kind:'customer'},loadComponent:()=>import('./app/features/finance/finance').then(m=>m.PartyDetail)},
{path:'suppliers/:id',canActivate:[permission],data:{permission:'supplier.view',kind:'supplier'},loadComponent:()=>import('./app/features/finance/finance').then(m=>m.PartyDetail)},
...directories.map(mode=>({path:mode,canActivate:[permission],data:{mode,permission:perms[mode]},loadComponent:()=>import('./app/features/directory/directory').then(m=>m.Directory)})),
{path:'lenden',canActivate:[permission],data:{permission:'customers.credit.view'},loadComponent:()=>import('./app/features/finance/finance').then(m=>m.LenDen)},
{path:'reports',canActivate:[permission],data:{permission:'reports.view'},loadComponent:()=>import('./app/features/finance/reports').then(m=>m.ReportsPage)},
{path:'settings',canActivate:[permission],data:{permission:'business.settings'},loadComponent:()=>import('./app/features/settings/settings').then(m=>m.SettingsPage)},
{path:'imports',loadComponent:()=>import('./app/features/settings/imports').then(m=>m.ImportsPage)},
{path:'license',loadComponent:()=>import('./app/features/settings/license-page').then(m=>m.LicensePage)},
{path:'account',loadComponent:()=>import('./app/features/settings/settings').then(m=>m.AccountPage)},
{path:'used-devices',canActivate:[permission],data:{permission:'inventory.view'},loadComponent:()=>import('./app/features/used-devices/used-devices').then(m=>m.UsedDevicesPage)},
{path:'**',redirectTo:''}]}];
@Component({selector:'invora-root',standalone:true,imports:[RouterOutlet],template:'<router-outlet/>'})class App{}
bootstrapApplication(App,{providers:[provideHttpClient(withInterceptors([authInterceptor])),provideRouter(routes)]}).catch(e=>console.error('Application initialization failed',e));
