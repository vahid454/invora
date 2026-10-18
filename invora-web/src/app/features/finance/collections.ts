import {Component,inject,signal,input,effect} from '@angular/core';
import {CurrencyPipe,DatePipe} from '@angular/common';
import {FormBuilder,FormControl,ReactiveFormsModule,Validators} from '@angular/forms';
import {RouterLink} from '@angular/router';
import {ApiService,BranchContext,errorText} from '../../core/api';
import {AuthService} from '../../core/auth';
import {Party,Row} from '../../core/models';
import {PartyPicker} from '../../shared/counter/party-picker';
import {ModalFrame} from '../../shared/counter/modal-frame';

type Due={id:string;kind:string;customerId:string;customerName:string;phone:string;alternatePhone:string;reference:string;description:string;financeReference:string;businessDate:string;dueDate:string|null;promiseDate:string|null;nextContactDate:string|null;amount:string;paid:string;outstanding:string;unallocatedCredit:string;accountBalance:string;cancelled:boolean;safetyStatus:string};
type Report={items:Due[];totalItems:number;totalOutstanding:string;customers:number;unallocatedCredit:string;today:string};
type ImportPreview={id:string;status:string;rowCount:number;totalAmount:string;errors:{row:number;message:string}[];preview:{name:string;phone:string;alternatePhone:string;reference:string;amount:string;dueDate:string;customerId:string|null}[]};
type Reminder={customerName:string;phone:string;alternatePhone:string;amount:string;message:string;deliveryStatus:string};
@Component({selector:'invora-collections',standalone:true,imports:[CurrencyPipe,DatePipe,ReactiveFormsModule,RouterLink,PartyPicker,ModalFrame],templateUrl:'./collections.html',styleUrl:'./collections.css'})
export class Collections {
 readonly unified=input(false);readonly searchTerm=input('');
 readonly auth=inject(AuthService);private api=inject(ApiService);private branch=inject(BranchContext);private fb=inject(FormBuilder);
 readonly rows=signal<Due[]>([]);readonly total=signal(0);readonly totalOutstanding=signal('0');readonly customerCount=signal(0);readonly unallocatedCredit=signal('0');readonly loading=signal(false);readonly busy=signal(false);readonly error=signal('');readonly notice=signal('');readonly today=signal(new Date().toLocaleDateString('en-CA',{timeZone:'Asia/Kolkata'}));
 readonly filters=this.fb.nonNullable.group({search:[''],status:['open'],from:[''],to:['']});page=1;private version=0;private timer:ReturnType<typeof setTimeout>|undefined;
 readonly selected=signal(new Map<string,string>());readonly modal=signal<'entry'|'import'|'promise'|'cancel'|'queue'|null>(null);readonly target=signal<Due|null>(null);readonly modalError=signal('');readonly history=signal<Row[]>([]);
 readonly form=this.fb.nonNullable.group({customerId:['',Validators.required],reference:['',[Validators.required,Validators.maxLength(100)]],description:['',[Validators.required,Validators.maxLength(500)]],financeReference:['',Validators.maxLength(200)],amount:['',[Validators.required,Validators.min(0.01)]],businessDate:[this.today(),Validators.required],dueDate:['',Validators.required]});
 readonly csv=new FormControl('',{nonNullable:true});readonly preview=signal<ImportPreview|null>(null);readonly reviewed=new FormControl(false,{nonNullable:true});
 readonly promise=this.fb.nonNullable.group({kind:['Promise'],promiseDate:[''],nextContactDate:[''],note:['',Validators.required]});readonly cancelReason=new FormControl('',{nonNullable:true,validators:Validators.required});
 readonly queue=signal<{id:string;name:string}[]>([]);readonly queueIndex=signal(0);readonly draft=signal<Reminder|null>(null);readonly draftLoading=signal(false);readonly phone=new FormControl('',{nonNullable:true});readonly message=new FormControl('',{nonNullable:true});
 constructor(){void this.load();effect(()=>{if(!this.unified())return;const term=this.searchTerm();if(this.filters.controls.search.value!==term){this.filters.controls.search.setValue(term,{emitEvent:false});this.page=1;void this.load();}});this.csv.valueChanges.subscribe(()=>{this.preview.set(null);this.reviewed.setValue(false);});}
 n(v:unknown){return Number(v??0);}s(v:unknown){return String(v??'');}
 query(){const f=this.filters.getRawValue();return this.branch.query()+'&search='+encodeURIComponent(f.search)+'&status='+f.status+(f.from?'&from='+f.from:'')+(f.to?'&to='+f.to:'');}
 async load(){const version=++this.version;this.loading.set(true);this.error.set('');try{const report=await this.api.get<Report>('/lenden/collections?'+this.query()+'&page='+this.page);if(version!==this.version)return;this.rows.set(report.items);this.total.set(report.totalItems);this.totalOutstanding.set(report.totalOutstanding);this.customerCount.set(report.customers);this.unallocatedCredit.set(report.unallocatedCredit);this.today.set(report.today);}catch(e){if(version===this.version){this.rows.set([]);this.error.set(errorText(e));}}finally{if(version===this.version)this.loading.set(false);}}
 filterChanged(debounce=false){this.page=1;this.selected.set(new Map());this.version++;clearTimeout(this.timer);if(debounce)this.timer=setTimeout(()=>void this.load(),200);else void this.load();}
 reset(){this.filters.reset({search:this.unified()?this.searchTerm():'',status:'open',from:'',to:''});this.filterChanged();}
 overdue(row:Due){const date=row.promiseDate??row.dueDate;return this.n(row.outstanding)>0&&!!date&&date<this.today();}
 toggle(row:Due,checked:boolean){this.selected.update(current=>{const next=new Map(current);if(checked)next.set(row.customerId,row.customerName);else next.delete(row.customerId);return next;});}
 eligible(row:Due){return this.n(row.outstanding)>0&&this.n(row.unallocatedCredit)===0&& !row.cancelled&&row.safetyStatus==='Clear';}
 clearSelection(){this.selected.set(new Map());}
 selectPage(){this.selected.update(current=>{const next=new Map(current);for(const row of this.rows())if(this.eligible(row))next.set(row.customerId,row.customerName);return next;});}
 async allocateCredit(row:Due){if(this.busy())return;this.busy.set(true);this.error.set('');try{const result=await this.api.mutate<{applied:string}>('/payments/allocate-account-credit',{branchId:this.branch.id(),customerId:row.customerId});this.notice.set('Existing receipts applied to oldest dues: ₹'+result.applied+'. No new money was recorded.');this.selected.set(new Map());await this.load();}catch(e){this.error.set(errorText(e));}finally{this.busy.set(false);}}
 async export(){this.busy.set(true);this.error.set('');try{await this.api.download('/lenden/collections/export?'+this.query(),'invora-collections-'+this.today()+'.csv');}catch(e){this.error.set(errorText(e));}finally{this.busy.set(false);}}
 open(kind:'entry'|'import'){this.modalError.set('');this.notice.set('');this.modal.set(kind);if(kind==='entry')this.form.reset({customerId:'',reference:'',description:'',financeReference:'',amount:'',businessDate:this.today(),dueDate:''});else{this.csv.setValue('');this.preview.set(null);}}
 close(){if(this.busy())return;this.modal.set(null);this.draft.set(null);this.queue.set([]);}
 choose(p:Party){this.form.controls.customerId.setValue(p.id);}
 async save(){if(this.form.invalid||this.busy())return;this.busy.set(true);this.modalError.set('');try{await this.api.mutate('/lenden/existing-dues',{branchId:this.branch.id(),...this.form.getRawValue()});this.modal.set(null);this.notice.set('Existing due recorded. No sale, stock change or cash movement was created.');await this.load();}catch(e){this.modalError.set(errorText(e));}finally{this.busy.set(false);}}
 template(){const text='name,phone,alternatePhone,reference,description,financeReference,amount,originalDate,dueDate\r\n';this.api.saveBlob(new Blob(['\uFEFF'+text],{type:'text/csv;charset=utf-8'}),'invora-existing-dues-template.csv');}
 async readFile(event:Event){this.modalError.set('');const file=(event.target as HTMLInputElement).files?.[0];if(!file)return;if(file.size>1000000){this.modalError.set('Use a CSV file smaller than 1 MB.');return;}try{this.csv.setValue(await file.text());}catch{this.modalError.set('Could not read the file. Choose it again.');}}
 async validate(){if(this.busy())return;this.busy.set(true);this.modalError.set('');this.preview.set(null);this.reviewed.setValue(false);try{this.preview.set(await this.api.mutate<ImportPreview>('/lenden/imports/validate',{branchId:this.branch.id(),csv:this.csv.value}));}catch(e){this.modalError.set(errorText(e));}finally{this.busy.set(false);}}
 async commit(){const preview=this.preview();if(!preview||preview.status!=='Validated'||!this.reviewed.value||this.busy())return;this.busy.set(true);this.modalError.set('');try{await this.api.mutate('/lenden/imports/'+preview.id+'/commit',{});this.modal.set(null);this.notice.set(preview.rowCount+' existing dues imported. Sales, stock and cash were unchanged.');this.page=1;await this.load();}catch(e){this.modalError.set(errorText(e));}finally{this.busy.set(false);}}
 async openPromise(row:Due){this.target.set(row);this.modalError.set('');this.history.set([]);this.promise.reset({kind:row.safetyStatus==='Clear'?'Promise':'Note',promiseDate:row.safetyStatus==='Clear'?row.promiseDate??row.dueDate??'':'',nextContactDate:'',note:''});this.modal.set('promise');try{const data=await this.api.get<{items:Row[]}>('/lenden/collections/'+row.id+'/history?'+this.branch.query()+'&kind='+row.kind);if(this.target()?.id===row.id)this.history.set(data.items);}catch(e){this.modalError.set(errorText(e));}}
 offset(days:number){const date=new Date(this.today()+'T12:00:00Z');date.setUTCDate(date.getUTCDate()+days);this.promise.controls.promiseDate.setValue(date.toISOString().slice(0,10));}
 async savePromise(){const row=this.target(),v=this.promise.getRawValue();if(!row||this.promise.invalid||this.busy()||(v.kind==='Promise'&&!v.promiseDate))return;this.busy.set(true);this.modalError.set('');try{await this.api.mutate('/lenden/followups',{branchId:this.branch.id(),customerId:row.customerId,...v,promiseDate:v.kind==='Promise'?v.promiseDate:null,nextContactDate:v.nextContactDate||null,saleId:row.kind==='invoice'?row.id:null,existingDueId:row.kind==='existing-due'?row.id:null});this.modal.set(null);await this.load();}catch(e){this.modalError.set(errorText(e));}finally{this.busy.set(false);}}
 openCancel(row:Due){this.target.set(row);this.cancelReason.setValue('');this.modalError.set('');this.modal.set('cancel');}
 async cancel(){if(this.cancelReason.invalid||!this.target()||this.busy())return;this.busy.set(true);this.modalError.set('');try{await this.api.mutate('/lenden/existing-dues/'+this.target()!.id+'/cancel',{reason:this.cancelReason.value});this.modal.set(null);this.notice.set('Entry cancelled with a reversing ledger entry. Its history is retained.');await this.load();}catch(e){this.modalError.set(errorText(e));}finally{this.busy.set(false);}}
 startQueue(){this.queue.set([...this.selected()].map(([id,name])=>({id,name})));this.queueIndex.set(0);this.modal.set('queue');void this.loadDraft();}
 async loadDraft(){const entry=this.queue()[this.queueIndex()];if(!entry)return;this.draft.set(null);this.draftLoading.set(true);this.modalError.set('');this.message.setValue('');try{const draft=await this.api.get<Reminder>('/lenden/collections/'+entry.id+'/reminder?'+this.branch.query());if(this.modal()!=='queue'||this.queue()[this.queueIndex()]?.id!==entry.id)return;this.draft.set(draft);this.phone.setValue(draft.phone);this.message.setValue(draft.message);}catch(e){this.modalError.set(errorText(e));}finally{this.draftLoading.set(false);}}
 async contact(kind:'whatsapp'|'sms'|'call'){
  const entry=this.queue()[this.queueIndex()],current=this.draft(),number=this.dial();
  if(!entry||!current||!number||!this.message.value.trim()||this.draftLoading())return;
  const tab=kind==='whatsapp'?window.open('','_blank'):null;
  if(tab)tab.opener=null;
  this.draftLoading.set(true);this.modalError.set('');
  try{
   const fresh=await this.api.get<Reminder>('/lenden/collections/'+entry.id+'/reminder?'+this.branch.query());
   if(this.queue()[this.queueIndex()]?.id!==entry.id||this.modal()!=='queue'){tab?.close();return;}
   if(fresh.amount!==current.amount||fresh.phone!==current.phone||fresh.alternatePhone!==current.alternatePhone){tab?.close();this.draft.set(fresh);this.phone.setValue(fresh.phone);this.message.setValue(fresh.message);this.modalError.set('Balance or contact changed. Review the refreshed draft before opening it.');return;}
   const link=kind==='whatsapp'?this.whatsapp():kind==='sms'?this.sms():'tel:+'+number;
   if(tab)tab.location.assign(link);else if(kind==='whatsapp')this.modalError.set('Allow pop-ups to open the WhatsApp draft.');else window.location.assign(link);
  }catch(e){tab?.close();this.draft.set(null);this.modalError.set(errorText(e));}finally{this.draftLoading.set(false);}
 }
 next(){if(this.queueIndex()+1>=this.queue().length){this.close();this.selected.set(new Map());this.notice.set('Reminder review finished. WhatsApp delivery is not tracked by Invora.');return;}this.queueIndex.update(x=>x+1);void this.loadDraft();}
 dial(){let digits=this.phone.value.replace(/\D/g,'');if(digits.length===10)digits='91'+digits;return /^[1-9]\d{6,14}$/.test(digits)?digits:'';}
 whatsapp(){return 'https://wa.me/'+this.dial()+'?text='+encodeURIComponent(this.message.value);}
 sms(){return 'sms:+'+this.dial()+'?body='+encodeURIComponent(this.message.value);}
 ngOnDestroy(){this.version++;clearTimeout(this.timer);}
}
