import {Component,ElementRef,afterNextRender,input,output,viewChild} from '@angular/core';
@Component({selector:'invora-modal',standalone:true,template:`<dialog #dialog class="counter-dialog" (cancel)="cancel($event)"><header class="counter-modal-header"><div><p class="eyebrow">{{eyebrow()}}</p><h2>{{title()}}</h2></div><button type="button" class="quiet" aria-label="Close dialog" [disabled]="busy()" (click)="closed.emit()">×</button></header><div class="counter-modal-body"><ng-content/></div></dialog>`})
export class ModalFrame {
 readonly title=input.required<string>();readonly eyebrow=input('AT YOUR COUNTER');readonly busy=input(false);readonly closed=output<void>();private dialog=viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
 constructor(){afterNextRender(()=>this.dialog().nativeElement.showModal());}
 cancel(event:Event){event.preventDefault();if(!this.busy())this.closed.emit();}
}
