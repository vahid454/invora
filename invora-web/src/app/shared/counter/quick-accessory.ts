import {Component,inject,input,output,signal,OnInit,OnDestroy} from '@angular/core';
import {CurrencyPipe} from '@angular/common';
import {FormBuilder,ReactiveFormsModule,Validators} from '@angular/forms';
import {ApiService,BranchContext,errorText} from '../../core/api';
import {AuthService} from '../../core/auth';
import {CatalogCategory,Page,Product,Tax} from '../../core/models';
import {ModalFrame} from './modal-frame';

export interface AccessoryPreset {category:string;hsn:string;taxRateId:string}
export interface QuickAccessoryResult {product:Product;quantityAdded:number;onHand:number}

@Component({selector:'invora-quick-accessory',standalone:true,imports:[ModalFrame,ReactiveFormsModule,CurrencyPipe],templateUrl:'./quick-accessory.html',styleUrl:'./quick-accessory.css'})
export class QuickAccessory implements OnInit,OnDestroy {
 readonly mode=input<'sale'|'stock'>('stock');readonly product=input<Product|null>(null);readonly initialName=input('');readonly initialCategory=input('');
 readonly saved=output<QuickAccessoryResult>();readonly closed=output<void>();readonly advanced=output<void>();
 readonly auth=inject(AuthService);private api=inject(ApiService);private branch=inject(BranchContext);private fb=inject(FormBuilder);
 readonly loading=signal(true);readonly busy=signal(false);readonly error=signal('');readonly selected=signal<Product|null>(null);readonly matches=signal<Product[]>([]);readonly searching=signal(false);readonly available=signal<number|null>(null);
 readonly categories=signal<CatalogCategory[]>([]);readonly taxes=signal<Tax[]>([]);readonly presets=signal<AccessoryPreset[]>([]);readonly showBilling=signal(false);readonly showOptions=signal(false);
 private timer:ReturnType<typeof setTimeout>|undefined;private searchVersion=0;private destroyed=false;
 private readonly money=[Validators.required,Validators.min(0),Validators.max(99999999999999),Validators.pattern(/^(?:\d+|\d*\.\d{1,2})$/)];
 readonly form=this.fb.nonNullable.group({name:['',[Validators.required,Validators.maxLength(200)]],category:['Accessory',[Validators.required,Validators.maxLength(100)]],sellingPrice:['',this.money],quantity:['1',[Validators.required,Validators.min(1),Validators.max(100000),Validators.pattern(/^\d+$/)]],unitCost:['',this.money],brand:['Generic',[Validators.required,Validators.maxLength(100)]],barcode:['',Validators.maxLength(100)],mrp:[''],warrantyMonths:['0',[Validators.min(0),Validators.max(120),Validators.pattern(/^\d+$/)]],hsn:[''],taxRateId:[''],rememberBilling:[false]});
 async ngOnInit(){try{
  const [categories,taxes,presets]=await Promise.all([this.api.get<CatalogCategory[]>('/categories'),this.api.get<Tax[]>('/tax-rates'),this.api.get<AccessoryPreset[]>('/accessory-billing-defaults')]);
  this.categories.set(categories.filter(x=>!x.requiresImei&&!['mobile','smartphone','mobile phone','phone'].includes(x.name.toLowerCase())));this.taxes.set(taxes.filter(x=>(x as Tax&{isActive?:boolean}).isActive!==false));this.presets.set(presets);
  let remembered='';try{remembered=localStorage.getItem('invora.accessory.category')??'';}catch{/* Browser preferences are optional. */}
  const category=this.initialCategory()||remembered||'Accessory';
  this.form.patchValue({name:this.initialName(),category:this.categories().some(c=>c.name===category)||presets.some(p=>p.category===category)?category:'Accessory'});this.categoryChanged();
  if(this.product())await this.choose(this.product()!);else if(this.initialName())this.queueSearch();
 }catch(e){this.error.set(errorText(e));}finally{this.loading.set(false);}}
 ngOnDestroy(){this.destroyed=true;clearTimeout(this.timer);this.searchVersion++;}
 categoryNames(){return [...new Set(['Accessory',...this.categories().map(x=>x.name),...this.presets().map(x=>x.category)])].sort();}
 categoryChanged(){const preset=this.presets().find(x=>x.category.toLowerCase()===this.form.controls.category.value.trim().toLowerCase());this.form.patchValue({hsn:preset?.hsn??'',taxRateId:this.taxes().some(x=>x.id===preset?.taxRateId)?preset!.taxRateId:'',rememberBilling:false});this.showBilling.set(!this.billingReady());}
 billingReady(){return /^(?:\d{4}|\d{6}|\d{8})$/.test(this.form.controls.hsn.value.trim())&&this.taxes().some(t=>t.id===this.form.controls.taxRateId.value);}
 billingText(){const tax=this.taxes().find(t=>t.id===this.form.controls.taxRateId.value);return `HSN ${this.form.controls.hsn.value} · ${tax?.name??'Choose tax'}${Number(tax?.cessRate)?' + cess':''}`;}
 queueSearch(){clearTimeout(this.timer);const version=++this.searchVersion;this.matches.set([]);const term=this.form.controls.name.value.trim();if(term.length<2){this.searching.set(false);return;}this.searching.set(true);this.timer=setTimeout(async()=>{try{const response=await this.api.get<Page<Product>>('/products?pageSize=10&search='+encodeURIComponent(term));if(version===this.searchVersion)this.matches.set(response.items.filter(x=>!x.serialized&&!x.requiresImei));}catch(e){if(version===this.searchVersion)this.error.set(errorText(e));}finally{if(version===this.searchVersion)this.searching.set(false);}},200);}
 async choose(product:Product){this.searchVersion++;clearTimeout(this.timer);this.matches.set([]);this.searching.set(false);this.selected.set(product);this.error.set('');this.form.patchValue({name:product.name,category:product.category,sellingPrice:String(product.sellingPrice),brand:product.brand,hsn:product.hsn,taxRateId:product.taxRateId,rememberBilling:false});this.available.set(null);try{const quantities=await this.api.get<Record<string,number>>('/inventory/quantity-availability?'+this.branch.query()+'&ids='+product.id);if(this.selected()?.id===product.id)this.available.set(quantities[product.id]??0);}catch(e){this.error.set(errorText(e));}}
 chooseNew(){this.selected.set(null);this.available.set(null);this.form.patchValue({name:'',sellingPrice:'',brand:'Generic'});this.categoryChanged();}
 valid(){return !this.loading()&&!this.form.invalid&&(!!this.selected()||this.billingReady());}
 async save(){if(this.busy()||!this.valid())return;this.busy.set(true);this.error.set('');const v=this.form.getRawValue();try{
  const result=await this.api.mutate<QuickAccessoryResult>('/inventory/quick-accessories',{branchId:this.branch.id(),quantity:Number(v.quantity),unitCost:v.unitCost,productVariantId:this.selected()?.id??null,product:this.selected()?null:{name:v.name.trim(),category:v.category.trim(),sellingPrice:v.sellingPrice,brand:v.brand.trim()||'Generic',hsn:v.hsn.trim(),taxRateId:v.taxRateId,barcode:v.barcode.trim()||null,mrp:String(v.mrp??'').trim()?v.mrp:null,warrantyMonths:Number(v.warrantyMonths)},rememberBilling:!this.selected()&&this.auth.can('business.settings')&&v.rememberBilling});
  try{localStorage.setItem('invora.accessory.category',result.product.category);}catch{/* A preference failure must not mask recorded stock. */}if(!this.destroyed)this.saved.emit(result);
 }catch(e){this.error.set(errorText(e));}finally{this.busy.set(false);}}
}
