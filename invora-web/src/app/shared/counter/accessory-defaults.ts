import {Component,inject,output,signal,OnInit} from '@angular/core';
import {FormBuilder,ReactiveFormsModule,Validators} from '@angular/forms';
import {ApiService,errorText} from '../../core/api';
import {CatalogCategory,Tax} from '../../core/models';
import {ModalFrame} from './modal-frame';
import {AccessoryPreset} from './quick-accessory';

@Component({selector:'invora-accessory-defaults',standalone:true,imports:[ModalFrame,ReactiveFormsModule],template:`
<invora-modal title="Category billing defaults" eyebrow="MAKE REPEAT ENTRY QUICKER" [busy]="busy()" (closed)="closed.emit()">
 <p class="modal-intro muted">Save the HSN and tax for each specific category you sell, such as phone cases or USB cables. New accessories reuse these details; existing products and invoices keep their own billing details.</p>
 @if(loading()){<p role="status">Loading defaults…</p>}
 <form [formGroup]="form" (ngSubmit)="save()"><fieldset class="plain-fieldset" [disabled]="busy()||loading()">
 <label>Category<input formControlName="category" list="billing-default-categories" maxlength="100" placeholder="e.g. Phone cases" (change)="selectCategory()"><datalist id="billing-default-categories">@for(name of categories();track name){<option [value]="name"></option>}</datalist></label>
 <div class="form-grid"><label>HSN code<input formControlName="hsn" inputmode="numeric" maxlength="8" placeholder="4, 6 or 8 digits"></label><label>Tax rate<select formControlName="taxRateId" aria-label="Tax rate"><option value="">Choose tax</option>@for(tax of taxes();track tax.id){<option [value]="tax.id">{{tax.name}} · {{tax.rate}}%</option>}</select></label></div>
 <button class="primary" type="submit" [disabled]="form.invalid">Save category default</button></fieldset></form>
 <p class="error" role="alert">{{error()}}</p><p class="success" role="status">{{success()}}</p>
 <section class="preset-list" aria-label="Saved category billing defaults">@for(preset of presets();track preset.category){<article><div><strong>{{preset.category}}</strong><small>HSN {{preset.hsn}} · {{taxName(preset.taxRateId)}}</small></div><button type="button" class="text-link" [disabled]="busy()" (click)="edit(preset)">Edit</button><button type="button" class="text-link" [disabled]="busy()" (click)="remove(preset)">Clear default</button></article>}@empty{<p class="small muted">No defaults saved yet. Configure the categories you use most.</p>}</section>
</invora-modal>`,styles:[`.preset-list{margin-top:24px}.preset-list article{display:flex;align-items:center;gap:12px;border-top:1px solid var(--line);padding:15px 0}.preset-list article>div{flex:1;min-width:0;overflow-wrap:anywhere}.preset-list small{color:var(--muted)}form{margin-bottom:20px}.preset-list button{min-height:44px}@media(max-width:600px){.preset-list article{flex-wrap:wrap}.preset-list article>div{flex-basis:100%}input,select{font-size:16px}}`]})
export class AccessoryDefaults implements OnInit {
 readonly closed=output<void>();private api=inject(ApiService);readonly busy=signal(false);readonly loading=signal(true);readonly error=signal('');readonly success=signal('');readonly presets=signal<AccessoryPreset[]>([]);readonly taxes=signal<Tax[]>([]);readonly categories=signal<string[]>([]);
 readonly form=inject(FormBuilder).nonNullable.group({category:['',[Validators.required,Validators.maxLength(100)]],hsn:['',[Validators.required,Validators.pattern(/^(?:\d{4}|\d{6}|\d{8})$/)]],taxRateId:['',Validators.required]});
 async ngOnInit(){try{const [presets,taxes,categories]=await Promise.all([this.api.get<AccessoryPreset[]>('/accessory-billing-defaults'),this.api.get<Tax[]>('/tax-rates'),this.api.get<CatalogCategory[]>('/categories')]);this.presets.set(presets);this.taxes.set(taxes.filter(x=>(x as Tax&{isActive?:boolean}).isActive!==false));this.categories.set([...new Set([...categories.filter(c=>!c.requiresImei).map(c=>c.name),...presets.map(p=>p.category)])].sort());}catch(e){this.error.set(errorText(e));}finally{this.loading.set(false);}}
 taxName(id:string){return this.taxes().find(t=>t.id===id)?.name??'Tax unavailable — choose another';}
 edit(preset:AccessoryPreset){this.form.patchValue(preset);this.success.set('');}
 selectCategory(){const preset=this.presets().find(p=>p.category.toLowerCase()===this.form.controls.category.value.trim().toLowerCase());this.form.patchValue({hsn:preset?.hsn??'',taxRateId:preset?.taxRateId??''});}
 async save(){if(this.form.invalid||this.busy())return;await this.update({...this.form.getRawValue(),remove:false});}
 async remove(preset:AccessoryPreset){if(this.busy())return;await this.update({category:preset.category,remove:true});}
 private async update(request:unknown){this.busy.set(true);this.error.set('');this.success.set('');try{this.presets.set(await this.api.mutate<AccessoryPreset[]>('/accessory-billing-defaults',request,'PUT'));this.success.set('Category defaults updated. Existing products keep their billing details.');}catch(e){this.error.set(errorText(e));}finally{this.busy.set(false);}}
}
