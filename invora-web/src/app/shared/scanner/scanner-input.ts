import {AfterViewInit,OnChanges,Component,ElementRef,EventEmitter,Input,Output,ViewChild} from '@angular/core';
import {FormControl,ReactiveFormsModule} from '@angular/forms';
@Component({selector:'invora-scanner-input',standalone:true,imports:[ReactiveFormsModule],template:`<div class="scanner"><span class="scan-glyph" aria-hidden="true">▥</span><input #field [formControl]="value" [placeholder]="placeholder" aria-label="Scan IMEI, serial or barcode" (keydown)="key($event)" autocomplete="off"><button type="button" class="quiet" [disabled]="disabled" (click)="submit()">Scan ↵</button></div><small class="hint">USB / Bluetooth scanner · or type and press Enter</small>`})
export class ScannerInput implements AfterViewInit,OnChanges {
  @ViewChild('field')field!:ElementRef<HTMLInputElement>;@Input()placeholder='Scan an IMEI or barcode…';@Input()tabSuffix=false;@Input()disabled=false;@Output()scanned=new EventEmitter<string>();readonly value=new FormControl('',{nonNullable:true});
  ngOnChanges(){if(this.disabled)this.value.disable({emitEvent:false});else this.value.enable({emitEvent:false});}
  ngAfterViewInit(){this.field.nativeElement.focus();}key(event:KeyboardEvent){if(event.key==='Enter'||this.tabSuffix&&event.key==='Tab'){event.preventDefault();this.submit();}}submit(){if(this.disabled)return;const value=this.value.value.trim();if(value){this.scanned.emit(value);this.value.setValue('');}this.field.nativeElement.focus();}
}
