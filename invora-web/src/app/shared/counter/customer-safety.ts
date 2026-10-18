import {Component,inject,input,output,signal,effect} from '@angular/core';
import {ReactiveFormsModule,FormControl} from '@angular/forms';
import {ApiService,BranchContext,errorText} from '../../core/api';
import {AuthService} from '../../core/auth';

type Safety={status:'Clear'|'Blocked'|'FraudConcern';reason:string;changedAtUtc:string|null};
@Component({selector:'invora-customer-safety',standalone:true,imports:[ReactiveFormsModule],template:`
<section class="safety-panel" aria-label="Internal customer safety">
  @if(loaded()){<div class="safety-heading"><div><small>INTERNAL CUSTOMER SAFETY</small><h3>{{label(status())}}</h3></div><span class="safety-mark" [class.clear]="status()==='Clear'">{{status()==='Clear'?'Contact allowed':'DO NOT CONTACT'}}</span></div>}
  @if(status()!=='Clear'){<p class="safety-warning">Do not call, message, send reminders, give money, acquire a device or start a new sale. Existing invoices, dues and payments stay available for review.</p>}
  @if(reason()){<p class="small"><strong>Recorded reason:</strong> {{reason()}}</p>}
  @if(!loaded()){<p class="small muted" role="status">Checking internal customer status…</p>}
  @if(loaded()&&auth.can('customers.manage')&&auth.can('business.settings')){
    <div class="safety-editor"><label>Change internal status<select [formControl]="next"><option value="Clear">Clear · normal account</option><option value="Blocked">Blacklist · do not deal/contact</option><option value="FraudConcern">Fraud concern · do not deal/contact</option></select></label><label>Reason for change<textarea [formControl]="changeReason" rows="2" maxlength="500" placeholder="Record facts for your team; never put this on a customer document"></textarea></label><button class="secondary" type="button" [disabled]="busy()||!changeReason.value.trim()||next.value===status()" (click)="save()">{{busy()?'Saving…':'Save internal flag'}}</button></div>
  }
  <p class="error" role="alert">{{error()}}</p>
</section>`,styles:[`.safety-panel{margin:1rem 0;padding:1.25rem;border:1px solid #d4b6a5;border-radius:18px;background:#fff8f3;color:#33241d}.safety-heading{display:flex;justify-content:space-between;align-items:center;gap:1rem}.safety-heading small{font-size:.7rem;letter-spacing:.15em;font-weight:800}.safety-heading h3{margin:.2rem 0}.safety-mark{border-radius:999px;background:#7b2825;color:#fff;padding:.42rem .7rem;font-size:.7rem;font-weight:800;white-space:nowrap}.safety-mark.clear{background:#e7f0e7;color:#1b4a37}.safety-warning{margin:.7rem 0;line-height:1.5}.safety-editor{display:grid;grid-template-columns:minmax(180px,1fr) minmax(220px,2fr) auto;align-items:end;gap:.75rem;margin-top:.9rem}.safety-editor label{min-width:0}.safety-editor textarea{min-height:3.3rem}@media(max-width:850px){.safety-editor{grid-template-columns:1fr}.safety-mark{white-space:normal}}`]})
export class CustomerSafety {
  readonly customerId=input.required<string>();readonly changed=output<void>();readonly auth=inject(AuthService);private api=inject(ApiService);private branch=inject(BranchContext);
  readonly status=signal<Safety['status']>('Clear');readonly reason=signal('');readonly error=signal('');readonly busy=signal(false);readonly loaded=signal(false);readonly next=new FormControl<Safety['status']>('Clear',{nonNullable:true});readonly changeReason=new FormControl('',{nonNullable:true});
  constructor(){effect(()=>{const id=this.customerId();if(id)void this.load(id);});}
  label(status:string){return status==='FraudConcern'?'Fraud concern · internal':status==='Blocked'?'Blacklisted · internal':'Clear · normal account';}
  async load(id=this.customerId()){this.loaded.set(false);this.error.set('');try{const value=await this.api.get<Safety>('/customers/'+id+'/safety?'+this.branch.query());if(id!==this.customerId())return;this.status.set(value.status);this.next.setValue(value.status);this.reason.set(value.reason);this.loaded.set(true);}catch(e){this.error.set(errorText(e));}}
  async save(){if(this.busy()||!this.changeReason.value.trim()||this.next.value===this.status())return;this.busy.set(true);this.error.set('');try{const value=await this.api.mutate<Safety>('/customers/'+this.customerId()+'/safety',{branchId:this.branch.id(),status:this.next.value,reason:this.changeReason.value.trim()},'PUT');this.status.set(value.status);this.reason.set(value.reason);this.changeReason.setValue('');this.changed.emit();}catch(e){this.error.set(errorText(e));}finally{this.busy.set(false);}}
}
