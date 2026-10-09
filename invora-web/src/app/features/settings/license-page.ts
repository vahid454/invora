import {Component,inject,signal} from '@angular/core';
import {DatePipe} from '@angular/common';
import {FormControl,ReactiveFormsModule,Validators} from '@angular/forms';
import {RouterLink} from '@angular/router';
import {ShopLicenseService} from '../../core/license';
import {AuthService} from '../../core/auth';
import {errorText} from '../../core/api';
@Component({standalone:true,imports:[DatePipe,ReactiveFormsModule,RouterLink],template:`
<section class="page-intro"><div><p class="eyebrow">YOUR SHOP LICENCE</p><h1>Activation & renewal</h1><p class="muted">One licence for this business and its branches. Your shop records stay available after expiry.</p></div><a routerLink="/settings" class="secondary">Business settings</a></section>
<p class="error" role="alert">{{error()||license.error()}}</p><p class="success" role="status">{{message()}}</p>
@if(license.status();as status){<div class="license-layout"><section class="panel license-summary"><span class="badge">{{status.required?status.state:'Licensing disabled'}}</span><h2>{{status.businessName}}</h2><label>Shop ID<input [value]="status.businessId??''" readonly aria-label="Shop ID"></label><button class="secondary" type="button" (click)="copyId()">Copy Shop ID</button><p class="small muted">Send this ID to the vendor with your purchase or renewal request. You can share this ID; keep your password and setup key private.</p>@if(status.expiresOn){<dl><div><dt>Paid through</dt><dd>{{status.expiresOn|date:'dd MMM yyyy'}}</dd></div><div><dt>Grace ends</dt><dd>{{status.graceUntil|date:'dd MMM yyyy'}}</dd></div></dl>}<p>{{status.canWrite?'Recording transactions is available.':'Read-only: activate a valid licence to record new transactions.'}}</p><small>Existing invoices, statements, reports, exports and host backups remain available. Licence dates use UTC.</small></section>
<section class="panel license-activation"><h2>{{status.state==='Active'||status.state==='Grace'?'Renew this shop':'Activate this shop'}}</h2>@if(auth.user()?.isOwner){<form (submit)="$event.preventDefault();activate()"><label>Licence key<textarea [formControl]="key" rows="7" maxlength="12000" placeholder="Paste the INVORA1… key from your vendor" required></textarea></label><p class="small muted">Use the key issued for the Shop ID shown here. The key is verified locally; activation does not send shop records to the vendor.</p><button class="primary" [disabled]="busy()||key.invalid">{{busy()?'Verifying…':'Activate licence'}}</button></form>}@else{<p>The business owner manages activation and renewal. Ask the owner to enter the shop licence.</p>}@if(!status.required){<p class="panel-note">This installation does not require a paid licence. A commercial deployment must enable licensing in its host configuration.</p>}</section></div>}
`})
export class LicensePage {
 readonly license=inject(ShopLicenseService);readonly auth=inject(AuthService);readonly key=new FormControl('',{nonNullable:true,validators:[Validators.required,Validators.maxLength(12000)]});readonly busy=signal(false);readonly error=signal('');readonly message=signal('');
 constructor(){void this.license.load();}
 async activate(){if(this.busy()||this.key.invalid)return;this.busy.set(true);this.error.set('');try{await this.license.activate(this.key.value.trim());this.key.reset();this.message.set('Licence verified and saved for this shop.');}catch(e){this.error.set(errorText(e));}finally{this.busy.set(false);}}
 async copyId(){try{await navigator.clipboard.writeText(this.license.status()?.businessId??'');this.message.set('Shop ID copied.');}catch{this.message.set('Select the Shop ID to copy it.');}}
}
