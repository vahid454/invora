import {Component,inject,input,output,signal,OnInit} from '@angular/core';
import {DatePipe} from '@angular/common';
import {FormBuilder,ReactiveFormsModule,Validators} from '@angular/forms';
import {ApiService,BranchContext,errorText} from '../../core/api';
import {ModalFrame} from './modal-frame';

type Details={ram:string;storage:string;color:string;condition:string;batteryHealth:number|null;notes:string};
type Revision={id:string;createdAtUtc:string;actor:string;reason:string;before:Details;after:Details};
type Device={unit:{description:string;identifiers:string[];status:string;condition:string};current:Details;revision:string;canEdit:boolean;history:Revision[]};

@Component({selector:'invora-device-editor',standalone:true,imports:[ReactiveFormsModule,DatePipe,ModalFrame],template:`
<invora-modal title="Device details & corrections" eyebrow="INVENTORY · ONE PHYSICAL DEVICE" [busy]="saving()" (closed)="close()">
  <p class="error" role="alert">{{error()}}</p><p class="success" role="status">{{message()}}</p>
  @if(loading()){<p class="panel-note" role="status">Loading this device and its correction history…</p>}
  @if(device();as d){
    <section class="device-summary"><div><h3>{{d.unit.description}}</h3><span class="badge">{{d.unit.status}}</span></div><ul>@for(identity of d.unit.identifiers;track identity){<li>{{identity}}</li>}</ul><p class="small muted">These identities stay attached to this physical device.</p></section>
    @if(!d.canEdit){<p class="panel-note">{{d.unit.status!=='InStock'?'This device is unavailable for editing. Use the appropriate return or transfer receipt workflow first.':'Your account can view this device. An authorized owner or employee can correct its details.'}}</p>}
    <form [formGroup]="form" (ngSubmit)="save()">
      <fieldset class="plain-fieldset" [disabled]="saving()||loading()||!d.canEdit">
        <section class="counter-section"><h3>Device specifications</h3><p class="small muted">Correct this device only. Other phones and posted invoices keep their existing details.</p>
          <div class="form-grid three"><label>Colour<input formControlName="color" maxlength="100" placeholder="e.g. Green"></label><label>RAM<input formControlName="ram" maxlength="50" placeholder="e.g. 8 GB"></label><label>ROM / storage<input formControlName="storage" maxlength="50" placeholder="e.g. 128 GB"></label></div>
        </section>
        <section class="counter-section"><h3>Inspection & condition</h3><div class="form-grid"><label>Condition<select formControlName="condition">@if(d.unit.condition==='New'){<option value="New">New</option>}@else{<option value="Used">Used</option><option value="Refurbished">Refurbished</option><option value="OpenBox">Open box</option>}</select></label><label>Battery health (%)<input formControlName="batteryHealth" aria-label="Battery health (%)" aria-describedby="device-battery-help" type="number" min="0" max="100" step="1" placeholder="Unknown"><small id="device-battery-help" class="muted">Leave blank if it has not been assessed.</small></label></div>
          @if(form.controls.batteryHealth.invalid){<p class="small text-danger" role="alert">Enter a whole number from 0 to 100.</p>}
          <label>Inspection / accessories / repair notes<textarea formControlName="notes" rows="4" maxlength="2000" placeholder="Screen, cameras, battery, replaced parts, locks and accessories"></textarea></label>
          @if(d.unit.condition!=='New'){<p class="small warranty-note">Used phones are sold without warranty.</p>}
        </section>
        @if(d.canEdit){<label>Reason for correction<textarea formControlName="reason" rows="2" maxlength="1000" placeholder="e.g. Box label checked; colour was entered incorrectly" required></textarea></label>}
      </fieldset>
      <p class="small muted device-guardrail">IMEI, serial, model, stock quantity and purchase cost are protected. Each saved correction records who changed it, when and why.</p>
      <div class="form-footer"><button class="secondary" type="button" [disabled]="saving()" (click)="close()">Close</button><button class="secondary" type="button" [disabled]="saving()||loading()" (click)="reload()">Reload details</button>@if(d.canEdit){<button class="primary" [disabled]="saving()||loading()||form.invalid||!form.dirty">{{saving()?'Saving correction…':'Save correction'}}</button>}</div>
    </form>
    <section class="device-corrections"><h3>Correction history</h3><p class="small muted">Latest 100 corrections recorded in this branch.</p>
      @for(change of d.history;track change.id){<article class="correction-entry"><header><strong>{{change.actor}}</strong><time>{{change.createdAtUtc|date:'dd MMM yyyy, HH:mm'}}</time></header><p class="correction-reason">{{change.reason}}</p><dl>@for(field of changedFields(change);track field.key){<div><dt>{{field.label}}</dt><dd><span class="muted">{{field.before}}</span><span aria-hidden="true"> → </span><strong>{{field.after}}</strong></dd></div>}</dl></article>}@empty{<p class="empty-note">No corrections yet. Original receipt and inspection records are retained.</p>}
    </section>
  }@else if(!loading()){<button type="button" class="secondary" (click)="load()">Try loading again</button>}
</invora-modal>`})
export class DeviceEditor implements OnInit {
  readonly deviceId=input.required<string>();readonly closed=output<void>();readonly saved=output<void>();
  private api=inject(ApiService);private branch=inject(BranchContext);private fb=inject(FormBuilder);
  readonly device=signal<Device|null>(null);readonly loading=signal(false);readonly saving=signal(false);readonly error=signal('');readonly message=signal('');
  readonly form=this.fb.nonNullable.group({ram:[''],storage:[''],color:[''],condition:['New'],batteryHealth:['',[Validators.min(0),Validators.max(100),Validators.pattern(/^\d{1,3}$/)]],notes:[''],reason:['',[Validators.required,Validators.pattern(/\S/),Validators.maxLength(1000)]]});
  ngOnInit(){void this.load();}
  async load(){this.loading.set(true);this.error.set('');try{const d=await this.api.get<Device>('/inventory/'+this.deviceId()+'/details?'+this.branch.query());this.device.set(d);this.form.reset({...d.current,batteryHealth:d.current.batteryHealth===null?'':String(d.current.batteryHealth),reason:''});}catch(e){this.error.set(errorText(e));}finally{this.loading.set(false);}}
  async reload(){if(!this.form.dirty||confirm('Discard these unsaved edits and reload the latest device details?'))await this.load();}
  close(){if(!this.saving()&&(!this.form.dirty||confirm('Discard the unsaved device correction?')))this.closed.emit();}
  async save(){const d=this.device();if(!d?.canEdit||this.saving()||this.loading()||this.form.invalid)return;this.saving.set(true);this.error.set('');this.message.set('');try{const v=this.form.getRawValue();await this.api.mutate('/inventory/'+this.deviceId()+'/corrections',{...v,branchId:this.branch.id(),revision:d.revision,batteryHealth:v.batteryHealth===''?null:Number(v.batteryHealth)});await this.load();this.message.set('Device correction saved. Stock quantity and money are unchanged.');this.saved.emit();}catch(e){this.error.set(errorText(e));}finally{this.saving.set(false);}}
  changedFields(change:Revision){const labels:Record<keyof Details,string>={color:'Colour',ram:'RAM',storage:'Storage',condition:'Condition',batteryHealth:'Battery health',notes:'Inspection notes'};return (Object.keys(labels) as (keyof Details)[]).filter(key=>change.before[key]!==change.after[key]).map(key=>({key,label:labels[key],before:this.value(change.before[key],key),after:this.value(change.after[key],key)}));}
  private value(value:unknown,key:string){return value===null||value===''?'Not recorded':String(value)+(key==='batteryHealth'?'%':'');}
}
