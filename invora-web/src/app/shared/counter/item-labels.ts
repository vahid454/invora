import {Component,computed,inject,input,OnInit,output,signal} from '@angular/core';
import {FormControl,ReactiveFormsModule} from '@angular/forms';
import {HttpErrorResponse} from '@angular/common/http';
import {ApiService,errorText} from '../../core/api';
import {ModalFrame} from './modal-frame';

interface ItemLabel {
  id:string;kind:'unit'|'product';code:string;model:string;details:string;category:string;
  condition:string;sku:string;identifiers:string[];qrPng:string;
}

@Component({selector:'invora-item-labels',standalone:true,imports:[ModalFrame,ReactiveFormsModule],
  template:`<invora-modal title="Print item stickers" eyebrow="LABEL ONCE · SCAN EVERY TIME" [busy]="busy()" (closed)="closed.emit()">
    <p class="muted label-intro">Items from this page are shown below. Each device keeps its own QR; quantity products share a SKU code. Reprint whenever needed.</p>
    @if(error()){<p class="error" role="alert">{{error()}}</p>}
    @if(loading()){<p role="status">Preparing labels…</p>}
    @if(labels().length){
      <div class="label-workspace">
        <section aria-label="Items to label" class="label-selection">
          <div class="label-list-heading"><strong>{{selected().length}} of {{labels().length}} selected</strong><button class="text-link" type="button" (click)="toggleAll()">{{selected().length===labels().length?'Clear selection':'Select all'}}</button></div>
          <div class="label-list">
            @for(item of labels();track item.id){
              <label class="label-choice" [class.chosen]="selected().includes(item.id)">
                <input type="checkbox" [checked]="selected().includes(item.id)" (change)="toggle(item.id)" [attr.aria-label]="'Label '+item.model+' '+(item.identifiers[0]||item.sku)">
                <span><strong>{{item.model}}</strong><small>{{item.details||item.category}}</small><small class="identity">{{item.identifiers[0]||item.sku}}</small></span>
              </label>
            }
          </div>
        </section>
        <section class="label-preview-panel" aria-label="Sticker preview">
          <p class="eyebrow">STICKER PREVIEW</p>
          @if(preview();as item){
            <div class="sticker-preview"><strong class="sticker-model">{{item.model}}</strong><div class="sticker-body"><img [src]="'data:image/png;base64,'+item.qrPng" alt="Item QR code" width="180" height="180"><div class="sticker-details"><span>{{item.details}}</span>@if(item.condition){<span>{{item.condition}}</span>}@for(identity of item.identifiers;track identity){<span class="identity">{{identity}}</span>}@if(item.kind==='product'){<span class="identity">SKU: {{item.sku}}</span><small>QUANTITY ITEM</small>}</div></div></div>
            <p class="small muted">Preview enlarged for readability. The PDF uses the selected physical size.</p>
          }@else{<p class="muted">Select an item to preview its sticker.</p>}
        </section>
      </div>
      <div class="label-settings"><label>Sticker size<select [formControl]="size"><option value="50x30">50 × 30 mm · compact</option><option value="60x40">60 × 40 mm · more details</option><option value="70x50">70 × 50 mm · long serials</option></select></label><label>Copies per item<input type="number" min="1" max="100" step="1" [formControl]="copies"></label><div class="label-count"><strong>{{totalCopies()}}</strong><span>stickers in this PDF</span></div></div>
      <p class="small muted label-print-note">Print at <strong>Actual size / 100%</strong>, one label per page. Match your printer's paper size and turn off “Fit to page”. Keep the white space around the QR. Prices are read from inventory when scanned.</p>
      @if(validation()){<p class="error" role="alert">{{validation()}}</p>}
      <div class="label-footer"><span class="success" role="status">{{success()}}</span><button type="button" class="secondary" [disabled]="busy()" (click)="closed.emit()">Close</button><button type="button" class="primary" [disabled]="busy()||!!validation()" (click)="download()">{{busy()?'Preparing PDF…':'Download print PDF ↓'}}</button></div>
    }
  </invora-modal>`,styleUrl:'./item-labels.css'})
export class ItemLabels implements OnInit {
  readonly branchId=input.required<string>();readonly kind=input.required<'unit'|'product'>();readonly ids=input.required<string[]>();readonly closed=output<void>();
  private api=inject(ApiService);readonly labels=signal<ItemLabel[]>([]);readonly selected=signal<string[]>([]);
  readonly loading=signal(true);readonly busy=signal(false);readonly error=signal('');readonly success=signal('');
  readonly size=new FormControl('50x30',{nonNullable:true});readonly copies=new FormControl(1,{nonNullable:true});
  readonly preview=computed(()=>this.labels().find(x=>this.selected().includes(x.id)));
  async ngOnInit(){try{const items=await this.api.get<ItemLabel[]>(this.path(this.ids()));this.labels.set(items);this.selected.set(items.map(x=>x.id));}catch(e){this.error.set(errorText(e));}finally{this.loading.set(false);}}
  private path(ids:string[]){return '/inventory/labels?'+new URLSearchParams({branchId:this.branchId(),kind:this.kind(),ids:ids.join(',')});}
  toggle(id:string){this.selected.update(ids=>ids.includes(id)?ids.filter(x=>x!==id):[...ids,id]);this.success.set('');}
  toggleAll(){this.selected.set(this.selected().length===this.labels().length?[]:this.labels().map(x=>x.id));this.success.set('');}
  totalCopies(){return this.selected().length*Math.max(0,Number(this.copies.value)||0);}
  validation(){return !this.selected().length?'Select at least one item.':!Number.isInteger(this.copies.value)||this.copies.value<1||this.copies.value>100?'Enter 1–100 whole copies per item.':this.totalCopies()>200?'Keep each download to 200 stickers or fewer.':'';}
  async download(){
    if(this.busy()||this.validation())return;this.busy.set(true);this.error.set('');this.success.set('');
    const size=this.size.value;const count=this.totalCopies();
    try{await this.api.download(this.path(this.selected()).replace('/labels?','/labels/pdf?')+'&size='+size+'&copies='+this.copies.value,'invora-labels-'+size+'.pdf');this.success.set(count+' stickers ready. Open the PDF to print.');}
    catch(e){let message=errorText(e);if(e instanceof HttpErrorResponse&&e.error instanceof Blob){try{const problem=JSON.parse(await e.error.text());message=problem.detail??problem.title??message;}catch{ /* Retain safe fallback. */ }}this.error.set(message);}
    finally{this.busy.set(false);}
  }
}
